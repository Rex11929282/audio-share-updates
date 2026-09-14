from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "MCB-CS2"
menu = v / "project/core/rendering/impl/menu/menu.core.cpp"
hpp = v / "project/core/rendering/rendering.hpp"


def fail(msg: str):
    raise SystemExit("[exact-runtime-v5] " + msg)

# Extra state needed to reproduce the uploaded shell behavior.
t = hpp.read_text(encoding="utf-8")
if "m_exact_last_compact" not in t:
    marker = "        bool m_sidebar_compact{};\n"
    if marker not in t:
        fail("compact state marker missing")
    t = t.replace(marker, marker + "        bool m_exact_last_compact{};\n", 1)
hpp.write_text(t, encoding="utf-8")

t = menu.read_text(encoding="utf-8")

# apply_exact_panel_v5 intentionally injects the exact topbar before the legacy
# implementation. Strip the legacy tail now, leaving only the exact function.
marker = "// MCB exact uploaded topbar."
if marker not in t:
    fail("exact topbar marker missing")
marker_pos = t.index(marker)
ret_pos = t.find("\n\t\treturn;\n\t}\n", marker_pos)
if ret_pos < 0:
    fail("exact topbar return marker missing")
exact_end = ret_pos + len("\n\t\treturn;\n\t}\n")
namespace_end = t.find("\n} // namespace rendering", exact_end)
if namespace_end < 0:
    fail("menu namespace end missing")
legacy_tail = t[exact_end:namespace_end]
if "menu_topbar_search_anim" in legacy_tail or "subtab_count" in legacy_tail:
    t = t[:exact_end] + t[namespace_end:]

