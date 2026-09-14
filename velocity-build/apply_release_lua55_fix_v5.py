from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
lua = root / "cs2" / "MCB-CS2" / "project" / "core" / "scripting" / "lua_manager.cpp"
t = lua.read_text(encoding="utf-8")
old = "value.state = lua_newstate( limited_lua_allocator, &value.memory );"
new = "value.state = lua_newstate( limited_lua_allocator, &value.memory, luaL_makeseed( nullptr ) );"
if old not in t:
    raise SystemExit("[release-lua55-fix-v5] lua_newstate marker not found")
t = t.replace(old, new, 1)
lua.write_text(t, encoding="utf-8")
print("[release-lua55-fix-v5] Lua 5.5 hash seed argument applied")
