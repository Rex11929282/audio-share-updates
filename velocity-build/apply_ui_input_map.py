"""Apply safe responsive input-coordinate and hit-target changes.

This patch is intentionally limited to XUI input/interaction. It does not
change aim/gameplay logic and it does not reuse the old crashing mouse patch.
"""
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
    raise SystemExit("[ui-map] target project not found")

overlay = Path(__file__).resolve().parent / "overlay"
header = overlay / "ui_coordinate_map.hpp"
if not header.exists():
    raise SystemExit("[ui-map] overlay/ui_coordinate_map.hpp missing")

xui_dir = v / "project" / "external" / "xdraw" / "xui"
xui_cpp = xui_dir / "xui.cpp"
if not xui_cpp.exists():
    raise SystemExit("[ui-map] xui.cpp missing")
shutil.copy2(header, xui_dir / "ui_coordinate_map.hpp")

t = xui_cpp.read_text(encoding="utf-8")

def replace_once(old: str, new: str, label: str):
    global t
    if new in t:
        return
    if old not in t:
        raise SystemExit(f"[ui-map] marker missing: {label}")
    t = t.replace(old, new, 1)

replace_once(
    '#include "xui.hpp"\n',
    '#include "xui.hpp"\n#include "ui_coordinate_map.hpp"\n',
    "coordinate header include")

hwnd_block = '''\tstatic HWND& get_hwnd( )
\t{
\t\tstatic HWND h{ nullptr };
\t\treturn h;
\t}
'''
helper = hwnd_block + r'''
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
			const auto client_w =
				static_cast<float>( client.right - client.left );
			const auto client_h =
				static_cast<float>( client.bottom - client.top );

			const auto mapped = xui::coord::client_to_viewport(
				client_x,
				client_y,
				client_w,
				client_h,
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
		update_mouse_position(
			input,
			static_cast<float>(
				static_cast<short>( LOWORD( lp ) ) ),
			static_cast<float>(
				static_cast<short>( HIWORD( lp ) ) ) );
	}

	static void update_mouse_position_from_screen(
		xui::input_state& input,
		LPARAM lp )
	{
		POINT point
		{
			static_cast<LONG>(
				static_cast<short>( LOWORD( lp ) ) ),
			static_cast<LONG>(
				static_cast<short>( HIWORD( lp ) ) )
		};

		const auto hwnd = get_hwnd( );
		if ( hwnd && ScreenToClient( hwnd, &point ) )
		{
			update_mouse_position(
				input,
				static_cast<float>( point.x ),
				static_cast<float>( point.y ) );
		}
	}
'''
replace_once(hwnd_block, helper, "coordinate mapping helper")

replace_once(
'''\t\tcase WM_MOUSEMOVE:
\t\t\ts.mouse_x = static_cast< float >( static_cast< short >( LOWORD( lp ) ) );
\t\t\ts.mouse_y = static_cast< float >( static_cast< short >( HIWORD( lp ) ) );
\t\t\treturn true;
''',
'''\t\tcase WM_MOUSEMOVE:
\t\t\tupdate_mouse_position( s, lp );
\t\t\treturn true;
''',
"WM_MOUSEMOVE mapping")

for label in (
    "WM_LBUTTONDOWN",
    "WM_LBUTTONUP",
    "WM_RBUTTONDOWN",
    "WM_RBUTTONUP",
    "WM_MBUTTONDOWN",
    "WM_MBUTTONUP",
    "WM_XBUTTONDOWN",
    "WM_XBUTTONUP",
):
    marker = f"\t\tcase {label}:\n"
    if label.startswith("WM_XBUTTON"):
        marker += "\t\t{\n"
        replacement = marker + "\t\t\tupdate_mouse_position( s, lp );\n"
    else:
        replacement = marker + "\t\t\tupdate_mouse_position( s, lp );\n"
    replace_once(marker, replacement, f"{label} coordinate refresh")

wheel_old = '''\t\tcase WM_MOUSEWHEEL:
\t\t{
\t\t\tconst auto delta = static_cast< float >( GET_WHEEL_DELTA_WPARAM( wp ) ) / static_cast< float >( WHEEL_DELTA );
'''
wheel_new = '''\t\tcase WM_MOUSEWHEEL:
\t\t{
\t\t\tupdate_mouse_position_from_screen( s, lp );
\t\t\tconst auto delta = static_cast< float >( GET_WHEEL_DELTA_WPARAM( wp ) ) / static_cast< float >( WHEEL_DELTA );
'''
replace_once(wheel_old, wheel_new, "wheel coordinate refresh")

