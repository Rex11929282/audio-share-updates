"""Apply responsive input mapping without duplicating prior localization patches.

No gameplay or protection settings are changed. The Windows regression compiles
our actual generated mapping helpers, with real Win32 coordinate conversion and
an explicitly synthetic viewport. It does not execute the game or original Lua.
"""
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
v = next((p for p in (root / 'cs2/MCB-CS2', root / 'cs2/velocity-cs2') if p.exists()), None)
if v is None:
    raise SystemExit('[ui-map] target project not found')
overlay = Path(__file__).resolve().parent / 'overlay'
header = overlay / 'ui_coordinate_map.hpp'
xui_dir = v / 'project/external/xdraw/xui'
xui_cpp = xui_dir / 'xui.cpp'
if not header.is_file() or not xui_cpp.is_file():
    raise SystemExit('[ui-map] input source/header missing')
shutil.copy2(header, xui_dir / header.name)
t = xui_cpp.read_text(encoding='utf-8')


def replace_once(old: str, new: str, label: str):
    global t
    if new in t:
        if t.count(new) != 1:
            raise SystemExit('[ui-map] duplicate completed patch: ' + label)
        return
    if t.count(old) != 1:
        raise SystemExit(f'[ui-map] {label}: expected one anchor, got {t.count(old)}')
    t = t.replace(old, new, 1)


replace_once('#include "xui.hpp"\n', '#include "xui.hpp"\n#include "ui_coordinate_map.hpp"\n', 'header')
hwnd_block = '''\tstatic HWND& get_hwnd( )
\t{
\t\tstatic HWND h{ nullptr };
\t\treturn h;
\t}
'''
helpers = r'''
	// MCB_UI_MAP_V2: this replaces, not supplements, the zh-ui LPARAM helper.
	static void update_mouse_position(
		xui::input_state& input,
		float client_x,
		float client_y )
	{
		RECT client{};
		const auto hwnd = get_hwnd( );
		const auto [ viewport_w, viewport_h ] = xdraw::viewport_size( );
		if ( hwnd && GetClientRect( hwnd, &client ) )
		{
			const auto mapped = xui::coord::client_to_viewport(
				client_x, client_y,
				static_cast<float>( client.right - client.left ),
				static_cast<float>( client.bottom - client.top ),
				static_cast<float>( viewport_w ),
				static_cast<float>( viewport_h ) );
			input.mouse_x = mapped.x;
			input.mouse_y = mapped.y;
			return;
		}
		input.mouse_x = client_x;
		input.mouse_y = client_y;
	}

	static void update_mouse_position(
		xui::input_state& input,
		LPARAM lp )
	{
		update_mouse_position(input,
			static_cast<float>(static_cast<short>(LOWORD(lp))),
			static_cast<float>(static_cast<short>(HIWORD(lp))));
	}

	static void update_mouse_position_from_screen(
		xui::input_state& input,
		LPARAM lp )
	{
		POINT point{
			static_cast<LONG>(static_cast<short>(LOWORD(lp))),
			static_cast<LONG>(static_cast<short>(HIWORD(lp))) };
		const auto hwnd = get_hwnd( );
		if ( hwnd && ScreenToClient( hwnd, &point ) )
		{
			update_mouse_position(input,
				static_cast<float>(point.x), static_cast<float>(point.y));
		}
	}
'''

# The earlier apply_zh_ui.py already adds the following overload. Remove only
# that reviewed legacy definition; unfamiliar helpers are a hard conflict.
if 'MCB_UI_MAP_V2' not in t:
    legacy_pattern = r'\tstatic void update_mouse_position\( xui::input_state& s, LPARAM lp \)\n\t\{.*?\n\t\}\n'
    found = list(re.finditer(legacy_pattern, t, re.S))
    if len(found) > 1:
        raise SystemExit('[ui-map] duplicated legacy helper')
    if found:
        old = found[0].group(0)
        for anchor in ('raw_x', 'raw_y', 'GetClientRect', 'viewport_w', 's.mouse_x'):
            if anchor not in old:
                raise SystemExit('[ui-map] unrecognized legacy mapping body')
        t = t[:found[0].start()] + t[found[0].end():]
    if 'static void update_mouse_position' in t:
        raise SystemExit('[ui-map] unrecognized existing mapping helpers')
