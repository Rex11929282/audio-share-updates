from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "MCB-CS2"


def fail(msg: str):
    raise SystemExit("[panel-v5] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        fail(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, repl: str, label: str, flags=re.S) -> str:
    out, count = re.subn(pattern, repl, text, count=1, flags=flags)
    if count != 1:
        fail(f"{label}: expected 1 regex occurrence, got {count}")
    return out


# ---------------------------------------------------------------------------
# 1) Neverlose-inspired MCB glass palette/layout derived from the uploaded
#    showcase: dark blue-black glass, cyan accent, wide navigation rail.
# ---------------------------------------------------------------------------
xdraw_hpp = v / "project/external/xdraw/xdraw.hpp"
t = xdraw_hpp.read_text(encoding="utf-8")
replacements = {
    "inline xdraw::color col_accent{ 173, 192, 255, 255 };": "inline xdraw::color col_accent{ 19, 191, 245, 255 };",
    "inline xdraw::color col_dark{ 17, 17, 17, 255 };": "inline xdraw::color col_dark{ 5, 11, 17, 255 };",
    "inline xdraw::color col_text{ 221, 229, 255, 235 };": "inline xdraw::color col_text{ 238, 243, 247, 245 };",
    "inline xdraw::color col_text_dim{ 173, 192, 255, 82 };": "inline xdraw::color col_text_dim{ 138, 150, 163, 180 };",
    "inline xdraw::color col_card{ 17, 17, 17, 82 };": "inline xdraw::color col_card{ 7, 19, 29, 190 };",
    "inline xdraw::color col_elevated{ 31, 31, 35, 118 };": "inline xdraw::color col_elevated{ 8, 18, 27, 215 };",
    "constexpr auto sidebar_w{ 42.0f };": "constexpr auto sidebar_w{ 203.0f };",
    "constexpr auto subtab_bar_h{ 35.0f };": "constexpr auto subtab_bar_h{ 66.0f };",
}
for old, new in replacements.items():
    if old in t:
        t = t.replace(old, new, 1)
xdraw_hpp.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# 2) Main menu: uploaded panel dimensions by default, still user-resizable.
# ---------------------------------------------------------------------------
menu = v / "project/core/rendering/impl/menu/menu.core.cpp"
t = menu.read_text(encoding="utf-8")
t = t.replace("constexpr float preferred_w{ 900.0f };", "constexpr float preferred_w{ 846.0f };", 1)
t = t.replace("constexpr float preferred_h{ 700.0f };", "constexpr float preferred_h{ 682.0f };", 1)
t = t.replace("const auto min_w = std::min( 620.0f, available_w );", "const auto min_w = std::min( 760.0f, available_w );", 1)
t = t.replace("const auto min_h = std::min( 460.0f, available_h );", "const auto min_h = std::min( 560.0f, available_h );", 1)
t = t.replace('xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, true, 620.0f, 460.0f, menu_reveal )',
              'xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, true, 760.0f, 560.0f, menu_reveal )', 1)

# Replace the narrow icon-only sidebar with a wide glass navigation rail.
sidebar_pattern = r"\tvoid menu::draw_side_bar\( float h \)\n\t\{.*?\n\t\}\n\n\tvoid menu::try_load_user_avatar"
sidebar_repl = r'''\tvoid menu::draw_side_bar( float h )
\t{
\t\tauto& dl = xui::draw::current( );
\t\tconst auto& input = xui::ctx( ).input;
\t\tconst auto sb_x = this->m_x + tokens::gap;
\t\tconst auto sb_y = this->m_y + tokens::gap;
\t\tconst auto sb_w = tokens::sidebar_w;
\t\tconst auto sb_h = h;
\t\tconstexpr auto round{ 8.0f };

\t\t// Dark translucent rail based on the uploaded MCB glass showcase.
\t\tdl.rect_filled_blurred( sb_x, sb_y, sb_w, sb_h, xdraw::corner_radius{ round }, xdraw::color{ 255, 255, 255, 155 } );
\t\tdl.rect_filled( sb_x, sb_y, sb_w, sb_h, xdraw::color{ 7, 17, 26, 214 }, xdraw::corner_radius{ round } );
\t\tdl.rect( sb_x, sb_y, sb_w, sb_h, xdraw::color{ 164, 220, 255, 28 }, xdraw::corner_radius{ round }, 1.0f );

\t\tconstexpr float brand_h{ 58.0f };
\t\tdl.line( sb_x + 1.0f, sb_y + brand_h, sb_x + sb_w - 1.0f, sb_y + brand_h, xdraw::color{ 164, 220, 255, 22 }, 1.0f );
\t\tdetail::draw_mcb_mark( dl, sb_x + 16.0f, sb_y + 16.0f, 54.0f, 26.0f, tokens::col_accent );
\t\tdl.text( sb_x + 80.0f, sb_y + 19.0f, "MCB", xdraw::color{ 245, 251, 255, 255 } );
\t\tdl.text( sb_x + 80.0f, sb_y + 34.0f, "CONTROL PANEL", xdraw::color{ 77, 91, 105, 220 } );

\t\tstatic constexpr const char* nav_labels[ 7 ]
\t\t{
\t\t\t"ragebot", "legitbot", "player", "world", "skins", "misc", "config"
\t\t};

\t\tconst auto nav_x = sb_x + 8.0f;
\t\tconst auto nav_w = sb_w - 16.0f;
\t\tconst auto nav_top = sb_y + brand_h + 17.0f;
\t\tconstexpr auto item_h{ 38.0f };
\t\tconstexpr auto item_gap{ 4.0f };

\t\tfor ( auto i = 0; i < 7; ++i )
\t\t{
\t\t\tconst auto y = nav_top + static_cast<float>( i ) * ( item_h + item_gap );
\t\t\tconst auto r = xui::rect{ nav_x, y, nav_w, item_h };
\t\t\tconst auto hovered = !xui::ctx( ).overlay_blocking( ) && input.in_rect( r );
\t\t\tconst auto active = this->m_tab == i;
\t\t\tconst auto hover_t = xui::anim::lerp( xui::fnv1a( "mcb_nav_hover" ) + i, hovered ? 1.0f : 0.0f, 14.0f );
\t\t\tconst auto active_t = xui::anim::lerp( xui::fnv1a( "mcb_nav_active" ) + i, active ? 1.0f : 0.0f, 12.0f );

\t\t\tif ( hover_t > 0.01f || active_t > 0.01f )
\t\t\t{
\t\t\t\tconst auto alpha = static_cast<std::uint8_t>( 18.0f + hover_t * 24.0f + active_t * 34.0f );
\t\t\t\tdl.rect_filled( r.x, r.y, r.w, r.h, xdraw::color{ 19, 191, 245, alpha }, xdraw::corner_radius{ 5.0f } );
\t\t\t}
\t\t\tif ( active_t > 0.01f )
\t\t\t{
\t\t\t\tdl.rect_filled( r.x, r.y + 5.0f, 2.0f, r.h - 10.0f,
\t\t\t\t\txdraw::color{ 19, 191, 245, static_cast<std::uint8_t>( 255.0f * active_t ) },
\t\t\t\t\txdraw::corner_radius{ 1.0f } );
\t\t\t}

\t\t\tconst auto icon_size = 18.0f;
\t\t\tconst auto icon_x = r.x + 12.0f;
\t\t\tconst auto icon_y = r.y + ( r.h - icon_size ) * 0.5f;
\t\t\tif ( this->m_textures.tabs[ i ].resource )
\t\t\t{
\t\t\t\tdl.image( icon_x, icon_y, icon_size, icon_size, this->m_textures.tabs[ i ].resource.Get( ),
\t\t\t\t\txdraw::color{ 19, 191, 245, active ? 255 : static_cast<std::uint8_t>( 180 + hover_t * 60.0f ) } );
\t\t\t}

\t\t\tconst auto label = localization::tr( nav_labels[ i ] );
\t\t\tconst auto text_col = active
\t\t\t\t? xdraw::color{ 244, 251, 255, 255 }
\t\t\t\t: xui::lerp( xdraw::color{ 168, 177, 187, 225 }, xdraw::color{ 220, 233, 242, 245 }, hover_t );
\t\t\tconst auto th = xdraw::measure_text( label ).second;
\t\t\tdl.text( r.x + 43.0f, r.y + ( r.h - th ) * 0.5f, label, text_col );

\t\t\tif ( hovered && input.mouse_clicked )
\t\t\t{
\t\t\t\tthis->m_tab = i;
\t\t\t\tthis->m_subtab = 0;
\t\t\t\tthis->close_search( );
\t\t\t}
\t\t}

\t\t// Profile/status card at the bottom of the rail.
\t\tthis->try_load_user_avatar( );
\t\tconstexpr auto profile_h{ 66.0f };
\t\tconst auto py = sb_y + sb_h - profile_h;
\t\tdl.line( sb_x + 1.0f, py, sb_x + sb_w - 1.0f, py, xdraw::color{ 164, 220, 255, 20 }, 1.0f );
\t\tdl.rect_filled( sb_x + 1.0f, py + 1.0f, sb_w - 2.0f, profile_h - 2.0f, xdraw::color{ 5, 12, 19, 160 }, xdraw::corner_radius{ 0.0f, 0.0f, round, round } );

\t\tconst auto ax = sb_x + 13.0f;
\t\tconst auto ay = py + 12.0f;
\t\tconstexpr auto avatar_size{ 40.0f };
\t\tif ( this->m_textures.user.resource )
\t\t{
\t\t\tdl.image( ax, ay, avatar_size, avatar_size, this->m_textures.user.resource.Get( ), xdraw::corner_radius{ 20.0f } );
\t\t}
\t\telse
\t\t{
\t\t\tdl.circle_filled( ax + 20.0f, ay + 20.0f, 20.0f, xdraw::color{ 8, 31, 46, 255 }, 32, true );
\t\t\tdetail::draw_mcb_mark( dl, ax + 7.0f, ay + 11.0f, 26.0f, 18.0f, tokens::col_accent );
\t\t}
\t\tdl.circle( ax + 20.0f, ay + 20.0f, 20.0f, xdraw::color{ 19, 191, 245, 115 }, 1.0f, 32, true );
\t\tdl.text( ax + 51.0f, ay + 6.0f, "MCB", xdraw::color{ 238, 245, 249, 245 } );
\t\tdl.text( ax + 51.0f, ay + 23.0f, "Lua / GUI Ready", xdraw::color{ 75, 135, 160, 220 } );
\t}

\tvoid menu::try_load_user_avatar'''
t = regex_once(t, sidebar_pattern, sidebar_repl, "wide MCB sidebar")

# Replace theme synchronization with the glass values from the uploaded panel.
t = regex_once(
    t,
    r"\tvoid menu::sync_theme_style\( \) const\n\t\{.*?\n\t\}\n\n\tvoid menu::draw_top_bar",
    r'''\tvoid menu::sync_theme_style( ) const
\t{
\t\tauto& style = xui::ctx( ).style;
\t\tstyle.window_bg = xdraw::color{ 5, 9, 14, 202 };
\t\tstyle.window_border = xdraw::color{ 164, 220, 255, 30 };
\t\tstyle.child_bg = xdraw::color{ 7, 19, 29, 176 };
\t\tstyle.child_border = xdraw::color{ 171, 223, 255, 26 };
\t\tstyle.checkbox_bg = xdraw::color{ 8, 20, 30, 210 };
\t\tstyle.checkbox_border = xdraw::color{ 20, 45, 64, 220 };
\t\tstyle.checkbox_mark = tokens::col_accent;
\t\tstyle.checkbox_mark_icon = xdraw::color{ 5, 11, 17, 255 };
\t\tstyle.slider_track = xdraw::color{ 15, 32, 46, 230 };
\t\tstyle.slider_fill = tokens::col_accent;
\t\tstyle.button_bg = xdraw::color{ 8, 20, 30, 215 };
\t\tstyle.button_border = xdraw::color{ 22, 40, 57, 220 };
\t\tstyle.button_hovered = xdraw::color{ 10, 26, 39, 235 };
\t\tstyle.button_active = tokens::col_accent;
\t\tstyle.keybind_bg = xdraw::color{ 8, 20, 30, 215 };
\t\tstyle.keybind_border = xdraw::color{ 22, 46, 64, 220 };
\t\tstyle.keybind_waiting = tokens::col_accent;
\t\tstyle.combo_bg = xdraw::color{ 8, 18, 27, 220 };
\t\tstyle.combo_border = xdraw::color{ 22, 40, 57, 220 };
\t\tstyle.combo_arrow = xdraw::color{ 102, 119, 135, 235 };
\t\tstyle.combo_hovered = xdraw::color{ 10, 26, 39, 235 };
\t\tstyle.combo_popup_bg = xdraw::color{ 6, 16, 25, 242 };
\t\tstyle.combo_popup_border = xdraw::color{ 22, 48, 68, 235 };
\t\tstyle.combo_popup_item_hovered = xdraw::color{ 10, 31, 45, 245 };
\t\tstyle.combo_popup_item_selected = xdraw::color{ 19, 191, 245, 36 };
\t\tstyle.popup_bg = xdraw::color{ 6, 16, 25, 242 };
\t\tstyle.popup_border = xdraw::color{ 22, 48, 68, 235 };
\t\tstyle.picker_bg = xdraw::color{ 8, 20, 30, 220 };
\t\tstyle.picker_border = xdraw::color{ 22, 45, 64, 220 };
\t\tstyle.picker_popup_bg = xdraw::color{ 6, 16, 25, 242 };
\t\tstyle.picker_popup_border = xdraw::color{ 22, 48, 68, 235 };
\t\tstyle.text_input_bg = xdraw::color{ 8, 18, 27, 228 };
\t\tstyle.text_input_border = xdraw::color{ 22, 40, 57, 225 };
\t\tstyle.separator = xdraw::color{ 20, 37, 54, 180 };
\t\tstyle.text = tokens::col_text;
\t\tstyle.text_dim = tokens::col_text_dim;
\t\tstyle.accent = tokens::col_accent;

\t\tstyle.rounding = 8.0f;
\t\tstyle.checkbox_rounding = 4.0f;
\t\tstyle.button_rounding = 5.0f;
\t\tstyle.combo_rounding = 5.0f;
\t\tstyle.combo_popup_rounding = 6.0f;
\t\tstyle.popup_rounding = 7.0f;
\t\tstyle.picker_popup_rounding = 7.0f;
\t\tstyle.text_input_rounding = 5.0f;
\t\tstyle.window_pad_x = 10.0f;
\t\tstyle.window_pad_y = 10.0f;
\t\tstyle.item_spacing_x = 8.0f;
\t\tstyle.item_spacing_y = 8.0f;
\t\tstyle.border_thickness = 1.0f;
\t}

\tvoid menu::draw_top_bar''',
    "Neverlose glass theme")

# Intro should be visible briefly instead of finishing in the same frame assets become ready.
old_intro = '''\t\tif ( pack_ready && this->m_intro_assets_ready_at < 0.0f )
\t\t{
\t\t\tthis->m_intro_assets_ready_at = this->m_intro_elapsed;
\t\t}

\t\tif ( this->m_intro_assets_ready_at >= 0.0f )
\t\t{
\t\t\tthis->m_intro_finished = true;
\t\t\treturn false;
\t\t}'''
new_intro = '''\t\tif ( pack_ready && this->m_intro_assets_ready_at < 0.0f )
\t\t{
\t\t\tthis->m_intro_assets_ready_at = this->m_intro_elapsed;
\t\t}

\t\tconstexpr float mcb_intro_min_seconds{ 0.55f };
\t\tconstexpr float mcb_intro_ready_hold_seconds{ 0.12f };
\t\tif ( this->m_intro_assets_ready_at >= 0.0f &&
\t\t\t this->m_intro_elapsed >= mcb_intro_min_seconds &&
\t\t\t this->m_intro_elapsed - this->m_intro_assets_ready_at >= mcb_intro_ready_hold_seconds )
\t\t{
\t\t\tthis->m_intro_finished = true;
\t\t\treturn false;
\t\t}'''
if old_intro in t:
    t = t.replace(old_intro, new_intro, 1)

menu.write_text(t, encoding="utf-8")

# Config-page min sliders must match the new usable wide-panel minimum.
config_menu = v / "project/core/rendering/impl/menu/menu.config.cpp"
t = config_menu.read_text(encoding="utf-8")
t = t.replace("std::max( 620.0f, static_cast< float >( ui_vw ) - 20.0f )", "std::max( 760.0f, static_cast< float >( ui_vw ) - 20.0f )")
t = t.replace("std::max( 460.0f, static_cast< float >( ui_vh ) - 20.0f )", "std::max( 560.0f, static_cast< float >( ui_vh ) - 20.0f )")
t = t.replace('"面板寬度##ui_panel_width", this->m_w, 620.0f, max_ui_w', '"面板寬度##ui_panel_width", this->m_w, 760.0f, max_ui_w')
t = t.replace('"面板高度##ui_panel_height", this->m_h, 460.0f, max_ui_h', '"面板高度##ui_panel_height", this->m_h, 560.0f, max_ui_h')
config_menu.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 3) Remaining MCB branding and one-time Registry migration.
# ---------------------------------------------------------------------------
impacts = v / "project/core/features/misc/impl/impacts.cpp"
t = impacts.read_text(encoding="utf-8")
t = t.replace('"[velocity]"', '"[MCB]"')
impacts.write_text(t, encoding="utf-8")

