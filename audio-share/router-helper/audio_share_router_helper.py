import json
import ntpath
import sys


PROTECTED_PREFIXES = ("discord", "voicemod", "voicemeeter")
ROUTE_COMMANDS = {"get-route", "set-route", "clear-route", "restore-route"}
router = None
policy = None


def _router():
    global router
    if router is None:
        import winappaudiorouter

        router = winappaudiorouter
    return router


class _PersistedRoutePolicy:
    def __init__(self):
        from pycaw.constants import EDataFlow, ERole
        from winappaudiorouter.com import com_initialized
        from winappaudiorouter.device_ids import pack_render_device_id, unpack_device_id
        from winappaudiorouter.policy_config import PolicyConfigFactory

        self._com_initialized = com_initialized
        self._factory = PolicyConfigFactory
        self._pack_device_id = pack_render_device_id
        self._unpack_device_id = unpack_device_id
        self._flow = EDataFlow.eRender.value
        self._roles = {
            "consoleDeviceId": ERole.eConsole.value,
            "multimediaDeviceId": ERole.eMultimedia.value,
        }

    def get_route(self, process_id):
        route = {}
        with self._com_initialized():
            with self._factory() as factory:
                for name, role in self._roles.items():
                    packed_device_id = factory.get_persisted_default_endpoint(
                        process_id=process_id,
                        flow=self._flow,
                        role=role,
                    )
                    route[name] = self._unpack_device_id(packed_device_id)
        return route

    def restore_route(self, process_id, route):
        with self._com_initialized():
            with self._factory() as factory:
                for name, role in self._roles.items():
                    factory.set_persisted_default_endpoint(
                        process_id=process_id,
                        flow=self._flow,
                        role=role,
                        packed_device_id=self._pack_device_id(route[name]),
                    )


def _policy():
    global policy
    if policy is None:
        policy = _PersistedRoutePolicy()
    return policy


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


def _route_state(request):
    route = {}
    for name in ("consoleDeviceId", "multimediaDeviceId"):
        if name not in request:
            return None
        value = request[name]
        if value is not None and (
            not isinstance(value, str) or not value.strip()
        ):
            return None
        route[name] = value
    return route


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
        if command == "restore-route":
            route = _route_state(request)
            if route is None:
                return {"ok": False, "error": "Invalid route state."}
        process_id, error = _route_session(request)
        if error is not None:
            return error
        if command == "get-route":
            return {
                "ok": True,
                "value": _policy().get_route(process_id),
            }
        if command == "set-route":
            _router().set_app_output_device(process_id=process_id, device=device_id)
        elif command == "restore-route":
            _policy().restore_route(process_id, route)
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