replace_once(hwnd_block, hwnd_block + helpers, 'replace legacy mapping')

replace_once(
'''\t\tcase WM_MOUSEMOVE:
\t\t\ts.mouse_x = static_cast< float >( static_cast< short >( LOWORD( lp ) ) );
\t\t\ts.mouse_y = static_cast< float >( static_cast< short >( HIWORD( lp ) ) );
\t\t\treturn true;
''',
'''\t\tcase WM_MOUSEMOVE:
\t\t\tupdate_mouse_position( s, lp );
\t\t\treturn true;
''', 'WM_MOUSEMOVE')
for label in ('WM_LBUTTONDOWN', 'WM_LBUTTONUP', 'WM_RBUTTONDOWN', 'WM_RBUTTONUP',
              'WM_MBUTTONDOWN', 'WM_MBUTTONUP', 'WM_XBUTTONDOWN', 'WM_XBUTTONUP'):
    marker = f'\t\tcase {label}:\n'
    if label.startswith('WM_XBUTTON'):
        marker += '\t\t{\n'
    replace_once(marker, marker + '\t\t\tupdate_mouse_position( s, lp );\n', label)

wheel_old = '''\t\tcase WM_MOUSEWHEEL:
\t\t{
\t\t\tconst auto delta = static_cast< float >( GET_WHEEL_DELTA_WPARAM( wp ) ) / static_cast< float >( WHEEL_DELTA );
'''
wheel_new = wheel_old.replace('\t\t{\n', '\t\t{\n\t\t\tupdate_mouse_position_from_screen( s, lp );\n', 1)
replace_once(wheel_old, wheel_new, 'wheel coordinates')

legacy_focus = '''\t\tcase WM_KILLFOCUS:
\t\tcase WM_CANCELMODE:
\t\tcase WM_CAPTURECHANGED:
\t\t\ts.mouse_down = false;
\t\t\ts.rmb_down = false;
\t\t\ts.keys.clear( );
\t\t\ts.focus_lost = true;
\t\t\treturn true;
'''
focus = '''\t\tcase WM_KILLFOCUS:
\t\tcase WM_CANCELMODE:
\t\tcase WM_CAPTURECHANGED:
\t\t\t// Cancellation is not a successful mouse release.
\t\t\ts.mouse_down = false;
\t\t\ts.mouse_clicked = false;
\t\t\ts.mouse_released = false;
\t\t\ts.rmb_down = false;
\t\t\ts.rmb_clicked = false;
\t\t\ts.rmb_released = false;
\t\t\ts.keys.clear( );
\t\t\ts.scroll_delta = 0.0f;
\t\t\ts.char_count = 0;
\t\t\ts.focus_lost = true;
\t\t\treturn true;
'''
replace_once(legacy_focus, focus, 'merge existing focus/capture cancellation')

replace_once(
'''\t\tconst auto abs = layout::item( w, h );
\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto hovered = can_interact && input.in_rect( abs );
''',
'''\t\tconst auto abs = layout::item( w, h );
\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto interaction = abs.expand( 2.0f );
\t\tconst auto hovered = can_interact && input.in_rect( interaction );
''', 'button hit target')
replace_once(
'''\t\tconst auto full_w = !display.empty( ) ? ( abs.w + st.item_spacing_x + lw ) : abs.w;
\t\tconst auto extended = rect{ abs.x, abs.y, full_w, abs.h };

\t\tconst auto can_interact = !c.overlay_blocking( );
''',
'''\t\tconst auto full_w = !display.empty( ) ? ( abs.w + st.item_spacing_x + lw ) : abs.w;
\t\tconst auto extended = rect{ abs.x, abs.y, full_w, abs.h };
\t\tconst auto interaction = extended.expand( 3.0f );

\t\tconst auto can_interact = !c.overlay_blocking( );
''', 'checkbox geometry')
replace_once('\t\tconst auto hovered = can_interact && input.in_rect( extended ) && !badge_listening;\n',
             '\t\tconst auto hovered = can_interact && input.in_rect( interaction ) && !badge_listening;\n', 'checkbox use')