config_hpp = v / "project/external/config.hpp"
t = config_hpp.read_text(encoding="utf-8")
if '#include <mutex>' not in t:
    t = t.replace('#include <windows.h>\n', '#include <windows.h>\n#include <mutex>\n', 1)
old_root = 'inline constexpr wchar_t k_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };'
if old_root in t:
    t = t.replace(old_root,
        'inline constexpr wchar_t k_root_key[ ]{ L"Software\\\\MCB\\\\configs" };\n\t\tinline constexpr wchar_t k_legacy_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };', 1)

if "migrate_legacy_once" not in t:
    root_marker = 'inline constexpr wchar_t k_legacy_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };\n'
    migrate = r'''

\t\tinline void migrate_legacy_once( )
\t\t{
\t\t\tstatic std::once_flag once{};
\t\t\tstd::call_once( once, [ ]
\t\t\t{
\t\t\t\tHKEY legacy{};
\t\t\t\tif ( RegOpenKeyExW( HKEY_CURRENT_USER, k_legacy_root_key, 0, KEY_READ, &legacy ) != ERROR_SUCCESS ) return;

\t\t\t\tHKEY current{};
\t\t\t\tDWORD disposition{};
\t\t\t\tif ( RegCreateKeyExW( HKEY_CURRENT_USER, k_root_key, 0, nullptr, 0, KEY_READ | KEY_WRITE, nullptr, &current, &disposition ) != ERROR_SUCCESS )
\t\t\t\t{
\t\t\t\t\tRegCloseKey( legacy );
\t\t\t\t\treturn;
\t\t\t\t}

\t\t\t\tDWORD index{};
\t\t\t\tfor ( ;; )
\t\t\t\t{
\t\t\t\t\twchar_t name[ 256 ]{};
\t\t\t\t\tDWORD name_len = static_cast<DWORD>( std::size( name ) );
\t\t\t\t\tDWORD type{};
\t\t\t\t\tDWORD size{};
\t\t\t\t\tconst auto info = RegEnumValueW( legacy, index++, name, &name_len, nullptr, &type, nullptr, &size );
\t\t\t\t\tif ( info == ERROR_NO_MORE_ITEMS ) break;
\t\t\t\t\tif ( info != ERROR_SUCCESS || type != REG_BINARY || size == 0 || size > 8u * 1024u * 1024u ) continue;

\t\t\t\t\tDWORD existing_type{};
\t\t\t\t\tDWORD existing_size{};
\t\t\t\t\tif ( RegQueryValueExW( current, name, nullptr, &existing_type, nullptr, &existing_size ) == ERROR_SUCCESS ) continue;

\t\t\t\t\tstd::vector<std::uint8_t> data( size );
\t\t\t\t\tDWORD read_size = size;
\t\t\t\t\tif ( RegQueryValueExW( legacy, name, nullptr, &type, data.data( ), &read_size ) == ERROR_SUCCESS && read_size == size )
\t\t\t\t\t{
\t\t\t\t\t\tRegSetValueExW( current, name, 0, REG_BINARY, data.data( ), size );
\t\t\t\t\t}
\t\t\t\t}

\t\t\t\tRegCloseKey( current );
\t\t\t\tRegCloseKey( legacy );
\t\t\t} );
\t\t}
'''
    if root_marker in t:
        t = t.replace(root_marker, root_marker + migrate, 1)

# Call migration at the start of every persistent registry operation.
for signature in (
    '\t\tinline bool save( std::wstring_view name )\n\t\t{\n',
    '\t\tinline bool load( std::wstring_view name )\n\t\t{\n',
    '\t\tinline bool remove( std::wstring_view name )\n\t\t{\n',
    '\t\tinline std::vector<std::wstring> list( )\n\t\t{\n',
):
    if signature in t:
        t = t.replace(signature, signature + '\t\t\tmigrate_legacy_once( );\n', 1)

config_hpp.write_text(t, encoding="utf-8")

print("[panel-v5] MCB glass panel + final visible branding applied")
