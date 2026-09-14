from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
hpp = root / "cs2" / "MCB-CS2" / "project" / "core" / "features" / "changer" / "changer.hpp"
t = hpp.read_text(encoding="utf-8")

if "#include <utilities/threadpool/threadpool.hpp>" not in t:
    t = t.replace("#include <filesystem>\n", "#include <filesystem>\n#include <utilities/threadpool/threadpool.hpp>\n", 1)

if "\t\tvoid shutdown( );" not in t.split("\tclass agents", 1)[0]:
    marker = "\t\tvoid flush_skin_images( );\n"
    if marker not in t:
        raise SystemExit("[release-jobs-fix-v5] flush_skin_images marker missing")
    t = t.replace(marker, marker + "\t\tvoid shutdown( );\n", 1)

if "m_image_jobs" not in t:
    marker = "\t\tstd::mutex m_image_mutex{};\n"
    if marker not in t:
        raise SystemExit("[release-jobs-fix-v5] image mutex marker missing")
    t = t.replace(marker, marker +
        "\t\tstd::mutex m_job_mutex{};\n"
        "\t\tstd::vector<threadpool::job> m_image_jobs{};\n"
        "\t\tstd::atomic_bool m_shutting_down{};\n", 1)

hpp.write_text(t, encoding="utf-8")

# Lua 5.5 added a third lua_newstate parameter: a string-hash seed. Match
# luaL_newstate's documented behavior while retaining our custom allocator.
lua = root / "cs2" / "MCB-CS2" / "project" / "core" / "scripting" / "lua_manager.cpp"
lua_text = lua.read_text(encoding="utf-8")
old = "value.state = lua_newstate( limited_lua_allocator, &value.memory );"
new = "value.state = lua_newstate( limited_lua_allocator, &value.memory, luaL_makeseed( nullptr ) );"
if old in lua_text:
    lua_text = lua_text.replace(old, new, 1)
elif new not in lua_text:
    raise SystemExit("[release-jobs-fix-v5] Lua 5.5 lua_newstate marker missing")
lua.write_text(lua_text, encoding="utf-8")

print("[release-jobs-fix-v5] tracked skin jobs + Lua 5.5 state seed applied")
