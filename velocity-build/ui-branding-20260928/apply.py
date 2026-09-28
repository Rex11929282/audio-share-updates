from pathlib import Path
import hashlib
import json
import re
import sys

ROOT = Path(sys.argv[1]).resolve()

TIGER_SVG = r'''<svg width="24" height="24" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg"><path fill="#111111" d="M4.4 3.3 8 5.2A9.1 9.1 0 0 1 12 4.3c1.45 0 2.8.32 4 .9l3.6-1.9-.4 4.2A8.8 8.8 0 0 1 21 12.9C21 18 17 21.4 12 21.4S3 18 3 12.9a8.8 8.8 0 0 1 1.8-5.4l-.4-4.2Zm3.2 6.1 2.4 1.1-.8 1.4-2.5-1 .9-1.5Zm8.8 0 .9 1.5-2.5 1-.8-1.4 2.4-1.1ZM8.1 13c.8 0 1.45.46 1.45 1.03 0 .56-.65 1.02-1.45 1.02S6.65 14.59 6.65 14c0-.57.65-1.03 1.45-1.03Zm7.8 0c.8 0 1.45.46 1.45 1.03 0 .56-.65 1.02-1.45 1.02s-1.45-.46-1.45-1.02c0-.57.65-1.03 1.45-1.03ZM12 13.6l1.2 1.1-1.2 1.15-1.2-1.15 1.2-1.1Zm-2.4 3.1 2.4 1.15 2.4-1.15-.6 1.65L12 19.2l-1.8-.85-.6-1.65Z"/><path fill="#111111" d="M9.2 6.1 12 7.5l2.8-1.4-.7 2.1L12 9.1 9.9 8.2l-.7-2.1ZM6.2 7.1l2.5.6-.5 1.2-2.7-.3.7-1.5Zm11.6 0 .7 1.5-2.7.3-.5-1.2 2.5-.6Z"/></svg>'''


def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")


def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text, old, new, label):
    if old not in text:
        raise RuntimeError(f"anchor missing: {label}")
    return text.replace(old, new, 1)


def replace_function(text, signature, replacement):
    pos = text.find(signature)
    if pos < 0:
        raise RuntimeError(f"function missing: {signature}")
    brace = text.find("{", pos)
    if brace < 0:
        raise RuntimeError(f"function brace missing: {signature}")

    depth = 0
    i = brace
    in_str = None
    escape = False
    line_comment = False
    block_comment = False
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ""
        if line_comment:
            if c == "\n":
                line_comment = False
            i += 1
            continue
        if block_comment:
            if c == "*" and n == "/":
                block_comment = False
                i += 2
                continue
            i += 1
            continue
        if in_str:
            if escape:
                escape = False
            elif c == "\\":
                escape = True
            elif c == in_str:
                in_str = None
            i += 1
            continue
        if c == "/" and n == "/":
            line_comment = True
            i += 2
            continue
        if c == "/" and n == "*":
            block_comment = True
            i += 2
            continue
        if c in ('"', "'"):
            in_str = c
            i += 1
            continue
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return text[:pos] + replacement.rstrip() + text[i + 1:]
        i += 1
    raise RuntimeError(f"unterminated function: {signature}")


settings_rel = "project/core/settings.hpp"
settings = read(settings_rel)
profile_start = settings.find("\t\tstruct profile_ui")
if profile_start < 0:
    raise RuntimeError("profile_ui struct missing")
profile_end_marker = "\t\t} m_profile_ui{};"
profile_end = settings.find(profile_end_marker, profile_start)
if profile_end < 0:
    raise RuntimeError("profile_ui end missing")
