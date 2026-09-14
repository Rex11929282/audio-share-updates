from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
entry = root / "cs2" / "MCB-CS2" / "project" / "entry.cpp"
t = entry.read_text(encoding="utf-8")
needle = "\telse if ( reason == DLL_PROCESS_DETACH )\n"
if "DLL_THREAD_ATTACH || reason == DLL_THREAD_DETACH" not in t:
    if needle not in t:
        raise SystemExit("[release-thread-crt-v5] detach marker not found")
    replacement = '''\telse if ( reason == DLL_THREAD_ATTACH || reason == DLL_THREAD_DETACH )
\t{
\t\t// Custom /MT entry point must forward thread notifications to the CRT.
\t\t_CRT_INIT( module_handle, reason, reserved );
\t}
\telse if ( reason == DLL_PROCESS_DETACH )
'''
    t = t.replace(needle, replacement, 1)
entry.write_text(t, encoding="utf-8")
print("[release-thread-crt-v5] CRT thread notifications forwarded")
