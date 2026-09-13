from pathlib import Path
import re
import shutil
import sys

root = Path(sys.argv[1]).resolve()
base = Path(__file__).resolve().parent / "overlay"
v = root / "cs2" / "velocity-cs2"


def die(message: str):
    raise SystemExit("[zh-ui] " + message)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        die(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


# ---------------------------------------------------------------------------
# Traditional-Chinese localization table. De-duplicate source keys first so a
# repeated English label can never produce duplicate switch cases.
# ---------------------------------------------------------------------------
localization_src = (base / "localization_zh_tw.hpp").read_text(encoding="utf-8")
seen = set()
filtered = []
for line in localization_src.splitlines():
    m = re.search(r'ZH\(\s*"([^"]+)"', line)
    if m:
        key = m.group(1)
        if key in seen:
            continue
        seen.add(key)
    filtered.append(line)
localization_src = "\n".join(filtered) + "\n"

loc_dir = v / "project" / "core" / "localization"
loc_dir.mkdir(parents=True, exist_ok=True)
(loc_dir / "zh_tw.hpp").write_text(localization_src, encoding="utf-8")

# ---------------------------------------------------------------------------
# XUI: preserve the original English widget ID, translate only visible text,
# and scale Windows client mouse coordinates into D3D viewport coordinates.
# This fixes stretched 4:3 modes such as 1440x1080 on a 1920x1080 desktop.
# ---------------------------------------------------------------------------
xui = v / "project" / "external" / "xdraw" / "xui" / "xui.cpp"
t = xui.read_text(encoding="utf-8")
if '#include <core/localization/zh_tw.hpp>' not in t:
    t = t.replace('#include <pch/pch.hpp>\n', '#include <pch/pch.hpp>\n#include <core/localization/zh_tw.hpp>\n', 1)

old_parse = '''\tstd::pair<std::string_view, std::string_view> parse_label( std::string_view label ) noexcept
\t{
\t\tconst auto pos = label.find( "##" );
\t\tif ( pos == std::string_view::npos )
\t\t{
\t\t\treturn { label, label };
\t\t}

\t\treturn { label.substr( 0, pos ), label };
\t}'''
new_parse = '''\tstd::pair<std::string_view, std::string_view> parse_label( std::string_view label ) noexcept
\t{
\t\tconst auto pos = label.find( "##" );
\t\tif ( pos == std::string_view::npos )
\t\t{
\t\t\treturn { localization::tr( label ), label };
\t\t}

\t\tconst auto visible = label.substr( 0, pos );
\t\treturn { localization::tr( visible ), label };
\t}'''
if 'localization::tr( visible )' not in t:
    t = replace_once(t, old_parse, new_parse, "parse_label")

hwnd_fn = '''\tstatic HWND& get_hwnd( )
\t{
\t\tstatic HWND h{ nullptr };
\t\treturn h;
\t}
'''
mouse_helper = '''\tstatic HWND& get_hwnd( )
\t{
\t\tstatic HWND h{ nullptr };
\t\treturn h;
\t}

\tstatic void update_mouse_position( xui::input_state& s, LPARAM lp )
\t{
\t\tconst auto raw_x = static_cast< float >( static_cast< short >( LOWORD( lp ) ) );
\t\tconst auto raw_y = static_cast< float >( static_cast< short >( HIWORD( lp ) ) );

\t\tRECT client{};
\t\tconst auto hwnd = get_hwnd( );
\t\tconst auto [ viewport_w, viewport_h ] = xdraw::viewport_size( );
\t\tif ( hwnd && GetClientRect( hwnd, &client ) )
\t\t{
\t\t\tconst auto client_w = client.right - client.left;
\t\t\tconst auto client_h = client.bottom - client.top;
\t\t\tif ( client_w > 0 && client_h > 0 && viewport_w > 0 && viewport_h > 0 )
\t\t\t{
\t\t\t\ts.mouse_x = raw_x * static_cast< float >( viewport_w ) / static_cast< float >( client_w );
\t\t\t\ts.mouse_y = raw_y * static_cast< float >( viewport_h ) / static_cast< float >( client_h );
\t\t\t\treturn;
\t\t\t}
\t\t}

\t\ts.mouse_x = raw_x;
\t\ts.mouse_y = raw_y;
\t}
'''
if 'static void update_mouse_position' not in t:
    t = replace_once(t, hwnd_fn, mouse_helper, "mouse helper")

old_mouse = '''\t\tcase WM_MOUSEMOVE:
\t\t\ts.mouse_x = static_cast< float >( static_cast< short >( LOWORD( lp ) ) );
\t\t\ts.mouse_y = static_cast< float >( static_cast< short >( HIWORD( lp ) ) );
\t\t\treturn true;'''
new_mouse = '''\t\tcase WM_MOUSEMOVE:
\t\t\tupdate_mouse_position( s, lp );
\t\t\treturn true;'''
if old_mouse in t:
    t = t.replace(old_mouse, new_mouse, 1)

for message in (
    "WM_LBUTTONDOWN", "WM_LBUTTONUP", "WM_LBUTTONDBLCLK",
    "WM_RBUTTONDOWN", "WM_RBUTTONUP", "WM_RBUTTONDBLCLK",
    "WM_MBUTTONDOWN", "WM_MBUTTONUP", "WM_MBUTTONDBLCLK",
):
    needle = f"\t\tcase {message}:\n"
    replacement = needle + "\t\t\tupdate_mouse_position( s, lp );\n"
    if needle in t and replacement not in t:
        t = t.replace(needle, replacement, 1)

xui.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# XDraw: translate direct text (combo/multicombo items) and add a runtime CJK
# fallback font from Windows. No font file is redistributed in this project.
# ---------------------------------------------------------------------------
xdraw = v / "project" / "external" / "xdraw" / "xdraw.cpp"
t = xdraw.read_text(encoding="utf-8")
if '#include <core/localization/zh_tw.hpp>' not in t:
    t = t.replace('#include <pch/pch.hpp>\n', '#include <pch/pch.hpp>\n#include <core/localization/zh_tw.hpp>\n', 1)
if '#include <filesystem>' not in t:
    t = t.replace('#include <algorithm>\n', '#include <algorithm>\n#include <filesystem>\n#include <fstream>\n', 1)

font_vectors = '''\t\t\tstd::vector<std::unique_ptr<font>> fonts{};
\t\t\tstd::vector<font*> font_stack{};'''
font_vectors_new = '''\t\t\tstd::vector<std::unique_ptr<font>> fonts{};
\t\t\t// Keeps the Windows system CJK font bytes alive for FT_New_Memory_Face.
\t\t\tstd::vector<std::byte> cjk_font_bytes{};
\t\t\tstd::vector<font*> font_stack{};'''
if 'cjk_font_bytes' not in t:
    t = replace_once(t, font_vectors, font_vectors_new, "xdraw CJK storage")

old_math = '''\t\tif ( math )
\t\t{
\t\t\tinter->fallback = math;
\t\t\tdetail::g.math_font = math;
\t\t}'''
new_math = '''\t\tif ( math )
\t\t{
\t\t\tdetail::g.math_font = math;
\t\t}

\t\tfont* cjk{};
\t\twchar_t windows_dir[ MAX_PATH ]{};
\t\tif ( GetWindowsDirectoryW( windows_dir, MAX_PATH ) )
\t\t{
\t\t\tconst auto fonts_dir = std::filesystem::path( windows_dir ) / L"Fonts";
\t\t\tconst std::array candidates{
\t\t\t\tfonts_dir / L"msjh.ttc",   // Microsoft JhengHei (Traditional Chinese)
\t\t\t\tfonts_dir / L"msjhbd.ttc",
\t\t\t\tfonts_dir / L"mingliu.ttc",
\t\t\t\tfonts_dir / L"msyh.ttc",
\t\t\t\tfonts_dir / L"simsun.ttc"
\t\t\t};

\t\t\tfor ( const auto& candidate : candidates )
\t\t\t{
\t\t\t\tstd::ifstream file( candidate, std::ios::binary | std::ios::ate );
\t\t\t\tif ( !file )
\t\t\t\t{
\t\t\t\t\tcontinue;
\t\t\t\t}

\t\t\t\tconst auto end = file.tellg( );
\t\t\t\tif ( end <= 0 )
\t\t\t\t{
\t\t\t\t\tcontinue;
\t\t\t\t}

\t\t\t\tdetail::g.cjk_font_bytes.resize( static_cast< std::size_t >( end ) );
\t\t\t\tfile.seekg( 0, std::ios::beg );
\t\t\t\tfile.read(
\t\t\t\t\treinterpret_cast< char* >( detail::g.cjk_font_bytes.data( ) ),
\t\t\t\t\tstatic_cast< std::streamsize >( detail::g.cjk_font_bytes.size( ) ) );
\t\t\t\tif ( !file )
\t\t\t\t{
\t\t\t\t\tdetail::g.cjk_font_bytes.clear( );
\t\t\t\t\tcontinue;
\t\t\t\t}

\t\t\t\tcjk = load_font(
\t\t\t\t\tstd::span<const std::byte>(
\t\t\t\t\t\tdetail::g.cjk_font_bytes.data( ), detail::g.cjk_font_bytes.size( ) ),
\t\t\t\t\t15.0f, 2048, 2048 );
\t\t\t\tif ( cjk )
\t\t\t\t{
\t\t\t\t\tbreak;
\t\t\t\t}
\t\t\t}
\t\t}

\t\tif ( cjk )
\t\t{
\t\t\tinter->fallback = cjk;
\t\t\tcjk->fallback = math;
\t\t}
\t\telse if ( math )
\t\t{
\t\t\tinter->fallback = math;
\t\t}'''
if 'fonts_dir / L"msjh.ttc"' not in t:
    t = replace_once(t, old_math, new_math, "CJK fallback initialization")

measure_sig = '\tstd::pair<float, float> font::measure( std::string_view str )\n\t{\n'
if measure_sig + '\t\tstr = localization::tr( str );\n' not in t:
    t = replace_once(
        t, measure_sig,
        measure_sig + '\t\tstr = localization::tr( str );\n',
        "font::measure localization")

text_sig = '\tvoid draw_list::text( float x, float y, std::string_view str, color col, text_style style, color shadow_col, font* f )\n\t{\n'
if text_sig + '\t\tstr = localization::tr( str );\n' not in t:
    t = replace_once(
        t, text_sig,
        text_sig + '\t\tstr = localization::tr( str );\n',
        "draw_list::text localization")

xdraw.write_text(t, encoding="utf-8")

# Give all menu font families a path to the CJK-enabled primary font.
fonts_cpp = v / "project" / "core" / "rendering" / "impl" / "fonts.cpp"
t = fonts_cpp.read_text(encoding="utf-8")
marker = '\t\tthis->load_family( this->smallest_pixel7, std::as_bytes( std::span{ resources::fonts::pixel7::smallest } ), { 9.0f, 10.5f, 14.0f } );\n'
if 'CJK-enabled primary fallback' not in t:
    addition = marker + '''

\t\t// CJK-enabled primary fallback (loaded from the local Windows font folder).
\t\tfor ( auto* family : { &this->inter_medium, &this->inter_bold, &this->smallest_pixel7 } )
\t\t{
\t\t\tfor ( auto*& font : family->sizes )
\t\t\t{
\t\t\t\tif ( font )
\t\t\t\t{
\t\t\t\t\tfont->fallback = xdraw::primary_font( );
\t\t\t\t}
\t\t\t}
\t\t}
'''
    t = replace_once(t, marker, addition, "fonts fallback")
fonts_cpp.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# Settings page: visible Lua import/open/reload controls.
# ---------------------------------------------------------------------------
config_cpp = v / "project" / "core" / "rendering" / "impl" / "menu" / "menu.config.cpp"
t = config_cpp.read_text(encoding="utf-8")
if '#include <core/scripting/scripting.hpp>' not in t:
    t = t.replace('#include <core/settings.hpp>\n', '#include <core/settings.hpp>\n#include <core/scripting/scripting.hpp>\n', 1)

t = t.replace(
    'const auto list_h = std::max( 80.0f, avail_h - btn_h - s.item_spacing_y );',
    'const auto list_h = std::max( 80.0f, avail_h - btn_h - 96.0f - s.item_spacing_y * 3.0f );')

lua_panel_anchor = '''\t\tif ( xui::button( "export", btn_w, btn_h ) )
\t\t{
\t\t\tconst auto name = has_selection ? detail::selected_name( ) : detail::search_buf;
\t\t\tconst auto code = config::export_share_words( name );
\t\t\tif ( !code.empty( ) )
\t\t\t{
\t\t\t\tdetail::copy_to_clipboard( code );
\t\t\t}
\t\t}

\t\txui::end_child( );'''
lua_panel = '''\t\tif ( xui::button( "export", btn_w, btn_h ) )
\t\t{
\t\t\tconst auto name = has_selection ? detail::selected_name( ) : detail::search_buf;
\t\t\tconst auto code = config::export_share_words( name );
\t\t\tif ( !code.empty( ) )
\t\t\t{
\t\t\t\tdetail::copy_to_clipboard( code );
\t\t\t}
\t\t}

\t\txui::layout::separator( );
\t\txui::text( "Lua 腳本", tokens::col_text );
\t\tconst auto lua_count = scripting::g_lua.script_count( );
\t\tconst auto lua_status = std::string( "已載入 " ) + std::to_string( lua_count ) + " 個腳本；腳本位置：DLL 同目錄\\\\scripts";
\t\txui::text( lua_status, tokens::col_text_dim );

\t\tconst auto lua_btn_w = ( avail_w - s.item_spacing_x * 2.0f ) / 3.0f;
\t\tif ( xui::button( "導入 Lua 腳本##lua_import", lua_btn_w, btn_h ) )
\t\t{
\t\t\tscripting::g_lua.import_script_dialog( rendering::g_context.get_window( ) );
\t\t}
\t\txui::layout::same_line( );
\t\tif ( xui::button( "開啟腳本資料夾##lua_folder", lua_btn_w, btn_h ) )
\t\t{
\t\t\tscripting::g_lua.open_script_directory( );
\t\t}
\t\txui::layout::same_line( );
\t\tif ( xui::button( "重新載入 Lua##lua_reload", lua_btn_w, btn_h ) )
\t\t{
\t\t\tscripting::g_lua.reload_all( );
\t\t}

\t\txui::end_child( );'''
if '導入 Lua 腳本##lua_import' not in t:
    t = replace_once(t, lua_panel_anchor, lua_panel, "Lua settings panel")
config_cpp.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# Natural Chinese units and wear names that are generated dynamically.
# ---------------------------------------------------------------------------
menu_dir = v / "project" / "core" / "rendering" / "impl" / "menu"
for source in menu_dir.glob("menu.*.cpp"):
    text = source.read_text(encoding="utf-8")
    text = text.replace('"%d tick(s)"', '"%d 個刻度"')
    text = text.replace('"%d ms"', '"%d 毫秒"')
    text = text.replace('"%.1fs"', '"%.1f 秒"')
    text = text.replace('"%.2fs"', '"%.2f 秒"')
    text = text.replace('"%.0fm"', '"%.0f 公尺"')
    if source.name == "menu.skins.cpp":
        text = text.replace('return "FN";', 'return "嶄新出廠";')
        text = text.replace('return "MW";', 'return "輕微磨損";')
        text = text.replace('return "FT";', 'return "久經沙場";')
        text = text.replace('return "WW";', 'return "破損不堪";')
        text = text.replace('return "BS";', 'return "戰痕累累";')
    source.write_text(text, encoding="utf-8")

# Add header to the VS project for source visibility (not required by the compiler,
# but useful when the full project is opened in Visual Studio).
vcx = v / "velocity-cs2.vcxproj"
t = vcx.read_text(encoding="utf-8")
entry = '    <ClInclude Include="project\\core\\localization\\zh_tw.hpp" />\n'
if 'project\\core\\localization\\zh_tw.hpp' not in t:
    marker = '    <ClInclude Include="project\\core\\scripting\\scripting.hpp" />\n'
    if marker in t:
        t = t.replace(marker, entry + marker, 1)
    else:
        t = t.replace('  <ItemGroup>\n    <ClInclude ', '  <ItemGroup>\n' + entry + '    <ClInclude ', 1)
vcx.write_text(t, encoding="utf-8")

print("[zh-ui] Traditional Chinese UI, CJK fallback, 1440x1080 mouse scaling and Lua import UI applied")