profile_end += len(profile_end_marker)
profile_struct = '''\t\tstruct profile_ui
\t\t{
\t\t\tenum class notice_style : int { compact, pill, glass, damage, game };
\t\t\tconfig::str display_name{ "", "profile", "display name" };
\t\t\tconfig::val<int> menu_opacity{ 72, "profile", "menu opacity" };
\t\t\tconfig::val<int> blur_strength{ 14, "profile", "blur strength" };
\t\t\tconfig::val<int> rounding{ 10, "profile", "rounding" };
\t\t\tconfig::val<int> border_alpha{ 20, "profile", "border alpha" };
\t\t\tconfig::val<int> animation_speed{ 72, "profile", "animation speed" };
\t\t\tconfig::enm<notice_style> hit_notice_style{ notice_style::glass, "profile", "hit notice style" };
\t\t\tconfig::val<int> accent_preset{ 0, "profile", "accent preset" };
\t\t\txui::setting decor_edge_glow{ true, {}, "edge glow", "profile" };
\t\t\txui::setting decor_corner_marks{ true, {}, "corner marks", "profile" };
\t\t\txui::setting decor_grid{ false, {}, "background grid", "profile" };
\t\t\tconfig::val<int> decor_intensity{ 38, "profile", "decor intensity" };
\t\t} m_profile_ui{};'''
settings = settings[:profile_start] + profile_struct + settings[profile_end:]
write(settings_rel, settings)

exact_rel = "project/core/rendering/impl/menu/menu.exact.cpp"
exact = read(exact_rel)
profile_fn = '''    void menu::draw_profile_center( float x, float y, float w, float h )
    {
        auto& p=settings::g_misc.m_profile_ui;auto& wm=settings::g_misc.m_watermark;
        this->m_profile_tab=std::clamp(this->m_profile_tab,0,1);
        xui::layout::set_cursor(x-this->m_x,y-this->m_y);
        static constexpr const char* tabs[]{"個人","外觀"};
        for(int i=0;i<2;++i){if(i)xui::layout::same_line();if(xui::button(tabs[i],(w-8.0f)/2.0f,30.0f))this->m_profile_tab=i;}
        xui::layout::set_cursor(x-this->m_x,y+42.0f-this->m_y);
        if(!xui::begin_child("##profile_center",w,std::max(80.0f,h-42.0f),true))return;
        if(this->m_profile_tab==0)
        {
            xui::text("個人資料",tokens::col_text);
            xui::text_input("顯示名稱##profile_name",p.display_name.value,32,"未命名");
            xui::layout::separator();
            xui::text("水印",tokens::col_text);
            xui::checkbox("顯示水印##profile_watermark",wm.enabled);
        }
        else
        {
            xui::text("外觀設定",tokens::col_text);
            static constexpr const char* accents[]{"冰藍","淺藍","紫色","綠色","金色","紅色"};
            xui::combo("強調色##profile_accent",p.accent_preset.value,accents,6);
            xui::slider_int("背景透明度",p.menu_opacity,15,100,"%d%%");
            xui::slider_int("玻璃模糊",p.blur_strength,0,32,"%d");
            xui::slider_int("圓角",p.rounding,2,24,"%d");
            xui::slider_int("邊框強度",p.border_alpha,0,120,"%d");
            xui::slider_int("動畫速度",p.animation_speed,0,100,"%d%%");
            xui::layout::separator();
            xui::text("UI 裝飾",tokens::col_text);
            xui::checkbox("邊緣微光##profile_decor_edge",p.decor_edge_glow);
            xui::checkbox("角落線條##profile_decor_corner",p.decor_corner_marks);
            xui::checkbox("背景網格##profile_decor_grid",p.decor_grid);
            xui::slider_int("裝飾強度##profile_decor_intensity",p.decor_intensity,0,100,"%d%%");
        }
        xui::end_child();
    }'''
exact = replace_function(exact, "    void menu::draw_profile_center( float x, float y, float w, float h )", profile_fn)
write(exact_rel, exact)

