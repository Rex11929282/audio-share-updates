import importlib
import io
import json
import sys
from dataclasses import dataclass
from pathlib import Path

import pytest


HELPER_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HELPER_DIRECTORY))


@dataclass
class Device:
    id: str
    name: str
    is_default: bool


@dataclass
class Session:
    process_id: int
    process_name: str


class RecordingRouter:
    def __init__(self, sessions=(), devices=()):
        self.sessions = list(sessions)
        self.devices = list(devices)
        self.get_calls = []
        self.set_calls = []
        self.clear_calls = []

    def list_app_sessions(self):
        return self.sessions

    def list_output_devices(self):
        return self.devices

    def get_app_output_device(self, *, process_id):
        self.get_calls.append(process_id)
        return {process_id: "previous-device"}

    def set_app_output_device(self, *, process_id, device):
        self.set_calls.append((process_id, device))

    def clear_app_output_device(self, *, process_id):
        self.clear_calls.append(process_id)


@pytest.fixture
def helper(monkeypatch):
    module = importlib.import_module("audio_share_router_helper")
    router = RecordingRouter()
    monkeypatch.setattr(module, "router", router)
    return module, router


def test_unknown_command_is_rejected(helper):
    module, router = helper

    assert module.handle({"command": "route-all"}) == {
        "ok": False,
        "error": "Unknown command.",
    }
    assert router.get_calls == []
    assert router.set_calls == []
    assert router.clear_calls == []


def test_set_route_rejects_discord_without_calling_router(helper):
    module, router = helper
    router.sessions = [Session(8, "discord.exe")]

    assert module.handle({"command": "set-route", "processId": 8, "deviceId": "input"}) == {
        "ok": False,
        "error": "Protected process cannot be routed.",
    }
    assert router.set_calls == []


@pytest.mark.parametrize(
    "process_name",
    [
        "DiscordCanary.exe",
        "VoicemodBeta.exe",
        "Voicemeeter8.exe",
        "VoicemeeterPro64.exe",
    ],
)
def test_set_route_rejects_protected_name_variants_without_calling_router(helper, process_name):
    module, router = helper
    router.sessions = [Session(8, process_name)]

    assert module.handle({"command": "set-route", "processId": 8, "deviceId": "input"}) == {
        "ok": False,
        "error": "Protected process cannot be routed.",
    }
    assert router.get_calls == []
    assert router.set_calls == []
    assert router.clear_calls == []


@pytest.mark.parametrize(
    "payload,error",
    [
        (None, "Malformed request."),
        ({"command": "get-route"}, "Missing processId."),
        ({"command": "set-route", "processId": 4}, "Missing deviceId."),
    ],
)
def test_malformed_request_is_rejected(helper, payload, error):
    module, _ = helper

    assert module.handle(payload) == {"ok": False, "error": error}


def test_missing_active_session_is_rejected(helper):
    module, router = helper

    assert module.handle({"command": "clear-route", "processId": 7}) == {
        "ok": False,
        "error": "Active output session not found.",
    }
    assert router.clear_calls == []


def test_list_devices_returns_json_safe_values(helper):
    module, router = helper
    router.devices = [Device("device-id", "Headphones", True)]

    response = module.handle({"command": "list-devices"})

    assert response == {
        "ok": True,
        "value": [{"id": "device-id", "name": "Headphones", "is_default": True}],
    }
    assert json.loads(json.dumps(response)) == response


def test_get_route_uses_the_active_session_pid(helper):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]

    assert module.handle({"command": "get-route", "processId": 9}) == {
        "ok": True,
        "value": "previous-device",
    }
    assert router.get_calls == [9]


def test_main_reads_one_request_and_writes_one_json_response(helper, monkeypatch):
    module, _ = helper
    stdout = io.StringIO()
    monkeypatch.setattr(module.sys, "stdin", io.StringIO('{"command":"health"}\n'))
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 0
    assert json.loads(stdout.getvalue()) == {
        "ok": True,
        "value": {"version": "1.1.1"},
    }


def test_main_reports_malformed_json_with_nonzero_exit(helper, monkeypatch):
    module, _ = helper
    stdout = io.StringIO()
    monkeypatch.setattr(module.sys, "stdin", io.StringIO("not json\n"))
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 1
    assert json.loads(stdout.getvalue()) == {"ok": False, "error": "Malformed JSON."}
