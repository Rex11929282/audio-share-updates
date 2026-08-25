import importlib
import io
import json
import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import pytest


HELPER_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HELPER_DIRECTORY))
PROCESS_START_UTC_TICKS = 638893440000000000
PROCESS_CREATE_TIME_SECONDS = 1753747200.0


@dataclass
class Device:
    id: str
    name: str
    is_default: bool


@dataclass
class Session:
    process_id: int
    process_name: str


@dataclass
class ProcessIdentityLease:
    process_name: str
    start_utc_ticks: int
    events: list | None = None
    active: bool = False

    def __enter__(self):
        self.active = True
        if self.events is not None:
            self.events.append("lease-enter")
        return self

    def __exit__(self, *_):
        if self.events is not None:
            self.events.append("lease-exit")
        self.active = False

    def name(self):
        assert self.active
        return self.process_name

    def create_time_utc_ticks(self):
        assert self.active
        return self.start_utc_ticks


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


class RecordingPolicy:
    def __init__(self):
        self.routes = {}
        self.get_calls = []
        self.set_calls = []
        self.restore_calls = []

    def get_route(self, process_id):
        self.get_calls.append(process_id)
        return self.routes.get(
            process_id,
            {"consoleDeviceId": None, "multimediaDeviceId": None},
        )

    def restore_route(self, process_id, route):
        self.restore_calls.append((process_id, route))

    def set_route(self, process_id, device_id):
        self.set_calls.append((process_id, device_id))


class RecordingPolicyFactory:
    def __init__(self, routes=None, set_failures=()):
        self.routes = routes or {}
        self.set_failures = set(set_failures)
        self.get_calls = []
        self.set_calls = []

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return None

    def get_persisted_default_endpoint(self, *, process_id, flow, role):
        self.get_calls.append((process_id, flow, role))
        return self.routes.get(role)

    def set_persisted_default_endpoint(
        self,
        *,
        process_id,
        flow,
        role,
        packed_device_id,
    ):
        self.set_calls.append((process_id, flow, role, packed_device_id))
        if role in self.set_failures:
            raise RuntimeError(f"role {role} failed")


