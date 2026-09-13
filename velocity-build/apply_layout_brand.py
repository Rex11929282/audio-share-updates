from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "velocity-cs2"
menu = v / "project" / "core" / "rendering" / "impl" / "menu" / "menu.core.cpp"


def die(msg: str):
    raise SystemExit("[layout-brand] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        die(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


t = menu.read_text(encoding="utf-8")

# ---------------------------------------------------------------------------
# MCB vector mark. This avoids shipping any logo asset and scales cleanly from
# the tiny sidebar mark to the intro screen.
# ---------------------------------------------------------------------------
if "draw_mcb_mark" not in t:
    anchor = "namespace rendering {\n\n\tnamespace detail {\n"
    helper = r'''namespace rendering {

	namespace detail {

		static void draw_mcb_mark( xdraw::draw_list& dl, float x, float y, float w, float h, xdraw::color col )
		{
			// Simple vector M C B monogram; resolution independent.
			const auto gap = std::max( 1.0f, w * 0.035f );
			const auto letter_w = std::max( 3.0f, ( w - gap * 2.0f ) / 3.0f );
			const auto top = y + h * 0.18f;
			const auto bottom = y + h * 0.82f;
			const auto mid = ( top + bottom ) * 0.5f;
			const auto thickness = std::clamp( h * 0.075f, 1.25f, 4.0f );
			auto line = [ & ]( float x0, float y0, float x1, float y1 )
			{
				dl.line( x0, y0, x1, y1, col, thickness, true );
			};

			// M
			const auto mx = x;
			line( mx, bottom, mx, top );
			line( mx, top, mx + letter_w * 0.5f, mid );
			line( mx + letter_w * 0.5f, mid, mx + letter_w, top );
			line( mx + letter_w, top, mx + letter_w, bottom );

			// C
			const auto cx = x + letter_w + gap;
			line( cx + letter_w, top, cx, top );
			line( cx, top, cx, bottom );
			line( cx, bottom, cx + letter_w, bottom );

			// B
			const auto bx = x + ( letter_w + gap ) * 2.0f;
			line( bx, top, bx, bottom );
			line( bx, top, bx + letter_w * 0.78f, top );
			line( bx, mid, bx + letter_w * 0.78f, mid );
			line( bx, bottom, bx + letter_w * 0.78f, bottom );
			line( bx + letter_w * 0.78f, top, bx + letter_w, top + ( mid - top ) * 0.35f );
			line( bx + letter_w, top + ( mid - top ) * 0.35f, bx + letter_w, mid - ( mid - top ) * 0.35f );
			line( bx + letter_w, mid - ( mid - top ) * 0.35f, bx + letter_w * 0.78f, mid );
			line( bx + letter_w * 0.78f, mid, bx + letter_w, mid + ( bottom - mid ) * 0.35f );
			line( bx + letter_w, mid + ( bottom - mid ) * 0.35f, bx + letter_w, bottom - ( bottom - mid ) * 0.35f );
			line( bx + letter_w, bottom - ( bottom - mid ) * 0.35f, bx + letter_w * 0.78f, bottom );
		}
'''
    t = replace_once(t, anchor, helper, "MCB helper")

# ---------------------------------------------------------------------------
# Remove the old splash artwork. The draw_intro fallback below becomes MCB.
# ---------------------------------------------------------------------------
old_intro_load = '''\t\tconstexpr float k_intro_logo_view_w{ 4421.68f };
\t\tconstexpr float k_intro_logo_px_w{ 240.0f };
\t\tthis->m_textures.intro_splash.resource = xdraw::load_svg(
\t\t\tsvgs::intro_splash_logo,
\t\t\tk_intro_logo_px_w / k_intro_logo_view_w,
\t\t\t&this->m_textures.intro_splash.width,
\t\t\t&this->m_textures.intro_splash.height
\t\t);'''
new_intro_load = '''\t\t// MCB build: do not load the upstream splash logo.
\t\tthis->m_textures.intro_splash.resource.Reset( );
\t\tthis->m_textures.intro_splash.width = 0;
\t\tthis->m_textures.intro_splash.height = 0;'''
if "MCB build: do not load the upstream splash logo" not in t:
    t = replace_once(t, old_intro_load, new_intro_load, "intro logo removal")

# Draw an MCB monogram when the upstream intro artwork is absent.
intro_anchor = '''\t\tconst auto emergence = smoothstep( 0.12f, 0.65f, this->m_intro_elapsed );'''
intro_insert = '''\t\tif ( anchor_w <= 4.0f )
\t\t{
\t\t\tconst auto brand_w = std::clamp( sw * 0.22f, 180.0f, 280.0f );
\t\t\tconst auto brand_h = std::clamp( sh * 0.075f, 58.0f, 84.0f );
\t\t\tanchor_x = std::floor( ( sw - brand_w ) * 0.5f );
\t\t\tanchor_y = std::floor( ( sh - brand_h ) * 0.5f );
\t\t\tanchor_w = brand_w;
\t\t\tanchor_bottom = anchor_y + brand_h;
\t\t\tconst auto brand_alpha = static_cast< std::uint8_t >( 255.0f * reveal );
\t\t\tdetail::draw_mcb_mark(
\t\t\t\tdl,
\t\t\t\tanchor_x + 12.0f,
\t\t\t\tanchor_y,
\t\t\t\tbrand_w - 24.0f,
\t\t\t\tbrand_h,
\t\t\t\ttokens::col_accent.alpha( brand_alpha ) );
\t\t}

\t\tconst auto emergence = smoothstep( 0.12f, 0.65f, this->m_intro_elapsed );'''
if "brand_w = std::clamp( sw * 0.22f" not in t:
    t = replace_once(t, intro_anchor, intro_insert, "intro MCB mark")

# ---------------------------------------------------------------------------
# Full-view responsive layout. On 1440x1080 the target is 1220x940 centered,
# while lower resolutions shrink automatically to stay inside the viewport.
# ---------------------------------------------------------------------------
reveal_anchor = '''\t\t\tconst auto menu_reveal = xui::ease::out_cubic( this->m_open_anim );

\t\t\tif ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, false, 200.0f, 200.0f, menu_reveal ) )'''
reveal_new = '''\t\t\tconst auto menu_reveal = xui::ease::out_cubic( this->m_open_anim );

\t\t\t// Responsive full-view menu. Always keep the entire GUI inside the
\t\t\t// actual D3D viewport, including stretched 4:3 resolutions.
\t\t\tconst auto [ viewport_w_i, viewport_h_i ] = xdraw::viewport_size( );
\t\t\tif ( viewport_w_i > 0 && viewport_h_i > 0 )
\t\t\t{
\t\t\t\tconst auto viewport_w = static_cast< float >( viewport_w_i );
\t\t\t\tconst auto viewport_h = static_cast< float >( viewport_h_i );
\t\t\t\tconstexpr float safe_margin{ 24.0f };
\t\t\t\tconstexpr float preferred_w{ 1220.0f };
\t\t\t\tconstexpr float preferred_h{ 940.0f };
\t\t\t\tconst auto available_w = std::max( 1.0f, viewport_w - safe_margin * 2.0f );
\t\t\t\tconst auto available_h = std::max( 1.0f, viewport_h - safe_margin * 2.0f );
\t\t\t\tthis->m_w = std::min( preferred_w, available_w );
\t\t\t\tthis->m_h = std::min( preferred_h, available_h );
\t\t\t\tthis->m_x = std::floor( std::max( 0.0f, ( viewport_w - this->m_w ) * 0.5f ) );
\t\t\t\tthis->m_y = std::floor( std::max( 0.0f, ( viewport_h - this->m_h ) * 0.5f ) );
\t\t\t}

\t\t\tif ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, false, 200.0f, 200.0f, menu_reveal ) )'''
if "constexpr float preferred_w{ 1220.0f }" not in t:
    t = replace_once(t, reveal_anchor, reveal_new, "responsive menu")

# ---------------------------------------------------------------------------
# Sidebar logo becomes MCB. The direct ASCII label is also retained in the
# binary so the brand can be trivially verified after linking.
# ---------------------------------------------------------------------------
old_sidebar_logo = '''\t\t{
\t\t\tconst auto pill_x = sb_x + 4.0f;
\t\t\tconst auto pill_y = this->m_y + tokens::gap + 4.0f;
\t\t\tconst auto pill_w = tokens::sidebar_w - 8.0f;
\t\t\tconst auto pill_h = logo_h - 8.0f;

\t\t\tconst auto lw = static_cast< float >( this->m_textures.logo.width );
\t\t\tconst auto lh = static_cast< float >( this->m_textures.logo.height );
\t\t\tconst auto lx = std::floor( pill_x + ( pill_w - lw ) * 0.5f );
\t\t\tconst auto ly = std::floor( pill_y + ( pill_h - lh ) * 0.5f );

\t\t\tdl.image( lx, ly, lw, lh, this->m_textures.logo.resource.Get( ) );
\t\t}'''
new_sidebar_logo = '''\t\t{
\t\t\tconst auto pill_x = sb_x + 4.0f;
\t\t\tconst auto pill_y = this->m_y + tokens::gap + 4.0f;
\t\t\tconst auto pill_w = tokens::sidebar_w - 8.0f;
\t\t\tconst auto pill_h = logo_h - 8.0f;
\t\t\tstatic constexpr std::string_view brand_name{ "MCB" };
\t\t\t( void )brand_name;
\t\t\tdetail::draw_mcb_mark(
\t\t\t\tdl,
\t\t\t\tpill_x + 2.5f,
\t\t\t\tpill_y + 1.5f,
\t\t\t\tpill_w - 5.0f,
\t\t\t\tpill_h - 3.0f,
\t\t\t\ttokens::col_dark );
\t\t}'''
if 'brand_name{ "MCB" }' not in t:
    t = replace_once(t, old_sidebar_logo, new_sidebar_logo, "sidebar MCB logo")

menu.write_text(t, encoding="utf-8")
print("[layout-brand] responsive GUI + MCB applied")