replace_once(
'''\t\t\tconst auto track_rect = rect{ abs.x, track_y, slider_width, track_height };
\t\t\tconst auto slider_hit_rect = rect{ abs.x, abs.y + text_height + spacing, slider_width, slider_area_h };

\t\t\tconst auto can_interact = !c.overlay_blocking( );
''',
'''\t\t\tconst auto track_rect = rect{ abs.x, track_y, slider_width, track_height };
\t\t\tconst auto slider_hit_rect = rect{ abs.x, abs.y + text_height + spacing, slider_width, slider_area_h };
\t\t\tconst auto slider_interaction = slider_hit_rect.expand( 4.0f );

\t\t\tconst auto can_interact = !c.overlay_blocking( );
''', 'slider geometry')
t = t.replace('input.in_rect( slider_hit_rect )', 'input.in_rect( slider_interaction )')
replace_once(
'''\t\tconst auto abs = layout::item( avail_w, input_h );
\t\tconst auto frame = rect{ abs.x, abs.y, avail_w, input_h };

\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto hovered = can_interact && input.in_rect( frame );
''',
'''\t\tconst auto abs = layout::item( avail_w, input_h );
\t\tconst auto frame = rect{ abs.x, abs.y, avail_w, input_h };
\t\tconst auto interaction = frame.expand( 3.0f );

\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto hovered = can_interact && input.in_rect( interaction );
''', 'text target')
replace_once(
'''\t\tconst auto swatch_rect = rect{ abs.x + x_offset, swatch_y, sw, sh };

\t\tconst auto can_interact = !c.overlay_blocking( ) || is_open;
\t\tconst auto hovered = can_interact && input.in_rect( swatch_rect );
''',
'''\t\tconst auto swatch_rect = rect{ abs.x + x_offset, swatch_y, sw, sh };
\t\tconst auto swatch_interaction = swatch_rect.expand( 3.0f );

\t\tconst auto can_interact = !c.overlay_blocking( ) || is_open;
\t\tconst auto hovered = can_interact && input.in_rect( swatch_interaction );
''', 'swatch target')

if len(re.findall(r'static void update_mouse_position\s*\(', t)) != 2:
    raise SystemExit('[ui-map] mapping overload count mismatch')
for msg in ('WM_KILLFOCUS', 'WM_CANCELMODE', 'WM_CAPTURECHANGED'):
    if t.count('case ' + msg + ':') != 1:
        raise SystemExit('[ui-map] duplicate/missing cancellation case: ' + msg)
if t.count(hwnd_block + helpers) != 1 or t.count(focus) != 1:
    raise SystemExit('[ui-map] generated helper/cancellation mismatch')
xui_cpp.write_text(t, encoding='utf-8')