@pytest.fixture
def helper(monkeypatch):
    module = importlib.import_module("audio_share_router_helper")
    router = RecordingRouter()
    policy = RecordingPolicy()
    monkeypatch.setattr(module, "router", router)
    monkeypatch.setattr(module, "policy", policy, raising=False)
    monkeypatch.setattr(
        module,
        "process_identity_lease_factory",
        lambda _: ProcessIdentityLease("chrome.exe", PROCESS_START_UTC_TICKS),
        raising=False,
    )
    router.policy = policy
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

    assert module.handle({
        "command": "set-route",
        "processId": 8,
        "processName": "discord.exe",
        "processStartUtcTicks": PROCESS_START_UTC_TICKS,
        "deviceId": "input",
    }) == {
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

    assert module.handle({
        "command": "set-route",
        "processId": 8,
        "processName": process_name,
        "processStartUtcTicks": PROCESS_START_UTC_TICKS,
        "deviceId": "input",
    }) == {
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


def test_route_requires_process_identity(helper):
    module, router = helper

    assert module.handle({"command": "clear-route", "processId": 7}) == {
        "ok": False,
        "error": "Missing process identity.",
    }
    assert router.clear_calls == []


def test_main_sanitizes_device_discovery_failure(helper, monkeypatch):
    module, router = helper
    stdout = io.StringIO()

    def fail_device_discovery():
        raise RuntimeError("untrusted router detail")

    monkeypatch.setattr(router, "list_output_devices", fail_device_discovery)
    monkeypatch.setattr(
        module.sys,
        "stdin",
        io.StringIO('{"command":"list-devices"}\n'),
    )
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 0
    assert json.loads(stdout.getvalue()) == {
        "ok": False,
        "error": "Router unavailable.",
    }


def test_set_route_allows_audible_process_on_a_nondefault_output(helper):
    module, router = helper
    module.process_identity_lease_factory = lambda _: ProcessIdentityLease(
        "cloudmusic.exe",
        PROCESS_START_UTC_TICKS,
    )

    assert module.handle(
        {
            "command": "set-route",
            "processId": 9,
            "processName": "cloudmusic.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            "deviceId": "input",
        }
    ) == {"ok": True, "value": None}
    assert router.policy.set_calls == [(9, "input")]


@pytest.mark.parametrize(
    "command,extra",
    [
        ("get-route", {}),
        ("set-route", {"deviceId": "input"}),
        (
            "restore-route",
            {
                "consoleDeviceId": None,
                "multimediaDeviceId": "previous-multimedia",
            },
        ),
    ],
)
@pytest.mark.parametrize(
    "process_name,process_start_utc_ticks,create_time_seconds",
    [
        ("other.exe", PROCESS_START_UTC_TICKS, PROCESS_CREATE_TIME_SECONDS),
        ("chrome.exe", PROCESS_START_UTC_TICKS + 10000000, PROCESS_CREATE_TIME_SECONDS),
    ],
)
def test_route_identity_mismatch_never_reads_or_writes(
    helper,
    monkeypatch,
    command,
    extra,
    process_name,
    process_start_utc_ticks,
    create_time_seconds,
):
    module, router = helper
    router.sessions = [Session(7, "chrome.exe")]
    monkeypatch.setattr(
        module,
        "process_identity_lease_factory",
        lambda _: ProcessIdentityLease(
            "chrome.exe",
            int(round(create_time_seconds * 10000000)) + module.UNIX_EPOCH_UTC_TICKS,
        ),
        raising=False,
    )
    request = {
        "command": command,
        "processId": 7,
        "processName": process_name,
        "processStartUtcTicks": process_start_utc_ticks,
        **extra,
    }

    assert module.handle(request) == {
        "ok": False,
        "error": "Process identity mismatch.",
    }
    assert router.policy.get_calls == []
    assert router.policy.set_calls == []
    assert router.policy.restore_calls == []
    assert router.set_calls == []
    assert router.clear_calls == []


@pytest.mark.parametrize(
    "command,extra,expected_policy_call",
    [
        ("get-route", {}, "get"),
        ("set-route", {"deviceId": "input"}, "set"),
        (
            "restore-route",
            {
                "consoleDeviceId": None,
                "multimediaDeviceId": "previous-multimedia",
            },
            "restore",
        ),
    ],
)
def test_process_identity_lease_is_held_through_policy_operation(
    helper,
    monkeypatch,
    command,
    extra,
    expected_policy_call,
):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]
    events = []
    lease = ProcessIdentityLease("chrome.exe", PROCESS_START_UTC_TICKS, events)

    class LeaseCheckingPolicy(RecordingPolicy):
        def get_route(self, process_id):
            assert lease.active
            events.append("get")
            return super().get_route(process_id)

        def set_route(self, process_id, device_id):
            assert lease.active
            events.append("set")
            super().set_route(process_id, device_id)

        def restore_route(self, process_id, route):
            assert lease.active
            events.append("restore")
            super().restore_route(process_id, route)

    policy = LeaseCheckingPolicy()
    monkeypatch.setattr(module, "policy", policy)
    monkeypatch.setattr(module, "process_identity_lease_factory", lambda _: lease)

    response = module.handle(
        {
            "command": command,
            "processId": 9,
            "processName": "chrome.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            **extra,
        }
    )

    assert response["ok"] is True
    assert events == ["lease-enter", expected_policy_call, "lease-exit"]
    assert lease.active is False


def test_process_identity_lease_is_released_when_policy_operation_fails(
    helper,
    monkeypatch,
):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]
    events = []
    lease = ProcessIdentityLease("chrome.exe", PROCESS_START_UTC_TICKS, events)

    class FailingPolicy(RecordingPolicy):
        def set_route(self, process_id, device_id):
            assert lease.active
            events.append("set")
            raise RuntimeError("raw policy detail")

    monkeypatch.setattr(module, "policy", FailingPolicy())
    monkeypatch.setattr(module, "process_identity_lease_factory", lambda _: lease)

    response = module.handle(
        {
            "command": "set-route",
            "processId": 9,
            "processName": "chrome.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            "deviceId": "input",
        }
    )

    assert response == {"ok": False, "error": "Routing request failed."}
    assert events == ["lease-enter", "set", "lease-exit"]
    assert lease.active is False


