import json
import ntpath
import sys


PROTECTED_PREFIXES = ("discord", "voicemod", "voicemeeter")
ROUTE_COMMANDS = {"get-route", "set-route", "clear-route"}
router = None


def _router():
    global router
    if router is None:
        import winappaudiorouter

        router = winappaudiorouter
    return router


def _field(value, name, fallback=None):
    if isinstance(value, dict):
        return value.get(name, fallback)
    return getattr(value, name, fallback)


def _active_session(process_id):
    for session in _router().list_app_sessions():
        session_id = _field(session, "process_id", _field(session, "pid"))
        if session_id == process_id:
            return session
    return None


def _process_id(request):
    process_id = request.get("processId")
    if isinstance(process_id, bool) or not isinstance(process_id, int) or process_id <= 0:
        return None
    return process_id


def _route_session(request):
    process_id = _process_id(request)
    if process_id is None:
        return None, {"ok": False, "error": "Missing processId."}

    session = _active_session(process_id)
    if session is None:
        return None, {"ok": False, "error": "Active output session not found."}

    process_name = _field(session, "process_name", "")
    if not isinstance(process_name, str) or not process_name.strip():
        return None, {"ok": False, "error": "Active output session not found."}
    if ntpath.basename(process_name).casefold().startswith(PROTECTED_PREFIXES):
        return None, {"ok": False, "error": "Protected process cannot be routed."}
    return process_id, None


def _device_value(device):
    return {
        "id": str(_field(device, "id", "")),
        "name": str(_field(device, "name", "")),
        "is_default": bool(_field(device, "is_default", False)),
    }


def handle(request):
    if not isinstance(request, dict):
        return {"ok": False, "error": "Malformed request."}

    command = request.get("command")
    if command == "health":
        return {"ok": True, "value": {"version": "1.1.1"}}
    if command == "list-devices":
        try:
            return {
                "ok": True,
                "value": [_device_value(device) for device in _router().list_output_devices()],
            }
        except Exception:
            return {"ok": False, "error": "Router unavailable."}
    if command not in ROUTE_COMMANDS:
        return {"ok": False, "error": "Unknown command."}

    try:
        if command == "set-route":
            device_id = request.get("deviceId")
            if not isinstance(device_id, str) or not device_id.strip():
                return {"ok": False, "error": "Missing deviceId."}
        process_id, error = _route_session(request)
        if error is not None:
            return error
        if command == "get-route":
            return {
                "ok": True,
                "value": _router().get_app_output_device(process_id=process_id).get(process_id),
            }
        if command == "set-route":
            _router().set_app_output_device(process_id=process_id, device=device_id)
        else:
            _router().clear_app_output_device(process_id=process_id)
    except Exception:
        return {"ok": False, "error": "Routing request failed."}

    return {"ok": True, "value": None}


def main():
    line = sys.stdin.readline()
    try:
        response = handle(json.loads(line))
    except json.JSONDecodeError:
        response = {"ok": False, "error": "Malformed JSON."}
        status = 1
    else:
        status = 0

    if not response["ok"]:
        print(response["error"], file=sys.stderr)
    sys.stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
    return status


if __name__ == "__main__":
    raise SystemExit(main())
