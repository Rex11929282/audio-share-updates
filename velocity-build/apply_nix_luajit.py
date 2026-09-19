from pathlib import Path
import shutil
import sys

root = Path(sys.argv[1]).resolve()
candidates = [
    root / "cs2" / "MCB-CS2",
    root / "cs2" / "velocity-cs2",
]
v = next((p for p in candidates if p.exists()), None)
if v is None:
    raise SystemExit("[nix-luajit] target project not found")

overlay = Path(__file__).resolve().parent / "overlay"
dst = v / "project" / "core" / "scripting"
dst.mkdir(parents=True, exist_ok=True)

for name in ("nix_runtime.hpp", "nix_runtime.cpp", "nix_luajit_manifest.hpp", "nix_luajit_api.hpp", "nix_value_bootstrap.hpp", "nix_entity_bridge.hpp", "nix_entity_bridge.cpp"):
    source = overlay / name
    if not source.exists():
        raise SystemExit(f"[nix-luajit] missing overlay/{name}")
    shutil.copy2(source, dst / name)

proj_candidates = [v / "MCB-CS2.vcxproj", v / "velocity-cs2.vcxproj"]
proj = next((p for p in proj_candidates if p.exists()), None)
if proj is None:
    raise SystemExit("[nix-luajit] vcxproj missing")

t = proj.read_text(encoding="utf-8")
compile_anchor = '    <ClCompile Include="project\\core\\scripting\\lua_manager.cpp" />'
if compile_anchor not in t:
    raise SystemExit("[nix-luajit] lua_manager.cpp project marker missing")
compile_entries = [
    '    <ClCompile Include="project\\core\\scripting\\nix_runtime.cpp" />',
    '    <ClCompile Include="project\\core\\scripting\\nix_entity_bridge.cpp" />',
]
for entry_line in compile_entries:
    if entry_line not in t:
        t = t.replace(compile_anchor, compile_anchor + '\n' + entry_line, 1)

header_anchor = '    <ClInclude Include="project\\core\\scripting\\scripting.hpp" />'
if header_anchor not in t:
    raise SystemExit("[nix-luajit] scripting.hpp project marker missing")
header_entries = [
    '    <ClInclude Include="project\\core\\scripting\\nix_runtime.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_luajit_manifest.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_luajit_api.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_value_bootstrap.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_entity_bridge.hpp" />',
]
for entry_line in header_entries:
    if entry_line not in t:
        t = t.replace(header_anchor, header_anchor + '\n' + entry_line, 1)
proj.write_text(t, encoding="utf-8")

entry = v / "project" / "entry.cpp"
t = entry.read_text(encoding="utf-8")
include_anchor = "#include <core/scripting/scripting.hpp>"
if "#include <core/scripting/nix_runtime.hpp>" not in t:
    if include_anchor not in t:
        raise SystemExit("[nix-luajit] entry scripting include marker missing")
    t = t.replace(
        include_anchor,
        include_anchor + '\n#include <core/scripting/nix_runtime.hpp>',
        1)

init_anchor = 'if ( !scripting::g_lua.initialize( module_handle ) ) diag::write( diag::level::warning, "Lua scripting initialization failed" );'
if "scripting::g_nix.initialize( module_handle )" not in t:
    if init_anchor not in t:
        raise SystemExit("[nix-luajit] entry init marker missing")
    t = t.replace(
        init_anchor,
        init_anchor +
        '\n\t\tif ( !scripting::g_nix.initialize( module_handle ) ) diag::write( diag::level::warning, "Nixware LuaJIT runtime unavailable; legacy Lua remains active" );',
        1)

shutdown_anchor = "scripting::g_lua.shutdown( );"
if "scripting::g_nix.shutdown( );" not in t:
    if shutdown_anchor not in t:
        raise SystemExit("[nix-luajit] entry shutdown marker missing")
    t = t.replace(
        shutdown_anchor,
        shutdown_anchor + '\n\t\tscripting::g_nix.shutdown( );',
        1)
entry.write_text(t, encoding="utf-8")

context = v / "project" / "core" / "rendering" / "impl" / "context.cpp"
t = context.read_text(encoding="utf-8")
if "#include <core/scripting/nix_runtime.hpp>" not in t:
    if "#include <core/scripting/scripting.hpp>" in t:
        t = t.replace(
            "#include <core/scripting/scripting.hpp>",
            "#include <core/scripting/scripting.hpp>\n#include <core/scripting/nix_runtime.hpp>",
            1)
    elif "#include <core/features/features.hpp>" in t:
        t = t.replace(
            "#include <core/features/features.hpp>",
            "#include <core/features/features.hpp>\n#include <core/scripting/nix_runtime.hpp>",
            1)
    else:
        raise SystemExit("[nix-luajit] context include marker missing")

frame_anchor = "scripting::g_lua.on_frame( );"
if "scripting::g_nix.on_frame( );" not in t:
    if frame_anchor not in t:
        raise SystemExit("[nix-luajit] context frame marker missing")
    t = t.replace(
        frame_anchor,
        frame_anchor + '\n\t\tscripting::g_nix.on_frame( );',
        1)
context.write_text(t, encoding="utf-8")

print(f"[nix-luajit] dual runtime integrated into {v}")