# Compile the exact helper and cancellation blocks emitted above. The viewport
# and input container are mocks; HWND and client/screen conversion are Win32.
release = Path('release').resolve()
testdir = release / 'ui-map-test'
testdir.mkdir(parents=True, exist_ok=True)
shutil.copy2(header, testdir / header.name)
source = r'''#define NOMINMAX
#include <windows.h>
#include <cassert>
#include <cmath>
#include <utility>
#include <vector>
#include <iostream>
#include "ui_coordinate_map.hpp"
namespace xui {
struct input_state {
    float mouse_x{}, mouse_y{}, scroll_delta{};
    bool mouse_down{}, mouse_clicked{}, mouse_released{};
    bool rmb_down{}, rmb_clicked{}, rmb_released{}, focus_lost{};
    unsigned char_count{};
    std::vector<int> keys;
};
}
namespace xdraw {
static int viewport_w = 1440, viewport_h = 1080;
std::pair<int,int> viewport_size() { return {viewport_w, viewport_h}; }
}
'''
source += hwnd_block + helpers
source += '\nbool cancel_input(xui::input_state& s, UINT msg) { switch(msg) {\n' + focus + '\n default: return false; } }\n'
source += r'''
int main() {
    int checks = 0;
    const auto require = [&](bool condition) {
        if (!condition) std::abort();
        ++checks;
    };
    const HWND hwnd = CreateWindowExW(0, L"STATIC", L"MCB map regression",
        WS_POPUP, 40, 50, 1920, 1080, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    require(hwnd != nullptr);
    get_hwnd() = hwnd;
    RECT client{};
    require(GetClientRect(hwnd, &client) != FALSE);
    require(client.right > 0 && client.bottom > 0);
    xui::input_state input{};
    const auto near_value = [](float a, float b) { return std::fabs(a - b) < 0.01f; };
    for (const auto viewport : {std::pair{1440,1080}, std::pair{1920,1080}, std::pair{1280,720}}) {
        xdraw::viewport_w = viewport.first; xdraw::viewport_h = viewport.second;
        for (const auto p : {POINT{0,0}, POINT{960,540}, POINT{1919,1079}, POINT{-20,-30}}) {
            update_mouse_position(input, MAKELPARAM(p.x, p.y));
            require(near_value(input.mouse_x, static_cast<float>(p.x) * viewport.first / client.right));
            require(near_value(input.mouse_y, static_cast<float>(p.y) * viewport.second / client.bottom));
        }
        POINT p{800,400};
        require(ClientToScreen(hwnd, &p) != FALSE);
        update_mouse_position_from_screen(input, MAKELPARAM(p.x,p.y));
        require(near_value(input.mouse_x, 800.0f * viewport.first / client.right));
        require(near_value(input.mouse_y, 400.0f * viewport.second / client.bottom));
    }
    for (const auto message : {WM_KILLFOCUS, WM_CANCELMODE, WM_CAPTURECHANGED}) {
        input.mouse_down = input.mouse_clicked = input.mouse_released = true;
        input.rmb_down = input.rmb_clicked = input.rmb_released = true;
        input.focus_lost = false; input.keys = {1,2}; input.char_count = 5; input.scroll_delta = 3;
        require(cancel_input(input, message));
        require(!input.mouse_down && !input.mouse_clicked && !input.mouse_released);
        require(!input.rmb_down && !input.rmb_clicked && !input.rmb_released);
        require(input.focus_lost && input.keys.empty() && input.char_count == 0 && input.scroll_delta == 0);
    }
    require(!cancel_input(input, WM_NULL));
    get_hwnd() = nullptr;
    update_mouse_position(input, 7.0f, 9.0f);
    require(input.mouse_x == 7.0f && input.mouse_y == 9.0f);
    require(DestroyWindow(hwnd) != FALSE);
    std::cout << "generated_ui_helper_checks=" << checks << "\n"
              << "win32_client_screen=REAL\nviewport_and_input_container=MOCKS\n"
              << "full_xui_gpu_and_game=NOT_TESTED\n";
}
'''
(testdir / 'ui_map_native_test.cpp').write_text(source, encoding='utf-8')
vsroot = os.environ.get('VSROOT')
if not vsroot:
    raise SystemExit('[ui-map] VSROOT absent: compiled regression must not be silently skipped')
devcmd = Path(vsroot) / 'Common7/Tools/VsDevCmd.bat'
batch = testdir / 'run_test.cmd'
batch.write_text(
    '@echo off\ncall "' + str(devcmd) + '" -arch=x64 -host_arch=x64\n'
    'if errorlevel 1 exit /b %errorlevel%\n'
    'cd /d "' + str(testdir) + '"\n'
    'cl /nologo /EHsc /std:c++20 /W4 ui_map_native_test.cpp /Fe:ui_map_native_test.exe user32.lib\n'
    'if errorlevel 1 exit /b %errorlevel%\n'
    'ui_map_native_test.exe\nexit /b %errorlevel%\n', encoding='utf-8')
result = subprocess.run(['cmd.exe', '/d', '/c', str(batch)], stdout=subprocess.PIPE,
                        stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
(release / 'UI_MAP_NATIVE_REGRESSION.log').write_text(result.stdout, encoding='utf-8')
print(result.stdout)
if result.returncode:
    raise SystemExit('[ui-map] generated-helper C++ regression failed')
(root / 'MCB_UI_INPUT_MAP_APPLIED.txt').write_text(
    'client_to_viewport=ENABLED\nmouse_buttons_refresh_position=ENABLED\n'
    'wheel_screen_to_client=ENABLED\nfocus_loss_cleanup=ENABLED\n'
    'larger_hit_targets=button,checkbox,slider,text_input,color_swatch\n'
    'legacy_mapping_replaced=YES\nduplicate_focus_case=NO\n'
    'cancellation_does_not_synthesize_release=YES\n'
    'in_game_1440x1080=NOT_TESTED\n', encoding='utf-8')
print('[ui-map] mapping consolidated; generated Win32 helper regression passed')
