import json
import ntpath
import sys
from contextlib import ExitStack


PROTECTED_PREFIXES = ("discord", "voicemod", "voicemeeter")
ROUTE_COMMANDS = {"get-route", "set-route", "clear-route", "restore-route"}
UNIX_EPOCH_UTC_TICKS = 621355968000000000
WINDOWS_EPOCH_UTC_TICKS = 504911232000000000
START_TIME_TOLERANCE_TICKS = 10000
router = None
policy = None
process_identity_lease_factory = None


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
        self._write_roles(process_id, lambda name: route[name])

    def set_route(self, process_id, device_id):
        self._write_roles(process_id, lambda _: device_id)

    def _write_roles(self, process_id, device_id_for_role):
        failures = []
        with self._com_initialized():
            with self._factory() as factory:
                for name, role in self._roles.items():
                    try:
                        factory.set_persisted_default_endpoint(
                            process_id=process_id,
                            flow=self._flow,
                            role=role,
                            packed_device_id=self._pack_device_id(
                                device_id_for_role(name)
                            ),
                        )
                    except Exception:
                        failures.append(
                            {
                                "consoleDeviceId": "Console",
                                "multimediaDeviceId": "Multimedia",
                            }[name]
                        )
        if failures:
            raise _RoleRoutingError(failures)


class _RoleRoutingError(RuntimeError):
    def __init__(self, roles):
        super().__init__(f"{' and '.join(roles)} role routing failed.")


def _policy():
    global policy
    if policy is None:
        policy = _PersistedRoutePolicy()
    return policy


class _WindowsProcessIdentityLease:
    _PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

    def __init__(self, process_id):
        import ctypes
        from ctypes import wintypes

        self._ctypes = ctypes
        self._wintypes = wintypes
        self._kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._kernel32.OpenProcess.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        self._kernel32.OpenProcess.restype = wintypes.HANDLE
        self._kernel32.QueryFullProcessImageNameW.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        self._kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
        self._kernel32.GetProcessTimes.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
        ]
        self._kernel32.GetProcessTimes.restype = wintypes.BOOL
        self._kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        self._kernel32.CloseHandle.restype = wintypes.BOOL
        self._handle = self._kernel32.OpenProcess(
            self._PROCESS_QUERY_LIMITED_INFORMATION,
            False,
            process_id,
        )
        if not self._handle:
            raise ctypes.WinError(ctypes.get_last_error())

    def __enter__(self):
        return self

    def __exit__(self, *_):
        if self._handle:
            self._kernel32.CloseHandle(self._handle)
            self._handle = None

    def name(self):
        size = self._wintypes.DWORD(32768)
        buffer = self._ctypes.create_unicode_buffer(size.value)
        if not self._kernel32.QueryFullProcessImageNameW(
            self._handle,
            0,
            buffer,
            self._ctypes.byref(size),
        ):
            raise self._ctypes.WinError(self._ctypes.get_last_error())
        return ntpath.basename(buffer.value)

    def create_time_utc_ticks(self):
        creation = self._wintypes.FILETIME()
        exit_time = self._wintypes.FILETIME()
        kernel = self._wintypes.FILETIME()
        user = self._wintypes.FILETIME()
        if not self._kernel32.GetProcessTimes(
            self._handle,
            self._ctypes.byref(creation),
            self._ctypes.byref(exit_time),
            self._ctypes.byref(kernel),
            self._ctypes.byref(user),
        ):
            raise self._ctypes.WinError(self._ctypes.get_last_error())
        file_time = (creation.dwHighDateTime << 32) | creation.dwLowDateTime
        return file_time + WINDOWS_EPOCH_UTC_TICKS


def _process_identity_lease(process_id):
    global process_identity_lease_factory
    if process_identity_lease_factory is None:
        process_identity_lease_factory = _WindowsProcessIdentityLease
    return process_identity_lease_factory(process_id)


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


def _normalized_process_name(process_name):
    basename = ntpath.basename(process_name)
    return ntpath.splitext(basename)[0].casefold()


def _process_identity(request):
    process_name = request.get("processName")
    process_start_utc_ticks = request.get("processStartUtcTicks")
    if (
        not isinstance(process_name, str)
        or not process_name.strip()
        or isinstance(process_start_utc_ticks, bool)
        or not isinstance(process_start_utc_ticks, int)
        or process_start_utc_ticks <= 0
    ):
        return None
    return process_name, process_start_utc_ticks


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

    expected_identity = _process_identity(request)
    if expected_identity is None:
        return None, {"ok": False, "error": "Missing process identity."}

    return (process_id, *expected_identity), None


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
        return {"ok": True, "value": {"version": "1.1.2"}}
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

    if command == "set-route":
        device_id = request.get("deviceId")
        if not isinstance(device_id, str) or not device_id.strip():
            return {"ok": False, "error": "Missing deviceId."}
    if command == "restore-route":
        route = _route_state(request)
        if route is None:
            return {"ok": False, "error": "Invalid route state."}

    try:
        route_session, error = _route_session(request)
    except Exception:
        return {"ok": False, "error": "Router unavailable."}
    if error is not None:
        return error
    process_id, expected_name, expected_start_utc_ticks = route_session

    identity_lease = ExitStack()
    try:
        process = identity_lease.enter_context(_process_identity_lease(process_id))
    except Exception:
        identity_lease.close()
        return {"ok": False, "error": "Process identity mismatch."}

    with identity_lease:
        try:
            actual_name = process.name()
            actual_start_utc_ticks = process.create_time_utc_ticks()
        except Exception:
            return {"ok": False, "error": "Process identity mismatch."}

        if (
            not isinstance(actual_name, str)
            or _normalized_process_name(actual_name)
            != _normalized_process_name(expected_name)
            or abs(actual_start_utc_ticks - expected_start_utc_ticks)
            > START_TIME_TOLERANCE_TICKS
        ):
            return {"ok": False, "error": "Process identity mismatch."}

        try:
            if command == "get-route":
                return {
                    "ok": True,
                    "value": _policy().get_route(process_id),
                }
            if command == "set-route":
                _policy().set_route(process_id, device_id)
            elif command == "restore-route":
                _policy().restore_route(process_id, route)
            else:
                _policy().set_route(process_id, None)
        except _RoleRoutingError as exception:
            return {"ok": False, "error": str(exception)}
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
