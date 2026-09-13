from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "velocity-cs2"


def die(msg: str):
    raise SystemExit("[perf-v3] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        die(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


# 1) Run Lua callbacks inside the active xdraw frame so HUD drawing is real.
context = v / "project/core/rendering/impl/context.cpp"
t = context.read_text(encoding="utf-8")
t = t.replace(
    '\t\tfeatures::misc::g_dlight.on_present( );\n\t\tscripting::g_lua.on_frame( );',
    '\t\tfeatures::misc::g_dlight.on_present( );',
    1)
old = '''\t\t\tif ( this->m_ui_assets_ready )
\t\t\t{
\t\t\t\tg_widgets.draw( );
\t\t\t}'''
new = '''\t\t\tif ( this->m_ui_assets_ready )
\t\t\t{
\t\t\t\t// Lua HUD callbacks must run between xdraw::begin_frame/end_frame.
\t\t\t\tscripting::g_lua.on_frame( );
\t\t\t\tg_widgets.draw( );
\t\t\t}'''
if 'Lua HUD callbacks must run between' not in t:
    t = replace_once(t, old, new, "Lua render-frame placement")
context.write_text(t, encoding="utf-8")


# 2) XUI interaction path: drag only from the top strip and skip the expensive
# full-window blur while dragging/resizing. This prevents button clicks from
# accidentally becoming window drags and removes the main resize hitch.
xui = v / "project/external/xdraw/xui/xui.cpp"
t = xui.read_text(encoding="utf-8")
t = t.replace('constexpr auto grip_size{ 16.0f };', 'constexpr auto grip_size{ 24.0f };', 1)
old = '''\t\tconst auto grip_hovered = resizable && allow_window_drag && input.in_rect( grip );
\t\tconst auto window_hovered = allow_window_drag && input.in_rect( abs );'''
new = '''\t\tconst auto grip_hovered = resizable && allow_window_drag && input.in_rect( grip );
\t\tconst auto window_hovered = allow_window_drag && input.in_rect( abs );
\t\t// Only the top strip starts a window drag. Previously every button click
\t\t// could arm active_window before the child widget processed the click.
\t\tconst auto drag_zone = rect{ abs.x, abs.y, abs.w, std::min( 52.0f, abs.h ) };
\t\tconst auto drag_hovered = allow_window_drag && input.in_rect( drag_zone );'''
if 'const auto drag_zone = rect{' not in t:
    t = replace_once(t, old, new, "drag zone")
t = t.replace(
    'else if ( window_hovered && !grip_hovered && input.mouse_clicked&& c.active_window == null_id',
    'else if ( drag_hovered && !grip_hovered && input.mouse_clicked&& c.active_window == null_id',
    1)
old_blur = '''\t\tif ( reveal_clamped < 1.0f )
\t\t{
\t\t\tdl.rect_filled_blurred( draw_x, draw_y, draw_w, draw_h, xdraw::corner_radius{ r }, xdraw::color{ 255, 255, 255, static_cast< std::uint8_t >( 210.0f * reveal_clamped ) } );
\t\t}
\t\telse
\t\t{
\t\t\tdl.rect_filled_blurred( draw_x, draw_y, draw_w, draw_h, xdraw::corner_radius{ r } );
\t\t}'''
new_blur = '''\t\tconst auto moving_or_resizing = c.active_window == id || c.active_resize == id;
\t\tif ( !moving_or_resizing )
\t\t{
\t\t\tif ( reveal_clamped < 1.0f )
\t\t\t{
\t\t\t\tdl.rect_filled_blurred( draw_x, draw_y, draw_w, draw_h, xdraw::corner_radius{ r }, xdraw::color{ 255, 255, 255, static_cast< std::uint8_t >( 210.0f * reveal_clamped ) } );
\t\t\t}
\t\t\telse
\t\t\t{
\t\t\t\tdl.rect_filled_blurred( draw_x, draw_y, draw_w, draw_h, xdraw::corner_radius{ r } );
\t\t\t}
\t\t}'''
if 'moving_or_resizing' not in t:
    t = replace_once(t, old_blur, new_blur, "interaction blur bypass")
xui.write_text(t, encoding="utf-8")


# 3) Defer Traditional-Chinese atlas prewarm out of font initialization. It is
# performed once from the menu render path instead of delaying module startup.
fonts = v / "project/core/rendering/impl/fonts.cpp"
t = fonts.read_text(encoding="utf-8")
prewarm = '''
\t\t// Prewarm Traditional-Chinese glyphs once so switching to a page does not
\t\t// cause a one-frame atlas rebuild/flicker.
\t\tconst auto zh_prewarm = localization::prewarm_chars( );
\t\t( void )xdraw::measure_text( zh_prewarm );
'''
if prewarm in t:
    t = t.replace(prewarm, '\n', 1)
fonts.write_text(t, encoding="utf-8")

hpp = v / "project/core/rendering/rendering.hpp"
t = hpp.read_text(encoding="utf-8")
if 'm_cjk_prewarm_done' not in t:
    anchor = '        int m_layout_viewport_h{};\n'
    t = replace_once(t, anchor, anchor + '        bool m_cjk_prewarm_done{};\n', 'CJK prewarm state')
hpp.write_text(t, encoding="utf-8")

menu = v / "project/core/rendering/impl/menu/menu.core.cpp"
t = menu.read_text(encoding="utf-8")
if '#include <core/localization/zh_tw.hpp>' not in t:
    t = t.replace('#include <core/settings.hpp>\n', '#include <core/settings.hpp>\n#include <core/localization/zh_tw.hpp>\n', 1)
anchor = '''\t\tif ( !g_context.ui_assets_ready( ) )
\t\t{
\t\t\tthis->draw_intro( );
\t\t\treturn;
\t\t}

\t\tif ( this->draw_intro( ) )'''
replacement = '''\t\tif ( !g_context.ui_assets_ready( ) )
\t\t{
\t\t\tthis->draw_intro( );
\t\t\treturn;
\t\t}

\t\t// Defer CJK glyph warm-up until the rendering path is alive. This keeps
\t\t// DLL initialization lighter while still preventing first-page flicker.
\t\tif ( !this->m_cjk_prewarm_done )
\t\t{
\t\t\t( void )xdraw::measure_text( localization::prewarm_chars( ) );
\t\t\tthis->m_cjk_prewarm_done = true;
\t\t}

\t\tif ( this->draw_intro( ) )'''
if 'Defer CJK glyph warm-up until the rendering path is alive' not in t:
    t = replace_once(t, anchor, replacement, "deferred CJK prewarm")
menu.write_text(t, encoding="utf-8")


# 4) Lua tab: show every loaded/suspended/failed script so active Lua is visible.
config = v / "project/core/rendering/impl/menu/menu.config.cpp"
t = config.read_text(encoding="utf-8")
anchor = '''\t\t\t\tconst auto folder_text = std::string( "腳本資料夾：" ) + scripting::g_lua.script_directory( ).string( );
\t\t\t\txui::text( folder_text, tokens::col_text_dim );

\t\t\t\tconst auto [ avail_w, avail_h ] = xui::layout::avail( );'''
insert = '''\t\t\t\tconst auto folder_text = std::string( "腳本資料夾：" ) + scripting::g_lua.script_directory( ).string( );
\t\t\t\txui::text( folder_text, tokens::col_text_dim );

\t\t\t\txui::layout::separator( );
\t\t\t\txui::text( "已開啟的 Lua", tokens::col_text );
\t\t\t\tconst auto lua_statuses = scripting::g_lua.script_statuses( );
\t\t\t\tif ( lua_statuses.empty( ) )
\t\t\t\t{
\t\t\t\t\txui::text( "目前沒有偵測到 Lua 腳本", tokens::col_text_dim );
\t\t\t\t}
\t\t\t\telse
\t\t\t\t{
\t\t\t\t\tfor ( const auto& status : lua_statuses )
\t\t\t\t\t{
\t\t\t\t\t\tconst auto suffix = status.suspended ? " — 已暫停" : status.loaded ? " — 運行中" : " — 載入失敗";
\t\t\t\t\t\tconst auto line = status.name + suffix;
\t\t\t\t\t\tconst auto col = status.suspended || !status.loaded
\t\t\t\t\t\t\t? xdraw::color{ 255, 145, 145, 235 }
\t\t\t\t\t\t\t: tokens::col_accent;
\t\t\t\t\t\txui::text( line, col );
\t\t\t\t\t\tif ( !status.last_error.empty( ) )
\t\t\t\t\t\t{
\t\t\t\t\t\t\tconst auto error_line = std::string( "錯誤：" ) + status.last_error;
\t\t\t\t\t\t\txui::text( error_line, xdraw::color{ 255, 145, 145, 210 } );
\t\t\t\t\t\t}
\t\t\t\t\t}
\t\t\t\t}

\t\t\t\tconst auto [ avail_w, avail_h ] = xui::layout::avail( );'''
if '已開啟的 Lua' not in t:
    t = replace_once(t, anchor, insert, "Lua status visualization")
config.write_text(t, encoding="utf-8")


# 5) Translation fallback should not spend allocations on dynamic game strings.
loc = v / "project/core/localization/zh_tw.hpp"
t = loc.read_text(encoding="utf-8")
anchor = '''    [[nodiscard]] inline std::string_view generic_tr( std::string_view s ) noexcept
    {
        bool has_ascii_alpha{};'''
replacement = '''    [[nodiscard]] inline std::string_view generic_tr( std::string_view s ) noexcept
    {
        // Generic fallback is for short, lower-case UI labels only. Dynamic
        // player/model/weapon text bypasses this path to avoid per-frame work.
        if ( s.empty( ) || s.size( ) > 80 ) return s;
        for ( const unsigned char c : s )
        {
            if ( c >= 0x80 || ( c >= 'A' && c <= 'Z' ) || ( c >= '0' && c <= '9' ) )
                return s;
        }

        bool has_ascii_alpha{};'''
if 'player/model/weapon text bypasses this path' not in t:
    t = replace_once(t, anchor, replacement, "translation fast path")
loc.write_text(t, encoding="utf-8")

print('[perf-v3] Lua HUD frame placement + interaction/performance/stability patches applied')