def test_list_devices_returns_json_safe_values(helper):
    module, router = helper
    router.devices = [Device("device-id", "Headphones", True)]

    response = module.handle({"command": "list-devices"})

    assert response == {
        "ok": True,
        "value": [{"id": "device-id", "name": "Headphones", "is_default": True}],
    }
    assert json.loads(json.dumps(response)) == response


def test_get_route_returns_console_and_multimedia_roles_for_the_active_session(helper):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]
    router.policy.routes[9] = {
        "consoleDeviceId": "previous-console",
        "multimediaDeviceId": "previous-multimedia",
    }

    assert module.handle(
        {
            "command": "get-route",
            "processId": 9,
            "processName": "chrome.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
        }
    ) == {
        "ok": True,
        "value": {
            "consoleDeviceId": "previous-console",
            "multimediaDeviceId": "previous-multimedia",
        },
    }
    assert router.policy.get_calls == [9]


def test_set_route_writes_through_the_dual_role_policy(helper):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]

    assert module.handle(
        {
            "command": "set-route",
            "processId": 9,
            "processName": "chrome",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            "deviceId": "input",
        }
    ) == {
        "ok": True,
        "value": None,
    }
    assert router.policy.set_calls == [(9, "input")]
    assert router.set_calls == []


def test_restore_route_writes_console_and_multimedia_roles_exactly(helper):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]
    route = {
        "consoleDeviceId": None,
        "multimediaDeviceId": "previous-multimedia",
    }

    assert module.handle(
        {
            "command": "restore-route",
            "processId": 9,
            "processName": "chrome.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            **route,
        }
    ) == {
        "ok": True,
        "value": None,
    }
    assert router.policy.restore_calls == [(9, route)]


@pytest.mark.parametrize(
    "payload",
    [
        {"command": "restore-route", "processId": 9},
        {
            "command": "restore-route",
            "processId": 9,
            "consoleDeviceId": 7,
            "multimediaDeviceId": None,
        },
        {
            "command": "restore-route",
            "processId": 9,
            "consoleDeviceId": None,
            "multimediaDeviceId": "",
        },
    ],
)
def test_restore_route_rejects_malformed_role_state(helper, payload):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]

    assert module.handle(payload) == {
        "ok": False,
        "error": "Invalid route state.",
    }
    assert router.policy.restore_calls == []


def test_persisted_route_policy_reads_console_and_multimedia_roles(helper):
    module, _ = helper
    factory = RecordingPolicyFactory({10: "packed-console", 20: "packed-multimedia"})
    policy = module._PersistedRoutePolicy.__new__(module._PersistedRoutePolicy)
    policy._com_initialized = lambda: factory
    policy._factory = lambda: factory
    policy._unpack_device_id = lambda value: value.removeprefix("packed-") if value else None
    policy._flow = 5
    policy._roles = {"consoleDeviceId": 10, "multimediaDeviceId": 20}

    route = policy.get_route(9)

    assert route == {
        "consoleDeviceId": "console",
        "multimediaDeviceId": "multimedia",
    }
    assert factory.get_calls == [(9, 5, 10), (9, 5, 20)]


def test_persisted_route_policy_restores_each_role_including_default(helper):
    module, _ = helper
    factory = RecordingPolicyFactory()
    policy = module._PersistedRoutePolicy.__new__(module._PersistedRoutePolicy)
    policy._com_initialized = lambda: factory
    policy._factory = lambda: factory
    policy._pack_device_id = lambda value: f"packed-{value}" if value else None
    policy._flow = 5
    policy._roles = {"consoleDeviceId": 10, "multimediaDeviceId": 20}

    policy.restore_route(
        9,
        {
            "consoleDeviceId": None,
            "multimediaDeviceId": "multimedia",
        },
    )

    assert factory.set_calls == [
        (9, 5, 10, None),
        (9, 5, 20, "packed-multimedia"),
    ]