core_rel = "project/core/rendering/impl/menu/menu.core.cpp"
core = read(core_rel)
old_brand = '''\t\tif ( this->m_sidebar_compact ) dl.text( sx + 14.0f, sy + 24.0f, "MCB", xdraw::color{ 244, 246, 249, 250 } );
\t\telse dl.text( sx + 18.0f, sy + 24.0f, "MCB", xdraw::color{ 244, 246, 249, 250 } );'''
new_brand = f'''\t\tstatic auto tiger_menu_w = 0, tiger_menu_h = 0;
\t\tstatic const auto tiger_menu = xdraw::load_svg( R"({TIGER_SVG})", 1.0f, &tiger_menu_w, &tiger_menu_h );
\t\tconst auto tiger_menu_size = 24.0f;
\t\tif ( tiger_menu ) dl.image( sx + 14.0f, sy + 14.0f, tiger_menu_size, tiger_menu_size, tiger_menu.Get( ), tokens::col_accent );
\t\tif ( !this->m_sidebar_compact ) dl.text( sx + 46.0f, sy + 24.0f, "MCB", xdraw::color{{ 244, 246, 249, 250 }} );'''
core = replace_once(core, old_brand, new_brand, "sidebar brand")
core = re.sub(r'\n\t\t\tconst auto ring = settings::g_misc\.m_profile_ui\.avatar_ring\.value;\n\t\t\tdl\.circle\([^\n]+\);', '', core, count=1)

shell_anchor_re = re.compile(r'(\t\t\tshell_dl\.rect\( this->m_x, this->m_y, this->m_w, this->m_h, xdraw::color\{ 255, 255, 255, static_cast<std::uint8_t>\( std::clamp\( settings::g_misc\.m_profile_ui\.border_alpha\.value \* 2, 0, 180 \) \) \}, xdraw::corner_radius\{ static_cast<float>\( settings::g_misc\.m_profile_ui\.rounding\.value \) \}, 1\.0f \);)')
m = shell_anchor_re.search(core)
if not m:
    raise RuntimeError("menu shell border anchor missing")
decor = r'''

			const auto& decor = settings::g_misc.m_profile_ui;
			const auto decor_level = std::clamp( decor.decor_intensity.value, 0, 100 );
			if ( decor_level > 0 )
			{
				auto decor_col = tokens::col_accent;
				if ( decor.decor_edge_glow.value )
				{
					decor_col.a = static_cast<std::uint8_t>( std::clamp( 14 + decor_level, 14, 114 ) );
					shell_dl.rect( this->m_x + 1.0f, this->m_y + 1.0f, this->m_w - 2.0f, this->m_h - 2.0f, decor_col, xdraw::corner_radius{ static_cast<float>( settings::g_misc.m_profile_ui.rounding.value ) }, 1.0f );
				}
				if ( decor.decor_corner_marks.value )
				{
					decor_col.a = static_cast<std::uint8_t>( std::clamp( 20 + decor_level, 20, 120 ) );
					const auto l = 18.0f;
					const auto x0 = this->m_x + 8.0f, x1 = this->m_x + this->m_w - 8.0f;
					const auto y0 = this->m_y + 8.0f, y1 = this->m_y + this->m_h - 8.0f;
					shell_dl.line( x0, y0, x0 + l, y0, decor_col, 1.0f ); shell_dl.line( x0, y0, x0, y0 + l, decor_col, 1.0f );
					shell_dl.line( x1 - l, y0, x1, y0, decor_col, 1.0f ); shell_dl.line( x1, y0, x1, y0 + l, decor_col, 1.0f );
					shell_dl.line( x0, y1, x0 + l, y1, decor_col, 1.0f ); shell_dl.line( x0, y1 - l, x0, y1, decor_col, 1.0f );
					shell_dl.line( x1 - l, y1, x1, y1, decor_col, 1.0f ); shell_dl.line( x1, y1 - l, x1, y1, decor_col, 1.0f );
				}
				if ( decor.decor_grid.value )
				{
					decor_col.a = static_cast<std::uint8_t>( std::clamp( 3 + decor_level / 5, 3, 23 ) );
					const auto gx0 = this->m_x + ( this->m_sidebar_compact ? 58.0f : 203.0f ) + 16.0f;
					const auto gx1 = this->m_x + this->m_w - 12.0f;
					const auto gy0 = this->m_y + 66.0f + 12.0f;
					const auto gy1 = this->m_y + this->m_h - 12.0f;
					for ( auto gx = gx0; gx < gx1; gx += 32.0f ) shell_dl.line( gx, gy0, gx, gy1, decor_col, 1.0f );
					for ( auto gy = gy0; gy < gy1; gy += 32.0f ) shell_dl.line( gx0, gy, gx1, gy, decor_col, 1.0f );
				}
			}'''