# Replace the old 7-tab shell completely. No old card/subtab/search layer is
# rendered underneath the uploaded shell anymore.
pattern = r"\tvoid menu::draw\( \)\n\t\{.*?\n\t\}\n\n\tvoid menu::shutdown"
replacement = r'''\tvoid menu::draw( )
\t{
\t\tif ( this->m_last_open != this->m_open )
\t\t{
\t\t\tif ( this->m_open )
\t\t\t{
\t\t\t\tthis->m_saved_relative_mouse = memory::read<std::uint8_t>( addresses::globals::input_system + 84 );
\t\t\t\tmemory::call_vfunc<void>( addresses::globals::input_system, 76, false );
\t\t\t\tthis->apply_saved_cursor( );
\t\t\t}
\t\t\telse
\t\t\t{
\t\t\t\tPOINT pt{};
\t\t\t\tif ( GetCursorPos( &pt ) )
\t\t\t\t{
\t\t\t\t\tthis->m_saved_cursor_x = pt.x;
\t\t\t\t\tthis->m_saved_cursor_y = pt.y;
\t\t\t\t\tthis->m_has_saved_cursor = true;
\t\t\t\t}
\t\t\t\tmemory::call_vfunc<void>( addresses::globals::input_system, 76, this->m_saved_relative_mouse != 0 );
\t\t\t}
\t\t\tthis->m_last_open = this->m_open;
\t\t}

\t\tif ( !g_context.ui_assets_ready( ) )
\t\t{
\t\t\tthis->draw_intro( );
\t\t\treturn;
\t\t}

\t\tif ( !this->m_cjk_prewarm_done )
\t\t{
\t\t\t( void )xdraw::measure_text( localization::prewarm_chars );
\t\t\tthis->m_cjk_prewarm_done = true;
\t\t}

\t\tif ( this->draw_intro( ) ) return;

\t\txui::begin( );
\t\tthis->sync_theme_style( );
\t\tauto& input = xui::ctx( ).input;

\t\t// Match the uploaded UI-only keyboard navigation.
\t\tconst auto typing = xui::ctx( ).active_text_input != xui::null_id ||
\t\t\txui::ctx( ).active_slider_edit != xui::null_id ||
\t\t\txui::ctx( ).active_keybind != xui::null_id;
\t\tfor ( const auto vk : input.key_presses( ) )
\t\t{
\t\t\tif ( input.ctrl_held( ) && vk == 'S' )
\t\t\t{
\t\t\t\tthis->save_exact_panel_state( );
\t\t\t\tcontinue;
\t\t\t}
\t\t\tif ( input.ctrl_held( ) && vk == 'B' )
\t\t\t{
\t\t\t\tthis->m_sidebar_compact = !this->m_sidebar_compact;
\t\t\t\tcontinue;
\t\t\t}
\t\t\tif ( !typing && vk == VK_OEM_2 )
\t\t\t{
\t\t\t\tthis->m_search_open = true;
\t\t\t\tcontinue;
\t\t\t}
\t\t\tif ( vk == VK_ESCAPE )
\t\t\t{
\t\t\t\txui::overlays::close_all( );
\t\t\t\tthis->m_search_open = false;
\t\t\t\tthis->m_search_query.clear( );
\t\t\t}
\t\t}

\t\tconst auto dt = xdraw::delta_time( );
\t\tconst auto anim_speed = this->m_open ? 14.0f : 16.0f;
\t\tconst auto anim_target = this->m_open ? 1.0f : 0.0f;
\t\tthis->m_open_anim += ( anim_target - this->m_open_anim ) * std::min( anim_speed * dt, 1.0f );
\t\tif ( this->m_open_anim < 0.01f && !this->m_open )
\t\t{
\t\t\txui::end( );
\t\t\treturn;
\t\t}

\t\t// HTML shell is 846x682. Compact mode removes 145 px from the sidebar
\t\t// while preserving the 643 px workspace, exactly like the source page.
\t\tif ( this->m_sidebar_compact != this->m_exact_last_compact )
\t\t{
\t\t\tconst auto delta_w = this->m_sidebar_compact ? -145.0f : 145.0f;
\t\t\tthis->m_w = std::max( this->m_sidebar_compact ? 701.0f : 846.0f, this->m_w + delta_w );
\t\t\tthis->m_exact_last_compact = this->m_sidebar_compact;
\t\t}

\t\tconst auto [ viewport_w_i, viewport_h_i ] = xdraw::viewport_size( );
\t\tif ( viewport_w_i > 0 && viewport_h_i > 0 )
\t\t{
\t\t\tconst auto viewport_w = static_cast<float>( viewport_w_i );
\t\t\tconst auto viewport_h = static_cast<float>( viewport_h_i );
\t\t\tconstexpr float safe_margin{ 12.0f };
\t\t\tconst auto available_w = std::max( 1.0f, viewport_w - safe_margin * 2.0f );
\t\t\tconst auto available_h = std::max( 1.0f, viewport_h - safe_margin * 2.0f );
\t\t\tconst auto preferred_w = this->m_sidebar_compact ? 701.0f : 846.0f;
\t\t\tconstexpr float preferred_h{ 682.0f };
\t\t\tif ( !this->m_user_layout_initialized )
\t\t\t{
\t\t\t\tthis->m_w = std::min( preferred_w, available_w );
\t\t\t\tthis->m_h = std::min( preferred_h, available_h );
\t\t\t\tthis->m_x = std::floor( ( viewport_w - this->m_w ) * 0.5f );
\t\t\t\tthis->m_y = std::floor( ( viewport_h - this->m_h ) * 0.5f );
\t\t\t\tthis->m_user_layout_initialized = true;
\t\t\t}
\t\t\telse
\t\t\t{
\t\t\t\tthis->m_w = std::clamp( this->m_w, std::min( 560.0f, available_w ), available_w );
\t\t\t\tthis->m_h = std::clamp( this->m_h, std::min( 460.0f, available_h ), available_h );
\t\t\t\tthis->m_x = std::clamp( this->m_x, 0.0f, std::max( 0.0f, viewport_w - this->m_w ) );
\t\t\t\tthis->m_y = std::clamp( this->m_y, 0.0f, std::max( 0.0f, viewport_h - this->m_h ) );
\t\t\t}
\t\t\tthis->m_layout_viewport_w = viewport_w_i;
\t\t\tthis->m_layout_viewport_h = viewport_h_i;
\t\t}

\t\tconst auto menu_reveal = xui::ease::out_cubic( this->m_open_anim );
\t\tif ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, true, 560.0f, 460.0f, menu_reveal ) )
\t\t{
\t\t\txui::end( );
\t\t\treturn;
\t\t}

\t\t// Exact shell: one 203/58px sidebar, one 66px topbar, one workspace.
\t\tthis->draw_side_bar( this->m_h );
\t\tthis->draw_top_bar( this->m_w - tokens::sidebar_w );
\t\tthis->m_body_x = this->m_x + tokens::sidebar_w + 12.0f;
\t\tthis->m_body_y = this->m_y + 77.0f;
\t\tthis->m_body_w = std::max( 100.0f, this->m_w - tokens::sidebar_w - 24.0f );
\t\tthis->m_body_h = std::max( 100.0f, this->m_h - 91.0f );
\t\tthis->draw_exact_page( );

\t\t// 17x17 source resize grip visual. XUI owns the actual drag interaction.
\t\tauto& dl = xui::draw::current( );
\t\tconst auto gx = this->m_x + this->m_w - 17.0f;
\t\tconst auto gy = this->m_y + this->m_h - 17.0f;
\t\tdl.line( gx + 8.0f, gy + 14.0f, gx + 14.0f, gy + 8.0f, xdraw::color{ 80, 122, 148, 180 }, 1.0f );
\t\tdl.line( gx + 11.0f, gy + 14.0f, gx + 14.0f, gy + 11.0f, xdraw::color{ 80, 122, 148, 180 }, 1.0f );

\t\tif ( this->m_dim_interface )
\t\t{
\t\t\tdl.rect_filled( this->m_x, this->m_y, this->m_w, this->m_h, xdraw::color{ 1, 4, 7, 36 }, xdraw::corner_radius{ 8.0f } );
\t\t}

\t\txui::end_window( );
\t\txui::end( );
\t}

\tvoid menu::shutdown'''

t, count = re.subn(pattern, replacement, t, count=1, flags=re.S)
if count != 1:
    fail("menu::draw replacement failed")
menu.write_text(t, encoding="utf-8")
print("[exact-runtime-v5] exact shell runtime + keyboard behavior applied")
