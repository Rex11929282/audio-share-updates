from pathlib import Path
import base64
import json
import re
import sys
import zlib

root = Path(sys.argv[1]).resolve()
repo_patch_dir = Path(__file__).resolve().parent
v = root / "cs2" / "MCB-CS2"


def fail(msg: str):
    raise SystemExit("[exact-panel-v5] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        fail(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


# Decode the exact page model extracted from the user's uploaded HTML/CSS/JS.
packed = (repo_patch_dir / "panel_model.b85").read_text(encoding="utf-8").strip()
model = json.loads(zlib.decompress(base64.b85decode(packed.encode("ascii"))).decode("utf-8"))

# Settings is custom in the HTML. Convert its real controls into the same generic
# model so the native renderer can use the identical names/defaults/options.
settings_groups = [
    {"title":"Interface","items":[
        ["range","DPI Scale",100,[75,150,"%"]],
        ["range","Menu Opacity",96,[40,100,"%"]],
        ["range","Font Scale",100,[85,125,"%"]],
        ["select","Sidebar Mode","Expanded",["Expanded","Compact","Auto"]],
        ["toggle","Use Windows DPI Scale",True,"Layout only"],
        ["toggle","Menu Animations",True,"UI only"],
        ["range","Animation Speed",72,[0,100,"%"]],
        ["toggle","Menu Sounds",False,"UI only"],
        ["select","Language","English",["English","简体中文","繁體中文","Русский","Custom"]],
        ["toggle","Custom Translation",False,"Local only"],
        ["select","Notification Position","Bottom Right",["Top Left","Top Right","Bottom Left","Bottom Right"]],
        ["key","Menu Key","INSERT"]
    ]},
    {"title":"Localization","items":[
        ["select","Translation Source","Built-in",["Built-in","Custom JSON"]],
        ["select","Custom Language Name","English Custom",["English Custom","简中 Custom","繁中 Custom","Русский Custom"]],
        ["toggle","Fallback To English",True,"Local only"],
        ["toggle","Reload Menu Text",False,"UI only"]
    ]},
    {"title":"Screen Widgets","items":[
        ["toggle","Watermark",True,"UI preview only"],
        ["select","Watermark Position","Top Right",["Top Left","Top Right","Bottom Left","Bottom Right"]],
        ["tags","Watermark Elements",["Logo","Framerate"],["Logo","KD Ratio","Speed","Framerate","1% Low","GPU Load","CPU Load","CPU Freq","Memory Load","Latency","Var","Loss","Connectivity Issues"]],
        ["toggle","Hotkey List",True,"UI preview only"],
        ["toggle","Spectator List",True,"UI preview only"],
        ["toggle","Event Logs",True,"UI preview only"],
        ["toggle","Indicators",True,"UI preview only"]
    ]}
]
model["hiddenPages"][0]["groups"] = settings_groups
model_json = json.dumps(model, ensure_ascii=False, separators=(",", ":"))

# ---------------------------------------------------------------------------
# Header state: exact 10-page shell and local UI state controls.
# ---------------------------------------------------------------------------
hpp = v / "project/core/rendering/rendering.hpp"
t = hpp.read_text(encoding="utf-8")
if "draw_exact_page" not in t:
    anchor = "        void draw_config( float group_w );\n"
    addition = anchor + "        void draw_exact_page( );\n        void save_exact_panel_state( );\n"
    t = replace_once(t, anchor, addition, "exact page methods")
if "m_exact_page" not in t:
    anchor = "        int m_subtab{};\n"
    addition = anchor + "        int m_exact_page{};\n        bool m_sidebar_compact{};\n        bool m_dim_interface{};\n        int m_exact_config_index{};\n"
    t = replace_once(t, anchor, addition, "exact page state")
hpp.write_text(t, encoding="utf-8")

# Compact mode needs a runtime sidebar width, exactly 203 / 58 from the HTML.
xdraw_hpp = v / "project/external/xdraw/xdraw.hpp"
t = xdraw_hpp.read_text(encoding="utf-8")
t = t.replace("constexpr auto sidebar_w{ 203.0f };", "inline float sidebar_w{ 203.0f };", 1)
xdraw_hpp.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# Generate the native model renderer. Controls are UI/local state only, matching
# the source HTML's own safety boundary. Existing Inventory / Scripts / Configs
# pages stay connected to their current native managers.
# ---------------------------------------------------------------------------
source = r'''#include <pch/pch.hpp>
#include <external/nlohmann/json.hpp>
#include <external/config.hpp>
#include <core/scripting/scripting.hpp>
#include "../../rendering.hpp"

#include <array>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <memory>
#include <unordered_map>

namespace rendering
{
    namespace exact_panel_detail
    {
        using json = nlohmann::json;

        static constexpr std::string_view k_model_json = R"MCBMODEL(__MODEL_JSON__)MCBMODEL";

        struct tag_value
        {
            std::array<bool, 32> selected{};
            int count{};
        };

        struct store
        {
            bool loaded{};
            bool dirty{};
            json disk{};
            std::unordered_map<std::string, std::unique_ptr<xui::setting>> toggles{};
            std::unordered_map<std::string, int> numbers{};
            std::unordered_map<std::string, int> choices{};
            std::unordered_map<std::string, int> keys{};
            std::unordered_map<std::string, xdraw::color> colors{};
            std::unordered_map<std::string, tag_value> tags{};
        };

        store& values( )
        {
            static store s{};
            return s;
        }

        const json& model( )
        {
            static const json m = json::parse( k_model_json.begin( ), k_model_json.end( ) );
            return m;
        }

        std::filesystem::path state_path( )
        {
            wchar_t buffer[ 32768 ]{};
            const auto n = GetEnvironmentVariableW( L"LOCALAPPDATA", buffer, static_cast<DWORD>( std::size( buffer ) ) );
            std::filesystem::path base = n && n < std::size( buffer ) ? std::filesystem::path{ buffer } : std::filesystem::temp_directory_path( );
            return base / L"MCB" / L"panel_state.json";
        }

        void load_state( )
        {
            auto& s = values( );
            if ( s.loaded ) return;
            s.loaded = true;
            std::ifstream file( state_path( ), std::ios::binary );
            if ( !file ) return;
            try { file >> s.disk; }
            catch ( ... ) { s.disk = json::object( ); }
        }

        std::string make_key( std::string_view page, std::string_view group, std::string_view name )
        {
            return std::string( page ) + "::" + std::string( group ) + "::" + std::string( name );
        }

        std::string ascii_lower( std::string_view s )
        {
            std::string out( s );
            for ( auto& c : out ) if ( c >= 'A' && c <= 'Z' ) c = static_cast<char>( c - 'A' + 'a' );
            return out;
        }

        bool search_match( std::string_view query, std::string_view group, std::string_view name )
        {
            if ( query.empty( ) ) return true;
            const auto q = ascii_lower( query );
            const auto g = ascii_lower( group );
            const auto n = ascii_lower( name );
            return g.find( q ) != std::string::npos || n.find( q ) != std::string::npos;
        }

        xdraw::color parse_color( std::string_view hex )
        {
            if ( hex.size( ) == 7 && hex[ 0 ] == '#' )
            {
                unsigned value{};
                const auto result = std::from_chars( hex.data( ) + 1, hex.data( ) + 7, value, 16 );
                if ( result.ec == std::errc{} )
                    return xdraw::color{ static_cast<std::uint8_t>( value >> 16 ), static_cast<std::uint8_t>( value >> 8 ), static_cast<std::uint8_t>( value ), 255 };
            }
            return tokens::col_accent;
        }

        int key_from_name( std::string_view name )
        {
            if ( name == "M3" ) return VK_MBUTTON;
            if ( name == "M4" ) return VK_XBUTTON1;
            if ( name == "M5" ) return VK_XBUTTON2;
            if ( name == "ALT" ) return VK_MENU;
            if ( name == "CAPS" ) return VK_CAPITAL;
            if ( name == "SPACE" ) return VK_SPACE;
            if ( name == "INSERT" ) return VK_INSERT;
            if ( name.size( ) == 1 ) return VkKeyScanA( name.front( ) ) & 0xff;
            return 0;
        }

        bool saved_has( const std::string& key )
        {
            auto& s = values( );
            return s.disk.is_object( ) && s.disk.contains( key );
        }

        bool& toggle_value( const std::string& key, bool def, std::string_view name, std::string_view group )
        {
            auto& s = values( );
            auto it = s.toggles.find( key );
            if ( it == s.toggles.end( ) )
            {
                const auto initial = saved_has( key ) ? s.disk[ key ].get<bool>( ) : def;
                auto ptr = std::make_unique<xui::setting>( initial, xui::bind_info{}, std::string( name ), std::string( group ) );
                it = s.toggles.emplace( key, std::move( ptr ) ).first;
            }
            return it->second->value;
        }

        int& number_value( const std::string& key, int def )
        {
            auto& s = values( );
            auto [it, inserted] = s.numbers.try_emplace( key, saved_has( key ) ? s.disk[ key ].get<int>( ) : def );
            return it->second;
        }

        int& choice_value( const std::string& key, int def )
        {
            auto& s = values( );
            auto [it, inserted] = s.choices.try_emplace( key, saved_has( key ) ? s.disk[ key ].get<int>( ) : def );
            return it->second;
        }

        int& key_value( const std::string& key, int def )
        {
            auto& s = values( );
            auto [it, inserted] = s.keys.try_emplace( key, saved_has( key ) ? s.disk[ key ].get<int>( ) : def );
            return it->second;
        }

        xdraw::color& color_value( const std::string& key, xdraw::color def )
        {
            auto& s = values( );
            const auto initial = saved_has( key ) ? xdraw::color{ s.disk[ key ].get<std::uint32_t>( ) } : def;
            auto [it, inserted] = s.colors.try_emplace( key, initial );
            return it->second;
        }

        tag_value& tags_value( const std::string& key, const json& defaults, const json& options )
        {
            auto& s = values( );
            auto it = s.tags.find( key );
            if ( it == s.tags.end( ) )
            {
                tag_value tv{};
                tv.count = std::min<int>( static_cast<int>( options.size( ) ), static_cast<int>( tv.selected.size( ) ) );
                if ( saved_has( key ) && s.disk[ key ].is_array( ) )
                {
                    for ( int i = 0; i < tv.count && i < static_cast<int>( s.disk[ key ].size( ) ); ++i ) tv.selected[ i ] = s.disk[ key ][ i ].get<bool>( );
                }
                else
                {
                    for ( int i = 0; i < tv.count; ++i )
                    {
                        const auto option = options[ i ].get<std::string>( );
                        tv.selected[ i ] = std::find( defaults.begin( ), defaults.end( ), option ) != defaults.end( );
                    }
                }
                it = s.tags.emplace( key, tv ).first;
            }
            return it->second;
        }

        const json* find_page( std::string_view id )
        {
            const auto& m = model( );
            for ( const auto& group : m[ "pageModel" ] )
                for ( const auto& page : group[ "pages" ] )
                    if ( page[ "id" ].get<std::string>( ) == id ) return &page;
            for ( const auto& page : m[ "hiddenPages" ] )
                if ( page[ "id" ].get<std::string>( ) == id ) return &page;
            return nullptr;
        }

        float panel_height( const json& group, std::string_view query )
        {
            int rows{};
            for ( const auto& item : group[ "items" ] )
                if ( search_match( query, group[ "title" ].get<std::string>( ), item[ 1 ].get<std::string>( ) ) ) ++rows;
            return rows == 0 ? 0.0f : 42.0f + rows * 30.0f + 8.0f;
        }

        void mark_dirty( ) { values( ).dirty = true; }

        void draw_group( std::string_view page_id, const json& group, float w, float h, std::string_view query )
        {
            const auto title = group[ "title" ].get<std::string>( );
            const auto child_id = std::string( "##exact_" ) + std::string( page_id ) + "_" + title;
            if ( !xui::begin_child( child_id, w, h, false ) ) return;

            if ( auto* win = xui::layout::current_window( ) )
            {
                auto& dl = xui::draw::current( );
                dl.rect_filled( win->bounds.x, win->bounds.y, win->bounds.w, 34.0f, xdraw::color{ 9, 15, 21, 240 }, xdraw::corner_radius::top( 8.0f ) );
                dl.line( win->bounds.x, win->bounds.y + 34.0f, win->bounds.right( ), win->bounds.y + 34.0f, xdraw::color{ 18, 31, 44, 255 }, 1.0f );
                dl.text( win->bounds.x + 9.0f, win->bounds.y + 11.0f, title, xdraw::color{ 224, 231, 236, 255 } );
                xui::layout::set_cursor( 9.0f, 38.0f );
            }

            for ( const auto& item : group[ "items" ] )
            {
                const auto type = item[ 0 ].get<std::string>( );
                const auto name = item[ 1 ].get<std::string>( );
                if ( !search_match( query, title, name ) ) continue;
                const auto key = make_key( page_id, title, name );
                bool changed{};

                if ( type == "toggle" )
                {
                    auto& s = values( );
                    if ( !s.toggles.contains( key ) )
                    {
                        const auto initial = saved_has( key ) ? s.disk[ key ].get<bool>( ) : item[ 2 ].get<bool>( );
                        s.toggles.emplace( key, std::make_unique<xui::setting>( initial, xui::bind_info{}, name, title ) );
                    }
                    changed = xui::checkbox( name, *s.toggles[ key ] );
                }
                else if ( type == "range" )
                {
                    auto& v = number_value( key, item[ 2 ].get<int>( ) );
                    const auto minv = item[ 3 ][ 0 ].get<int>( );
                    const auto maxv = item[ 3 ][ 1 ].get<int>( );
                    const auto suffix = item[ 3 ][ 2 ].get<std::string>( );
                    const auto fmt = suffix == "%" ? "%d%%" : suffix == "°" ? "%d°" : suffix == "ms" ? "%d ms" : "%d";
                    changed = xui::slider_int( name, v, minv, maxv, fmt );
                }
                else if ( type == "select" || type == "segment" )
                {
                    const auto& options = item[ 3 ];
                    int def{};
                    for ( int i = 0; i < static_cast<int>( options.size( ) ); ++i ) if ( options[ i ].get<std::string>( ) == item[ 2 ].get<std::string>( ) ) { def = i; break; }
                    auto& current = choice_value( key, def );
                    std::vector<std::string> storage;
                    std::vector<const char*> ptrs;
                    storage.reserve( options.size( ) ); ptrs.reserve( options.size( ) );
                    for ( const auto& option : options ) storage.push_back( option.get<std::string>( ) );
                    for ( const auto& option : storage ) ptrs.push_back( option.c_str( ) );
                    changed = xui::combo( name, current, ptrs.data( ), static_cast<int>( ptrs.size( ) ) );
                }
                else if ( type == "key" )
                {
                    auto& v = key_value( key, key_from_name( item[ 2 ].get<std::string>( ) ) );
                    changed = xui::keybind( name, v );
                }
                else if ( type == "color" )
                {
                    auto& v = color_value( key, parse_color( item[ 2 ].get<std::string>( ) ) );
                    changed = xui::color_picker( name, v, 0.0f, false );
                }
                else if ( type == "tags" )
                {
                    const auto& options = item[ 3 ];
                    auto& tv = tags_value( key, item[ 2 ], options );
                    std::vector<std::string> storage;
                    std::vector<const char*> ptrs;
                    storage.reserve( options.size( ) ); ptrs.reserve( options.size( ) );
                    for ( const auto& option : options ) storage.push_back( option.get<std::string>( ) );
                    for ( const auto& option : storage ) ptrs.push_back( option.c_str( ) );
                    changed = xui::multicombo( name, tv.selected.data( ), ptrs.data( ), tv.count );
                }
                if ( changed ) mark_dirty( );
            }
            xui::end_child( );
        }

        void serialize_state( json& out )
        {
            auto& s = values( );
            out = json::object( );
            for ( const auto& [k, v] : s.toggles ) if ( v ) out[ k ] = v->value;
            for ( const auto& [k, v] : s.numbers ) out[ k ] = v;
            for ( const auto& [k, v] : s.choices ) out[ k ] = v;
            for ( const auto& [k, v] : s.keys ) out[ k ] = v;
            for ( const auto& [k, v] : s.colors ) out[ k ] = v.val;
            for ( const auto& [k, v] : s.tags )
            {
                auto& a = out[ k ] = json::array( );
                for ( int i = 0; i < v.count; ++i ) a.push_back( v.selected[ i ] );
            }
        }
    }

    void menu::save_exact_panel_state( )
    {
        using namespace exact_panel_detail;
        load_state( );
        json out;
        serialize_state( out );
        const auto path = state_path( );
        std::error_code ec;
        std::filesystem::create_directories( path.parent_path( ), ec );
        if ( ec ) return;
        std::ofstream file( path, std::ios::binary | std::ios::trunc );
        if ( !file ) return;
        file << out.dump( 2 );
        values( ).disk = std::move( out );
        values( ).dirty = false;
    }

    void menu::draw_exact_page( )
    {
        using namespace exact_panel_detail;
        load_state( );
        static constexpr std::array<const char*, 10> ids{
            "ragebot", "antiaim", "legitbot", "players", "world", "inventory", "main", "scripts", "configs", "settings"
        };
        this->m_exact_page = std::clamp( this->m_exact_page, 0, 9 );
        const auto id = std::string_view{ ids[ this->m_exact_page ] };

        const auto content_x = this->m_x + tokens::sidebar_w + 12.0f;
        const auto content_y = this->m_y + 66.0f + 11.0f;
        const auto content_w = std::max( 120.0f, this->m_w - tokens::sidebar_w - 24.0f );
        const auto content_h = std::max( 100.0f, this->m_h - 66.0f - 25.0f );
        const auto col_w = ( content_w - 9.0f ) * 0.5f;

        // Existing native managers remain functional for these workflow pages.
        if ( id == "inventory" ) { this->m_tab = 4; this->draw_skins( col_w ); return; }
        if ( id == "scripts" ) { this->m_tab = 6; this->m_subtab = 1; this->draw_config( col_w ); return; }
        if ( id == "configs" ) { this->m_tab = 6; this->m_subtab = 0; this->draw_config( col_w ); return; }

        const auto* page = find_page( id );
        if ( !page ) return;
        xui::layout::set_cursor( content_x - this->m_x, content_y - this->m_y );
        if ( !xui::begin_child( "##exact_page_scroll", content_w, content_h, true ) ) return;

        const auto start = xui::layout::get_cursor( );
        float y = start.second;
        const auto x0 = start.first;
        const auto x1 = x0 + col_w + 9.0f;
        const auto& groups = ( *page )[ "groups" ];
        const auto query = std::string_view{ this->m_search_query };

        for ( std::size_t i = 0; i < groups.size( ); i += 2 )
        {
            const auto lh = panel_height( groups[ i ], query );
            const auto rh = i + 1 < groups.size( ) ? panel_height( groups[ i + 1 ], query ) : 0.0f;
            const auto row_h = std::max( lh, rh );
            if ( row_h <= 0.0f ) continue;
            if ( lh > 0.0f ) { xui::layout::set_cursor( x0, y ); draw_group( id, groups[ i ], col_w, lh, query ); }
            if ( rh > 0.0f ) { xui::layout::set_cursor( x1, y ); draw_group( id, groups[ i + 1 ], col_w, rh, query ); }
            y += row_h + 9.0f;
        }

        xui::layout::set_cursor( x0, y );
        xui::layout::spacing( 1.0f );
        xui::end_child( );
    }
}
'''.replace("__MODEL_JSON__", model_json)

exact_cpp = v / "project/core/rendering/impl/menu/menu.exact.cpp"
exact_cpp.write_text(source, encoding="utf-8")

# Add generated source to project.
proj = v / "MCB-CS2.vcxproj"
t = proj.read_text(encoding="utf-8")
if "menu\\menu.exact.cpp" not in t:
    anchor = '<ClCompile Include="project\\core\\rendering\\impl\\menu\\menu.config.cpp" />'
    if anchor not in t: fail("project menu.config.cpp marker missing")
    t = t.replace(anchor, anchor + '\n    <ClCompile Include="project\\core\\rendering\\impl\\menu\\menu.exact.cpp" />', 1)
proj.write_text(t, encoding="utf-8")

# ---------------------------------------------------------------------------
# Exact shell routing/navigation/topbar.
# ---------------------------------------------------------------------------
menu = v / "project/core/rendering/impl/menu/menu.core.cpp"
t = menu.read_text(encoding="utf-8")

# Nine HTML sidebar pages grouped exactly as Aimbot / Visuals / Miscellaneous.
sidebar_pattern = r"\tvoid menu::draw_side_bar\( float h \)\n\t\{.*?\n\t\}\n\n\tvoid menu::try_load_user_avatar"
sidebar = r'''\tvoid menu::draw_side_bar( float h )
\t{
\t\tauto& dl = xui::draw::current( );
\t\tconst auto& input = xui::ctx( ).input;
\t\ttokens::sidebar_w = this->m_sidebar_compact ? 58.0f : 203.0f;
\t\tconst auto sx = this->m_x;
\t\tconst auto sy = this->m_y;
\t\tconst auto sw = tokens::sidebar_w;
\t\tconst auto sh = h;
\t\tdl.rect_filled( sx, sy, sw, sh, xdraw::color{ 7, 17, 26, 220 }, xdraw::corner_radius::left( 8.0f ) );
\t\tdl.line( sx + sw - 1.0f, sy, sx + sw - 1.0f, sy + sh, xdraw::color{ 173, 223, 255, 25 }, 1.0f );
\t\tdl.line( sx, sy + 67.0f, sx + sw, sy + 67.0f, xdraw::color{ 21, 39, 56, 128 }, 1.0f );

\t\tif ( this->m_sidebar_compact )
\t\t\tdl.text( sx + 16.0f, sy + 26.0f, "MCB", xdraw::color{ 223, 248, 255, 255 } );
\t\telse
\t\t\tdl.text( sx + 21.0f, sy + 25.0f, "MCB", xdraw::color{ 245, 251, 255, 255 } );

\t\tstatic constexpr const char* labels[ 9 ]{ "Ragebot", "Anti Aim", "Legitbot", "Players", "World", "Inventory", "Main", "Scripts", "Configs" };
\t\tstatic constexpr const char* groups[ 3 ]{ "Aimbot", "Visuals", "Miscellaneous" };
\t\tstatic constexpr int icon_map[ 9 ]{ 0, 0, 1, 2, 3, 4, 5, 5, 6 };
\t\tfloat y = sy + 79.0f;
\t\tfor ( int section = 0; section < 3; ++section )
\t\t{
\t\t\tif ( !this->m_sidebar_compact ) dl.text( sx + 16.0f, y + 4.0f, groups[ section ], xdraw::color{ 57, 70, 83, 255 } );
\t\t\ty += 22.0f;
\t\t\tfor ( int j = 0; j < 3; ++j )
\t\t\t{
\t\t\t\tconst auto i = section * 3 + j;
\t\t\t\tconst auto r = xui::rect{ sx + 7.0f, y, sw - 14.0f, 35.0f };
\t\t\t\tconst auto hovered = input.in_rect( r ) && !xui::ctx( ).overlay_blocking( );
\t\t\t\tconst auto active = this->m_exact_page == i;
\t\t\t\tif ( hovered || active )
\t\t\t\t{
\t\t\t\t\tconst auto c = active ? xdraw::color{ 19, 191, 245, 48 } : xdraw::color{ 10, 26, 39, 220 };
\t\t\t\t\tdl.rect_filled( r.x, r.y, r.w, r.h, c, xdraw::corner_radius{ 4.0f } );
\t\t\t\t}
\t\t\t\tif ( active ) dl.rect_filled( r.x, r.y + 5.0f, 2.0f, 25.0f, tokens::col_accent, xdraw::corner_radius{ 1.0f } );
\t\t\t\tconst auto tex = this->m_textures.tabs[ icon_map[ i ] ].resource.Get( );
\t\t\t\tif ( tex ) dl.image( r.x + ( this->m_sidebar_compact ? 13.0f : 10.0f ), r.y + 9.0f, 17.0f, 17.0f, tex, xdraw::color{ 19, 191, 245, 230 } );
\t\t\t\tif ( !this->m_sidebar_compact ) dl.text( r.x + 36.0f, r.y + 12.0f, labels[ i ], active ? xdraw::color{ 244, 251, 255, 255 } : xdraw::color{ 168, 177, 187, 255 } );
\t\t\t\tif ( hovered && input.mouse_clicked ) { this->m_exact_page = i; this->close_search( ); }
\t\t\t\ty += 36.0f;
\t\t\t}
\t\t\ty += 8.0f;
\t\t}

\t\tconst auto py = sy + sh - 71.0f;
\t\tdl.line( sx, py, sx + sw, py, xdraw::color{ 173, 223, 255, 20 }, 1.0f );
\t\tconst auto avatar_x = sx + ( this->m_sidebar_compact ? 12.0f : 13.0f );
\t\tdl.circle_filled( avatar_x + 21.0f, py + 35.0f, this->m_sidebar_compact ? 17.0f : 21.0f, xdraw::color{ 7, 19, 30, 255 }, 32, true );
\t\tdl.circle( avatar_x + 21.0f, py + 35.0f, this->m_sidebar_compact ? 17.0f : 21.0f, xdraw::color{ 19, 191, 245, 115 }, 1.0f, 32, true );
\t\tdl.text( avatar_x + ( this->m_sidebar_compact ? 11.0f : 13.0f ), py + 31.0f, "M", xdraw::color{ 168, 237, 255, 255 } );
\t\tif ( !this->m_sidebar_compact )
\t\t{
\t\t\tdl.text( sx + 65.0f, py + 24.0f, "MCB", xdraw::color{ 238, 245, 249, 255 } );
\t\t\tdl.text( sx + 65.0f, py + 41.0f, "UI ONLY · LOCAL", xdraw::color{ 75, 89, 103, 255 } );
\t\t\tconst auto cr = xui::rect{ sx + sw - 31.0f, py + 24.0f, 21.0f, 21.0f };
\t\t\tif ( cr.contains( input.mouse_x, input.mouse_y ) )
\t\t\t{
\t\t\t\tdl.text( cr.x + 6.0f, cr.y + 4.0f, "‹", xdraw::color{ 179, 234, 255, 255 } );
\t\t\t\tif ( input.mouse_clicked ) this->m_sidebar_compact = true;
\t\t\t}
\t\t\telse dl.text( cr.x + 6.0f, cr.y + 4.0f, "‹", xdraw::color{ 83, 98, 114, 255 } );
\t\t}
\t\telse
\t\t{
\t\t\tconst auto cr = xui::rect{ sx, py, sw, 71.0f };
\t\t\tif ( cr.contains( input.mouse_x, input.mouse_y ) && input.mouse_clicked ) this->m_sidebar_compact = false;
\t\t}
\t}

\tvoid menu::try_load_user_avatar'''
t, n = re.subn(sidebar_pattern, sidebar, t, count=1, flags=re.S)
if n != 1: fail("sidebar replacement failed")

# Draw the HTML topbar and return before the legacy subtab bar code.
top_anchor = "\tvoid menu::draw_top_bar( float w )\n\t{\n"
if "MCB exact uploaded topbar" not in t:
    exact_top = r'''\tvoid menu::draw_top_bar( float w )
\t{
\t\t// MCB exact uploaded topbar.
\t\tauto& dl = xui::draw::current( );
\t\tconst auto& input = xui::ctx( ).input;
\t\tconst auto x = this->m_x + tokens::sidebar_w;
\t\tconst auto y = this->m_y;
\t\tconst auto width = this->m_w - tokens::sidebar_w;
\t\tdl.rect_filled( x, y, width, 66.0f, xdraw::color{ 8, 11, 15, 235 }, xdraw::corner_radius::top( 8.0f ) );
\t\tdl.line( x, y + 65.0f, x + width, y + 65.0f, xdraw::color{ 173, 223, 255, 20 }, 1.0f );

\t\tconst auto save = xui::rect{ x + 14.0f, y + 17.0f, 83.0f, 32.0f };
\t\tconst auto save_hover = save.contains( input.mouse_x, input.mouse_y );
\t\tdl.rect_filled( save.x, save.y, save.w, save.h, save_hover ? xdraw::color{ 10, 24, 36, 255 } : xdraw::color{ 9, 19, 29, 255 }, xdraw::corner_radius{ 4.0f } );
\t\tdl.rect( save.x, save.y, save.w, save.h, save_hover ? xdraw::color{ 33, 68, 93, 255 } : xdraw::color{ 22, 40, 57, 255 }, xdraw::corner_radius{ 4.0f } );
\t\tdl.text( save.x + 13.0f, save.y + 11.0f, "Save", xdraw::color{ 200, 211, 221, 255 } );
\t\tif ( save_hover && input.mouse_clicked ) this->save_exact_panel_state( );

\t\tstatic constexpr const char* presets[ 4 ]{ "Global", "Default", "Legit", "Visuals" };
\t\tconst auto cfg = xui::rect{ x + 104.0f, y + 17.0f, 128.0f, 32.0f };
\t\tconst auto cfg_hover = cfg.contains( input.mouse_x, input.mouse_y );
\t\tdl.rect_filled( cfg.x, cfg.y, cfg.w, cfg.h, xdraw::color{ 8, 18, 27, 255 }, xdraw::corner_radius{ 4.0f } );
\t\tdl.rect( cfg.x, cfg.y, cfg.w, cfg.h, cfg_hover ? xdraw::color{ 29, 82, 111, 255 } : xdraw::color{ 22, 40, 57, 255 }, xdraw::corner_radius{ 4.0f } );
\t\tdl.text( cfg.x + 9.0f, cfg.y + 11.0f, presets[ std::clamp( this->m_exact_config_index, 0, 3 ) ], xdraw::color{ 166, 177, 188, 255 } );
\t\tdl.text( cfg.right( ) - 17.0f, cfg.y + 10.0f, "⌄", xdraw::color{ 98, 113, 125, 255 } );
\t\tif ( cfg_hover && input.mouse_clicked ) this->m_exact_config_index = ( this->m_exact_config_index + 1 ) % 4;

\t\tconst auto settings = xui::rect{ x + width - 44.0f, y + 18.0f, 30.0f, 30.0f };
\t\tconst auto search = xui::rect{ settings.x - 34.0f, settings.y, 30.0f, 30.0f };
\t\tconst auto theme = xui::rect{ search.x - 34.0f, settings.y, 30.0f, 30.0f };
\t\tfor ( const auto& r : { theme, search, settings } ) if ( r.contains( input.mouse_x, input.mouse_y ) ) dl.rect_filled( r.x, r.y, r.w, r.h, xdraw::color{ 11, 24, 35, 255 }, xdraw::corner_radius{ 4.0f } );
\t\tdl.text( theme.x + 8.0f, theme.y + 8.0f, "◐", this->m_dim_interface ? tokens::col_accent : xdraw::color{ 102, 119, 135, 255 } );
\t\tdl.text( search.x + 8.0f, search.y + 8.0f, "⌕", xdraw::color{ 102, 119, 135, 255 } );
\t\tdl.text( settings.x + 8.0f, settings.y + 8.0f, "⚙", xdraw::color{ 102, 119, 135, 255 } );
\t\tif ( theme.contains( input.mouse_x, input.mouse_y ) && input.mouse_clicked ) this->m_dim_interface = !this->m_dim_interface;
\t\tif ( search.contains( input.mouse_x, input.mouse_y ) && input.mouse_clicked ) this->m_search_open = !this->m_search_open;
\t\tif ( settings.contains( input.mouse_x, input.mouse_y ) && input.mouse_clicked ) { this->m_exact_page = 9; this->close_search( ); }

\t\tif ( this->m_search_open )
\t\t{
\t\t\tconst auto sx = theme.x - 170.0f;
\t\t\txui::layout::set_cursor( sx - this->m_x, y + 21.0f - this->m_y );
\t\t\txui::text_input( "##exact_search", this->m_search_query, 80, "Search" );
\t\t}
\t\treturn;
'''
    t = replace_once(t, top_anchor, exact_top, "exact topbar")

# Replace legacy 7-tab dispatcher with exact-model renderer.
old_switch = '''\t\t\tswitch ( this->m_tab )
\t\t\t{
\t\t\tcase 0: this->draw_ragebot( col_w ); break;
\t\t\tcase 1: this->draw_legitbot( col_w ); break;
\t\t\tcase 2: this->draw_player( col_w ); break;
\t\t\tcase 3: this->draw_world( col_w ); break;
\t\t\tcase 4: this->draw_skins( col_w ); break;
\t\t\tcase 5: this->draw_misc( col_w ); break;
\t\t\tcase 6: this->draw_config( col_w ); break;
\t\t\t}'''
if old_switch in t:
    t = t.replace(old_switch, "\t\t\tthis->draw_exact_page( );", 1)
elif "this->draw_exact_page( );" not in t:
    fail("legacy dispatch switch not found")

# Drag only from the empty center portion of the 66 px topbar so buttons never
# fight the window drag handler.
xui = v / "project/external/xdraw/xui/xui.cpp"
x = xui.read_text(encoding="utf-8")
x = x.replace(
    "const auto drag_zone = rect{ abs.x, abs.y, abs.w, std::min( 52.0f, abs.h ) };",
    "const auto drag_zone = rect{ abs.x + tokens::sidebar_w + 238.0f, abs.y, std::max( 0.0f, abs.w - tokens::sidebar_w - 458.0f ), std::min( 66.0f, abs.h ) };",
    1)
xui.write_text(x, encoding="utf-8")

menu.write_text(t, encoding="utf-8")
print("[exact-panel-v5] exact 10-page uploaded shell/model applied")