def test_persisted_route_policy_set_attempts_both_roles_and_aggregates_failures(helper):
    module, _ = helper
    factory = RecordingPolicyFactory(set_failures=(10, 20))
    policy = module._PersistedRoutePolicy.__new__(module._PersistedRoutePolicy)
    policy._com_initialized = lambda: factory
    policy._factory = lambda: factory
    policy._pack_device_id = lambda value: f"packed-{value}"
    policy._flow = 5
    policy._roles = {"consoleDeviceId": 10, "multimediaDeviceId": 20}

    with pytest.raises(
        RuntimeError,
        match="Console and Multimedia role routing failed",
    ):
        policy.set_route(9, "input")

    assert factory.set_calls == [
        (9, 5, 10, "packed-input"),
        (9, 5, 20, "packed-input"),
    ]


def test_persisted_route_policy_restore_attempts_both_roles_and_aggregates_failures(helper):
    module, _ = helper
    factory = RecordingPolicyFactory(set_failures=(10, 20))
    policy = module._PersistedRoutePolicy.__new__(module._PersistedRoutePolicy)
    policy._com_initialized = lambda: factory
    policy._factory = lambda: factory
    policy._pack_device_id = lambda value: f"packed-{value}" if value else None
    policy._flow = 5
    policy._roles = {"consoleDeviceId": 10, "multimediaDeviceId": 20}

    with pytest.raises(
        RuntimeError,
        match="Console and Multimedia role routing failed",
    ):
        policy.restore_route(
            9,
            {
                "consoleDeviceId": None,
                "multimediaDeviceId": "multimedia",
            },
        )

    assert factory.set_calls == [
        (9, 5, 10, None),
        (9, 5, 20, "packed-multimedia"),
    ]


def test_role_failures_are_sanitized_and_preserved_in_protocol_response(
    helper,
    monkeypatch,
):
    module, router = helper
    router.sessions = [Session(9, "chrome.exe")]
    factory = RecordingPolicyFactory(set_failures=(10, 20))
    policy = module._PersistedRoutePolicy.__new__(module._PersistedRoutePolicy)
    policy._com_initialized = lambda: factory
    policy._factory = lambda: factory
    policy._pack_device_id = lambda value: f"packed-{value}"
    policy._flow = 5
    policy._roles = {"consoleDeviceId": 10, "multimediaDeviceId": 20}
    monkeypatch.setattr(module, "policy", policy)

    response = module.handle(
        {
            "command": "set-route",
            "processId": 9,
            "processName": "chrome.exe",
            "processStartUtcTicks": PROCESS_START_UTC_TICKS,
            "deviceId": "input",
        }
    )

    assert response == {
        "ok": False,
        "error": "Console and Multimedia role routing failed.",
    }
    assert "role 10 failed" not in response["error"]
    assert "role 20 failed" not in response["error"]


def test_main_reads_one_request_and_writes_one_json_response(helper, monkeypatch):
    module, _ = helper
    stdout = io.StringIO()
    monkeypatch.setattr(module.sys, "stdin", io.StringIO('{"command":"health"}\n'))
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 0
    assert json.loads(stdout.getvalue()) == {
        "ok": True,
        "value": {"version": "1.1.2"},
    }


def test_main_accepts_windows_utf8_bom_request(helper, monkeypatch):
    module, _ = helper
    stdout = io.StringIO()
    monkeypatch.setattr(module.sys, "stdin", io.StringIO('\ufeff{"command":"health"}\n'))
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 0
    assert json.loads(stdout.getvalue()) == {
        "ok": True,
        "value": {"version": "1.1.2"},
    }


def test_helper_process_reads_utf8_bom_when_windows_console_encoding_differs():
    environment = os.environ | {"PYTHONIOENCODING": "cp1252"}

    result = subprocess.run(
        [sys.executable, str(HELPER_DIRECTORY / "audio_share_router_helper.py")],
        input=b'\xef\xbb\xbf{"command":"health"}\n',
        capture_output=True,
        env=environment,
        check=False,
    )

    assert result.returncode == 0
    assert json.loads(result.stdout) == {"ok": True, "value": {"version": "1.1.2"}}


def test_main_reports_malformed_json_with_nonzero_exit(helper, monkeypatch):
    module, _ = helper
    stdout = io.StringIO()
    monkeypatch.setattr(module.sys, "stdin", io.StringIO("not json\n"))
    monkeypatch.setattr(module.sys, "stdout", stdout)

    assert module.main() == 1
    assert json.loads(stdout.getvalue()) == {"ok": False, "error": "Malformed JSON."}