focus_anchor = '''\t\tcase WM_CHAR:
'''
focus_block = '''\t\tcase WM_KILLFOCUS:
\t\t\ts.mouse_down = false;
\t\t\ts.mouse_clicked = false;
\t\t\ts.mouse_released = true;
\t\t\ts.rmb_down = false;
\t\t\ts.rmb_clicked = false;
\t\t\ts.rmb_released = true;
\t\t\ts.keys.clear( );
\t\t\treturn true;

\t\tcase WM_CHAR:
'''
replace_once(focus_anchor, focus_block, "focus loss cleanup")

replace_once(
'''\t\tconst auto abs = layout::item( w, h );
\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto hovered = can_interact && input.in_rect( abs );
''',
'''\t\tconst auto abs = layout::item( w, h );
\t\tconst auto can_interact = !c.overlay_blocking( );
\t\tconst auto interaction = abs.expand( 2.0f );
\t\tconst auto hovered = can_interact && input.in_rect( interaction );
''',
"button hit target")

replace_once(
'''\t\tconst auto full_w = !display.empty( ) ? ( abs.w + st.item_spacing_x + lw ) : abs.w;
\t\tconst auto extended = rect{ abs.x, abs.y, full_w, abs.h };

\t\tconst auto can_interact = !c.overlay_blocking( );
''',
'''\t\tconst auto full_w = !display.empty( ) ? ( abs.w + st.item_spacing_x + lw ) : abs.w;
\t\tconst auto extended = rect{ abs.x, abs.y, full_w, abs.h };
\t\tconst auto interaction = extended.expand( 3.0f );

\t\tconst auto can_interact = !c.overlay_blocking( );
''',
"checkbox hit target geometry")
replace_once(
'\t\tconst auto hovered = can_interact && input.in_rect( extended ) && !badge_listening;\n',
'\t\tconst auto hovered = can_interact && input.in_rect( interaction ) && !badge_listening;\n',
"checkbox hit target use")

replace_once(
'''\t\t\tconst auto track_rect = rect{ abs.x, track_y, slider_width, track_height };
\t\t\tconst auto slider_hit_rect = rect{ abs.x, abs.y + text_height + spacing, slider_width, slider_area_h };

\t\t\tconst auto can_interact = !c.overlay_blocking( );
''',
'''\t\t\tconst auto track_rect = rect{ abs.x, track_y, slider_width, track_height };
\t\t\tconst auto slider_hit_rect = rect{ abs.x, abs.y + text_height + spacing, slider_width, slider_area_h };
\t\t\tconst auto slider_interaction = slider_hit_rect.expand( 4.0f );

\t\t\tconst auto can_interact = !c.overlay_blocking( );
''',
"slider expanded hit target")
t = t.replace(
    "input.in_rect( slider_hit_rect )",
    "input.in_rect( slider_interaction )")

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
''',
"text input hit target")

# Keep visual geometry compact, but make color swatches easier to hit.
replace_once(
'''\t\tconst auto swatch_rect = rect{ abs.x + x_offset, swatch_y, sw, sh };

\t\tconst auto can_interact = !c.overlay_blocking( ) || is_open;
\t\tconst auto hovered = can_interact && input.in_rect( swatch_rect );
''',
'''\t\tconst auto swatch_rect = rect{ abs.x + x_offset, swatch_y, sw, sh };
\t\tconst auto swatch_interaction = swatch_rect.expand( 3.0f );

\t\tconst auto can_interact = !c.overlay_blocking( ) || is_open;
\t\tconst auto hovered = can_interact && input.in_rect( swatch_interaction );
''',
"color swatch hit target")

xui_cpp.write_text(t, encoding="utf-8")

report = root / "MCB_UI_INPUT_MAP_APPLIED.txt"
report.write_text(
    "client_to_viewport=ENABLED\\n"
    "mouse_buttons_refresh_position=ENABLED\\n"
    "wheel_screen_to_client=ENABLED\\n"
    "focus_loss_cleanup=ENABLED\\n"
    "larger_hit_targets=button,checkbox,slider,text_input,color_swatch\\n"
    "old_crashing_mouse_patch=NOT_USED\\n"
    "in_game_1440x1080=NOT_TESTED\\n",
    encoding="utf-8")

print("[ui-map] responsive input mapping and hit targets applied")
