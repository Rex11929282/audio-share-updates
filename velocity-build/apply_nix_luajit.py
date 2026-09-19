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

for name in ("nix_runtime.hpp", "nix_runtime.cpp", "nix_luajit_manifest.hpp", "nix_luajit_api.hpp", "nix_value_bootstrap.hpp", "nix_entity_bridge.hpp", "nix_entity_bridge.cpp", "nix_cvar_bridge.hpp", "nix_cvar_bridge.cpp", "nix_engine_bridge.hpp", "nix_engine_bridge.cpp", "nix_event_bridge.hpp", "nix_event_bridge.cpp"):
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
    '    <ClCompile Include="project\\core\\scripting\\nix_cvar_bridge.cpp" />',
    '    <ClCompile Include="project\\core\\scripting\\nix_engine_bridge.cpp" />',
    '    <ClCompile Include="project\\core\\scripting\\nix_event_bridge.cpp" />',
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
    '    <ClInclude Include="project\\core\\scripting\\nix_cvar_bridge.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_engine_bridge.hpp" />',
    '    <ClInclude Include="project\\core\\scripting\\nix_event_bridge.hpp" />',
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


cheat = v / "project" / "core" / "hooks" / "impl" / "cheat.cpp"
t = cheat.read_text(encoding="utf-8")
cheat_include_anchor = "#include <core/rendering/rendering.hpp>"
if "#include <core/scripting/nix_runtime.hpp>" not in t:
    if cheat_include_anchor not in t:
        raise SystemExit("[nix-luajit] cheat include marker missing")
    t = t.replace(
        cheat_include_anchor,
        cheat_include_anchor + '\n#include <core/scripting/nix_runtime.hpp>',
        1)

override_anchor = "\t\tfeatures::combat::g_misc.duckpeek( ).on_override_view( view_setup );"
if "scripting::g_nix.on_override_view( view_setup );" not in t:
    if override_anchor not in t:
        raise SystemExit("[nix-luajit] override_view hook marker missing")
    t = t.replace(
        override_anchor,
        override_anchor + '\n\t\tscripting::g_nix.on_override_view( view_setup );',
        1)
cheat.write_text(t, encoding="utf-8")


menu_exact = v / "project" / "core" / "rendering" / "impl" / "menu" / "menu.exact.cpp"
t = menu_exact.read_text(encoding="utf-8")
menu_include_anchor = "#include <pch/pch.hpp>"
if "#include <core/scripting/nix_runtime.hpp>" not in t:
    if menu_include_anchor not in t:
        raise SystemExit("[nix-luajit] menu exact include marker missing")
    t = t.replace(
        menu_include_anchor,
        menu_include_anchor + '\n#include <core/scripting/nix_runtime.hpp>',
        1)
menu_exact.write_text(t, encoding="utf-8")

views = v / "project" / "core" / "rendering" / "ui_views_v62.inl"
t = views.read_text(encoding="utf-8")
scripts_anchor = '''    if(!xui::begin_child("##v62_scripts",w,h,true))return;
    xui::text("Scripts - local draft editor",tokens::col_text);
    xui::text("EXECUTION DISABLED",xdraw::color{255,174,95,255});'''
scripts_replacement = '''    if(!xui::begin_child("##v62_scripts",w,h,true))return;
    xui::text("Nixware LuaJIT Runtime",tokens::col_text);
    xui::text(
        scripting::g_nix.ready()
            ? "Exact LuaJIT runtime ready"
            : "LuaJIT runtime unavailable",
        scripting::g_nix.ready()
            ? tokens::col_accent
            : xdraw::color{255,126,112,255});
    if(xui::button("Import / Approve Nixware Lua",210,28)){
        if(scripting::g_nix.import_script_dialog(nullptr))
            notify("Nixware Lua approved; it will load on the next runtime scan");
    }
    xui::layout::same_line();
    if(xui::button("Reload Nixware",116,28)){
        scripting::g_nix.reload_all();
        notify("Approved Nixware scripts reloaded");
    }
    const auto nix_status=scripting::g_nix.script_statuses();
    if(nix_status.empty()){
        xui::text("No approved Nixware script is loaded",tokens::col_text_dim);
    } else {
        const auto shown=std::min<std::size_t>(nix_status.size(),6);
        for(std::size_t i=0;i<shown;++i){
            const auto& item=nix_status[i];
            std::string line=(item.loaded&&!item.suspended?"[OK] ":"[STOP] ")+item.name;
            if(!item.last_error.empty())line+=" - "+item.last_error;
            xui::text(xui::truncate(line,std::max(160.0f,w-28.0f)),
                       item.loaded&&!item.suspended?tokens::col_text:tokens::col_text_dim);
        }
        if(nix_status.size()>shown)
            xui::text("... "+std::to_string(nix_status.size()-shown)+" more",tokens::col_text_dim);
    }
    xui::layout::separator();
    xui::text("Scripts - local draft editor",tokens::col_text);
    xui::text("Draft editor does not execute; Nixware imports use the separate LuaJIT runtime.",tokens::col_text_dim);'''
if scripts_anchor not in t:
    raise SystemExit("[nix-luajit] v6.2 script view marker missing")
t = t.replace(scripts_anchor, scripts_replacement, 1)
views.write_text(t, encoding="utf-8")


events_cpp = v / "project" / "core" / "systems" / "impl" / "events.cpp"
t = events_cpp.read_text(encoding="utf-8")
events_include_anchor = "#include <core/features/features.hpp>"
if "#include <core/scripting/nix_runtime.hpp>" not in t:
    if events_include_anchor not in t:
        raise SystemExit("[nix-luajit] events include marker missing")
    t = t.replace(
        events_include_anchor,
        events_include_anchor + '\n#include <core/scripting/nix_runtime.hpp>',
        1)

event_replacements = {
    '[ ]( void* event ) { features::misc::g_impacts.on_bullet_impact( reinterpret_cast< std::uintptr_t >( event ) ); }':
        '[ ]( void* event ) { scripting::g_nix.on_game_event( "bullet_impact", reinterpret_cast< std::uintptr_t >( event ) ); features::misc::g_impacts.on_bullet_impact( reinterpret_cast< std::uintptr_t >( event ) ); }',
    '[ ]( void* event ) { features::misc::g_impacts.on_player_hurt( reinterpret_cast< std::uintptr_t >( event ) ); }':
        '[ ]( void* event ) { scripting::g_nix.on_game_event( "player_hurt", reinterpret_cast< std::uintptr_t >( event ) ); features::misc::g_impacts.on_player_hurt( reinterpret_cast< std::uintptr_t >( event ) ); }',
    '[ ]( void* event ) { features::misc::g_other.on_round_start( ); }':
        '[ ]( void* event ) { scripting::g_nix.on_game_event( "round_start", reinterpret_cast< std::uintptr_t >( event ) ); features::misc::g_other.on_round_start( ); }',
    '[ ]( void* event ) { features::misc::g_other.on_player_death( reinterpret_cast< std::uintptr_t >( event ) ); }':
        '[ ]( void* event ) { scripting::g_nix.on_game_event( "player_death", reinterpret_cast< std::uintptr_t >( event ) ); features::misc::g_other.on_player_death( reinterpret_cast< std::uintptr_t >( event ) ); }',
}
for old, new in event_replacements.items():
    if new not in t:
        if old not in t:
            raise SystemExit("[nix-luajit] Source2 event listener marker missing: " + old[:60])
        t = t.replace(old, new, 1)
events_cpp.write_text(t, encoding="utf-8")