core = core[:m.end()] + decor + core[m.end():]
write(core_rel, core)

widgets_rel = "project/core/rendering/impl/widgets.cpp"
widgets = read(widgets_rel)
if "fixed tiger icon" not in widgets:
    logo_start = widgets.find("\t\t// ── logo")
    if logo_start < 0:
        raise RuntimeError("watermark logo section missing")
    logo_end = widgets.find("\t\tconst auto inner_h", logo_start)
    if logo_end < 0:
        raise RuntimeError("watermark logo end missing")
    logo_block = f'''\t\t// ── fixed tiger icon ───────────────────────────────────────────────
\t\tconst auto logo_scale = logo_icon_size / 24.0f;
\t\tstatic auto logo_w = 0, logo_h = 0;
\t\tstatic const auto logo = xdraw::load_svg( R"({TIGER_SVG})", logo_scale, &logo_w, &logo_h );

'''
    widgets = widgets[:logo_start] + logo_block + widgets[logo_end:]

widgets = re.sub(r'\n\t\tconst char\* brand_symbol=.*?;\n\t\tconst auto \[name_tw, name_th\] = xdraw::measure_text\( brand_symbol \);', '', widgets, count=1)

old_logo_width = "\t\tconst auto logo_pill_w = logo_icon_pad + logo_draw_w + logo_icon_pad + name_tw + text_pad_x;"
new_logo_width = "\t\tconst auto logo_pill_w = logo_icon_pad + logo_draw_w + logo_icon_pad;"
if old_logo_width in widgets:
    widgets = widgets.replace(old_logo_width, new_logo_width, 1)
elif new_logo_width not in widgets:
    raise RuntimeError("watermark pill width anchor missing")

widgets = re.sub(
    r'\n\t\tdraw_list\.text\( cx \+ logo_icon_pad \+ logo_draw_w \+ logo_icon_pad,\n\t\t\ty \+ \( h - name_th \) \* 0\.5f \+ text_nudge, brand_symbol, s\.checkbox_mark_icon \);',
    '', widgets, count=1
)
write(widgets_rel, widgets)

checks = {
    "profile_two_tabs": 'static constexpr const char* tabs[]{"個人","外觀"};' in exact,
    "real_ui_decor_controls": all(x in exact for x in ("邊緣微光", "角落線條", "背景網格", "裝飾強度")),
    "no_old_accessory_ui": not any(x in exact for x in ("頭像光暈", "側欄裝飾", "細微粒子", "浮水印符號")),
    "no_text_tiger_symbol_logic": "watermark_symbol" not in settings and "brand_symbol" not in widgets,
    "fixed_tiger_watermark": "fixed tiger icon" in widgets and "xdraw::load_svg" in widgets and "brand_symbol" not in widgets,
    "fixed_tiger_menu": TIGER_SVG in core and "tiger_menu" in core,
    "decor_rendering_wired": all(x in core for x in ("decor_edge_glow", "decor_corner_marks", "decor_grid", "decor_intensity")),
}
failed = [k for k, v in checks.items() if not v]
if failed:
    raise RuntimeError("branding/ui decoration postcondition failed: " + ", ".join(failed))

report = {
    "name": "MCB UI branding + appearance 2026-09-28",
    "scope": "UI only",
    "checks": checks,
    "files": {},
}
for rel in (settings_rel, exact_rel, core_rel, widgets_rel):
    report["files"][rel] = hashlib.sha256((ROOT / rel).read_bytes()).hexdigest()
(ROOT / "MCB_UI_BRANDING_20260928.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
