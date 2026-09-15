from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
v = root / 'cs2' / 'MCB-CS2'
exact = v / 'project/core/rendering/impl/menu/menu.exact.cpp'
if not exact.exists():
    raise SystemExit('[v61-pages] menu.exact.cpp missing')

t = exact.read_text(encoding='utf-8')

helper = r'''
    namespace exact_panel_v61
    {
        using json = nlohmann::json;

        struct inventory_state
        {
            int tab{};
            int selected{};
            int weapon_filter{};
            int rarity_filter{};
            int sort_mode{};
            int view_mode{};
            int sticker_slot{};
            bool inspect_open{};
            std::string search{};
            xdraw::color weapon_color{ 19, 191, 245, 255 };
        };

        inventory_state& inventory( )
        {
            static inventory_state s{};
            return s;
        }

        struct scripts_state
        {
            int selected{};
            int file_tab{};
            std::string search{};
            std::string editor_line{ "print(\\\"MCB local editor preview\\\")" };
        };

        scripts_state& scripts( )
        {
            static scripts_state s{};
            return s;
        }

        struct configs_state
        {
            bool loaded{};
            int selected{};
            int next_custom{ 1 };
            bool delete_confirm{};
            std::vector<std::string> names{ "Global", "Default", "Legit", "Visuals" };
        };

        std::filesystem::path local_dir( )
        {
            wchar_t buffer[ 32768 ]{};
            const auto n = GetEnvironmentVariableW( L"LOCALAPPDATA", buffer, static_cast<DWORD>( std::size( buffer ) ) );
            std::filesystem::path base = n && n < std::size( buffer ) ? std::filesystem::path{ buffer } : std::filesystem::temp_directory_path( );
            return base / L"MCB";
        }

        std::filesystem::path config_index_path( ) { return local_dir( ) / L"panel_configs.json"; }
        std::filesystem::path exported_config_path( ) { return local_dir( ) / L"exported_config.json"; }
        std::filesystem::path local_snapshot_path( ) { return local_dir( ) / L"local_snapshot.json"; }

        configs_state& configs( )
        {
            static configs_state s{};
            if ( !s.loaded )
            {
                s.loaded = true;
                std::ifstream f( config_index_path( ), std::ios::binary );
                if ( f )
                {
                    try
                    {
                        json j; f >> j;
                        if ( j.contains( "names" ) && j[ "names" ].is_array( ) )
                        {
                            auto loaded = j[ "names" ].get<std::vector<std::string>>( );
                            if ( loaded.size( ) >= 4 ) s.names = std::move( loaded );
                        }
                        if ( j.contains( "next_custom" ) ) s.next_custom = std::max( 1, j[ "next_custom" ].get<int>( ) );
                    }
                    catch ( ... ) {}
                }
            }
            return s;
        }

        void persist_configs( )
        {
            auto& s = configs( );
            std::error_code ec;
            std::filesystem::create_directories( local_dir( ), ec );
            if ( ec ) return;
            std::ofstream f( config_index_path( ), std::ios::binary | std::ios::trunc );
            if ( !f ) return;
            json j{{ "names", s.names }, { "next_custom", s.next_custom }};
            f << j.dump( 2 );
        }

        void write_config_snapshot( const std::filesystem::path& path, std::string_view name )
        {
            std::error_code ec;
            std::filesystem::create_directories( path.parent_path( ), ec );
            if ( ec ) return;
            json ui_state;
            exact_panel_detail::serialize_state( ui_state );
            json out{
                { "format", "MCB UI-only config snapshot" },
                { "config", std::string( name ) },
                { "gameplay_execution", false },
                { "panel_state", std::move( ui_state ) }
            };
            std::ofstream f( path, std::ios::binary | std::ios::trunc );
            if ( f ) f << out.dump( 2 );
        }

        void draw_inventory( float w, float h )
        {
            auto& s = inventory( );
            if ( !xui::begin_child( "##v61_inventory_root", w, h, true ) ) return;

            static constexpr const char* tabs[ 5 ]{ "Loadout", "Skins", "Agents", "Stickers", "Keychains" };
            const auto [avail_w0, avail_h0] = xui::layout::avail( );
            const auto tab_w = std::max( 72.0f, ( avail_w0 - 4.0f * 7.0f ) / 5.0f );
            for ( int i = 0; i < 5; ++i )
            {
                std::string id = std::string( tabs[ i ] ) + "##inv_tab_" + std::to_string( i );
                if ( xui::button( id, tab_w, 28.0f ) ) { s.tab = i; s.selected = 0; }
                if ( i != 4 ) xui::layout::same_line( );
            }

            xui::layout::separator( );
            xui::text_input( "##inventory_search", s.search, 64, "Search inventory..." );

            static constexpr const char* weapon_filters[ 5 ]{ "All Weapons", "Rifles", "Pistols", "Snipers", "Knives" };
            static constexpr const char* rarities[ 6 ]{ "Any Rarity", "Consumer", "Industrial", "Mil-Spec", "Restricted", "Classified" };
            static constexpr const char* sort_modes[ 4 ]{ "Name", "Rarity", "Newest", "Equipped" };
            static constexpr const char* view_modes[ 2 ]{ "Grid", "List" };
            xui::combo( "Weapon Filter", s.weapon_filter, weapon_filters, 5 );
            xui::layout::same_line( );
            xui::combo( "Rarity Filter", s.rarity_filter, rarities, 6 );
            xui::layout::same_line( );
            xui::combo( "Sort", s.sort_mode, sort_modes, 4 );
            xui::layout::same_line( );
            xui::combo( "View", s.view_mode, view_modes, 2 );

            static constexpr const char* loadout[ 6 ]{ "AK-47", "M4A1-S", "AWP", "Desert Eagle", "Glock-18", "Karambit" };
            static constexpr const char* skins[ 12 ]{ "Asiimov", "Printstream", "Neo-Noir", "Vulcan", "Fade", "Doppler", "Hyper Beast", "Redline", "Mecha Industries", "Bloodsport", "Nightwish", "Slate" };
            static constexpr const char* agents[ 6 ]{ "Cmdr. Mae", "Special Agent Ava", "Lt. Commander Ricksaw", "Number K", "Sir Bloody Miami Darryl", "Getaway Sally" };
            static constexpr const char* stickers[ 8 ]{ "Crown", "Dragon Lore", "Howling Dawn", "Titan", "iBUYPOWER", "Katowice", "MCB Cyan", "Minimal" };
            static constexpr const char* keychains[ 6 ]{ "Mini Knife", "Lucky Coin", "Pixel Cat", "Cyan Cube", "Star", "Classic Tag" };
            const char* const* items = loadout; int count = 6;
            if ( s.tab == 1 ) { items = skins; count = 12; }
            else if ( s.tab == 2 ) { items = agents; count = 6; }
            else if ( s.tab == 3 ) { items = stickers; count = 8; }
            else if ( s.tab == 4 ) { items = keychains; count = 6; }
            s.selected = std::clamp( s.selected, 0, count - 1 );

            const auto start = xui::layout::get_cursor( );
            const auto [avail_w, avail_h] = xui::layout::avail( );
            const auto left_w = std::max( 220.0f, avail_w * 0.58f );
            const auto right_w = std::max( 170.0f, avail_w - left_w - 9.0f );
            if ( xui::begin_child( "##inventory_browser", left_w, std::max( 220.0f, avail_h ), true ) )
            {
                for ( int i = 0; i < count; ++i )
                {
                    const std::string name = items[ i ];
                    if ( !s.search.empty( ) )
                    {
                        auto low = name; auto q = s.search;
                        std::transform( low.begin( ), low.end( ), low.begin( ), []( unsigned char c ){ return static_cast<char>( std::tolower( c ) ); } );
                        std::transform( q.begin( ), q.end( ), q.begin( ), []( unsigned char c ){ return static_cast<char>( std::tolower( c ) ); } );
                        if ( low.find( q ) == std::string::npos ) continue;
                    }
                    std::string id = name + "##inv_item_" + std::to_string( i );
                    if ( xui::button( id, std::max( 150.0f, left_w - 22.0f ), s.view_mode == 0 ? 40.0f : 28.0f ) ) s.selected = i;
                }
                xui::end_child( );
            }
            xui::layout::set_cursor( start.first + left_w + 9.0f, start.second );
            if ( xui::begin_child( "##inventory_inspector", right_w, std::max( 220.0f, avail_h ), true ) )
            {
                xui::text( "Large Preview", tokens::col_text );
                xui::text( items[ s.selected ], tokens::col_accent );
                xui::text( tabs[ s.tab ], tokens::col_text_dim );
                xui::layout::separator( );
                if ( s.tab <= 1 )
                {
                    xui::color_picker( "Weapon Color", s.weapon_color, 0.0f, false );
                    xui::text( "Stickers", tokens::col_text );
                    for ( int slot = 0; slot < 4; ++slot )
                    {
                        std::string label = std::string( "Slot " ) + std::to_string( slot + 1 ) + "##sticker_slot";
                        if ( xui::button( label, 64.0f, 24.0f ) ) s.sticker_slot = slot;
                        if ( slot != 3 ) xui::layout::same_line( );
                    }
                    xui::text( std::string( "Selected sticker slot: " ) + std::to_string( s.sticker_slot + 1 ), tokens::col_text_dim );
                    xui::text( "Keychain", tokens::col_text );
                    xui::text( "Local preview attachment", tokens::col_text_dim );
                }
                else if ( s.tab == 2 )
                {
                    xui::text( "Agent / Model Presentation", tokens::col_text );
                    xui::text( "Front model preview · UI only", tokens::col_text_dim );
                }
                else if ( s.tab == 3 )
                {
                    xui::text( "Sticker Preview", tokens::col_text );
                    xui::text( "4-slot placement preview · local only", tokens::col_text_dim );
                }
                else
                {
                    xui::text( "Keychain Preview", tokens::col_text );
                    xui::text( "Attachment preview · local only", tokens::col_text_dim );
                }
                xui::layout::separator( );
                if ( xui::button( s.inspect_open ? "Close Inspect Drawer" : "Open Inspect Drawer", right_w - 20.0f, 28.0f ) ) s.inspect_open = !s.inspect_open;
                if ( s.inspect_open )
                {
                    xui::text( "Inspect Drawer", tokens::col_text );
                    xui::text( "Name / rarity / wear / local presentation", tokens::col_text_dim );
                }
                xui::layout::separator( );
                xui::text( "LOCAL UI PREVIEW ONLY", xdraw::color{ 108, 197, 230, 235 } );
                xui::end_child( );
            }
            xui::end_child( );
        }

        void draw_scripts( float w, float h )
        {
            auto& s = scripts( );
            if ( !xui::begin_child( "##v61_scripts_root", w, h, true ) ) return;
            xui::text( "Scripts", tokens::col_text );
            xui::text( "EXECUTION DISABLED", xdraw::color{ 255, 174, 95, 255 } );
            xui::text_input( "##script_search", s.search, 64, "Search scripts..." );
            if ( xui::button( "Reload##scripts_local", 86.0f, 26.0f ) ) {}
            xui::layout::same_line( );
            xui::text( "Status: Idle", tokens::col_text_dim );
            xui::layout::separator( );

            const auto start = xui::layout::get_cursor( );
            const auto [avail_w, avail_h] = xui::layout::avail( );
            const auto left_w = std::max( 185.0f, avail_w * 0.31f );
            if ( xui::begin_child( "##script_list", left_w, std::max( 240.0f, avail_h ), true ) )
            {
                static constexpr const char* names[ 4 ]{ "main.lua", "settings.lua", "hud_preview.lua", "example.lua" };
                for ( int i = 0; i < 4; ++i )
                {
                    std::string label = std::string( names[ i ] ) + "##script_" + std::to_string( i );
                    if ( xui::button( label, left_w - 20.0f, 30.0f ) ) s.selected = i;
                }
                xui::layout::separator( );
                xui::text( "Enabled / Disabled / Idle", tokens::col_text_dim );
                xui::end_child( );
            }
            xui::layout::set_cursor( start.first + left_w + 9.0f, start.second );
            const auto right_w = std::max( 220.0f, avail_w - left_w - 9.0f );
            if ( xui::begin_child( "##script_details", right_w, std::max( 240.0f, avail_h ), true ) )
            {
                xui::text( "Details / Settings", tokens::col_text );
                if ( xui::button( "main.lua##editor_tab_main", 90.0f, 25.0f ) ) s.file_tab = 0;
                xui::layout::same_line( );
                if ( xui::button( "settings.lua##editor_tab_settings", 110.0f, 25.0f ) ) s.file_tab = 1;
                xui::layout::separator( );
                xui::text( s.file_tab == 0 ? "-- MCB local script editor preview" : "-- Local settings file preview", tokens::col_text_dim );
                xui::text( s.file_tab == 0 ? "function on_frame(dt)" : "return { opacity = 0.96 }", tokens::col_text );
                if ( s.file_tab == 0 ) xui::text( "    -- execution is intentionally disabled", tokens::col_text );
                if ( s.file_tab == 0 ) xui::text( "end", tokens::col_text );
                xui::text_input( "##editor_line", s.editor_line, 512, "Editor line..." );
                xui::layout::separator( );
                xui::text( "Editor Footer · UTF-8 · Lua · Local presentation", tokens::col_text_dim );
                xui::text( "No script execution is performed by this view.", xdraw::color{ 255, 174, 95, 230 } );
                xui::end_child( );
            }
            xui::end_child( );
        }

        void draw_configs( float w, float h )
        {
            auto& s = configs( );
            s.selected = std::clamp( s.selected, 0, std::max( 0, static_cast<int>( s.names.size( ) ) - 1 ) );
            if ( !xui::begin_child( "##v61_configs_root", w, h, true ) ) return;
            xui::text( "Configs", tokens::col_text );
            xui::text( "Local UI snapshots only", tokens::col_text_dim );

            if ( xui::button( "New", 58.0f, 26.0f ) )
            {
                s.names.push_back( "Custom " + std::to_string( s.next_custom++ ) );
                s.selected = static_cast<int>( s.names.size( ) ) - 1;
                persist_configs( );
            }
            xui::layout::same_line( );
            if ( xui::button( "Refresh", 70.0f, 26.0f ) ) { s.loaded = false; ( void )configs( ); }
            xui::layout::same_line( );
            if ( xui::button( "Load", 58.0f, 26.0f ) ) {}
            xui::layout::same_line( );
            if ( xui::button( "Save", 58.0f, 26.0f ) )
                write_config_snapshot( local_dir( ) / ( std::wstring( L"config_" ) + std::to_wstring( s.selected ) + L".json" ), s.names[ s.selected ] );
            xui::layout::same_line( );
            if ( xui::button( "Delete", 64.0f, 26.0f ) && s.selected >= 4 ) s.delete_confirm = true;
            xui::layout::same_line( );
            if ( xui::button( "Export JSON", 92.0f, 26.0f ) ) write_config_snapshot( exported_config_path( ), s.names[ s.selected ] );
            xui::layout::same_line( );
            if ( xui::button( "Import JSON", 92.0f, 26.0f ) )
            {
                std::ifstream f( exported_config_path( ), std::ios::binary );
                if ( f )
                {
                    try { json j; f >> j; if ( j.contains( "config" ) ) { s.names.push_back( j[ "config" ].get<std::string>( ) + " Imported" ); s.selected = static_cast<int>( s.names.size( ) ) - 1; persist_configs( ); } }
                    catch ( ... ) {}
                }
            }
            xui::layout::same_line( );
            if ( xui::button( "Local Snapshot", 104.0f, 26.0f ) ) write_config_snapshot( local_snapshot_path( ), s.names[ s.selected ] );

            xui::layout::separator( );
            if ( s.delete_confirm )
            {
                xui::text( "Confirm delete selected custom config?", xdraw::color{ 255, 174, 95, 255 } );
                if ( xui::button( "Confirm Delete", 110.0f, 26.0f ) )
                {
                    if ( s.selected >= 4 && s.selected < static_cast<int>( s.names.size( ) ) ) s.names.erase( s.names.begin( ) + s.selected );
                    s.selected = std::clamp( s.selected - 1, 0, std::max( 0, static_cast<int>( s.names.size( ) ) - 1 ) );
                    s.delete_confirm = false; persist_configs( );
                }
                xui::layout::same_line( );
                if ( xui::button( "Cancel##cfg_delete", 70.0f, 26.0f ) ) s.delete_confirm = false;
                xui::layout::separator( );
            }

            const auto [avail_w, avail_h] = xui::layout::avail( );
            const auto start = xui::layout::get_cursor( );
            const auto left_w = std::max( 190.0f, avail_w * 0.38f );
            if ( xui::begin_child( "##config_list", left_w, std::max( 220.0f, avail_h ), true ) )
            {
                for ( int i = 0; i < static_cast<int>( s.names.size( ) ); ++i )
                {
                    std::string label = s.names[ i ] + "##cfg_" + std::to_string( i );
                    if ( xui::button( label, left_w - 20.0f, 30.0f ) ) s.selected = i;
                }
                xui::end_child( );
            }
            xui::layout::set_cursor( start.first + left_w + 9.0f, start.second );
            if ( xui::begin_child( "##config_details", std::max( 210.0f, avail_w - left_w - 9.0f ), std::max( 220.0f, avail_h ), true ) )
            {
                xui::text( "Selected Config", tokens::col_text );
                xui::text( s.names[ s.selected ], tokens::col_accent );
                xui::layout::separator( );
                xui::text( s.selected == 0 ? "Global" : s.selected == 1 ? "Default" : s.selected == 2 ? "Legit" : s.selected == 3 ? "Visuals" : "Custom Config", tokens::col_text_dim );
                xui::text( "Unsaved config indicator: local UI state", tokens::col_text_dim );
                xui::text( "JSON import/export does not modify the game.", tokens::col_text_dim );
                xui::end_child( );
            }
            xui::end_child( );
        }
    }
'''

marker = '    void menu::save_exact_panel_state( )\n'
if 'namespace exact_panel_v61' not in t:
    if marker not in t:
        raise SystemExit('[v61-pages] save state marker missing')
    t = t.replace(marker, helper + '\n' + marker, 1)

old = '''        // Existing native managers remain functional for these workflow pages.\n        if ( id == "inventory" ) { this->m_tab = 4; this->draw_skins( col_w ); return; }\n        if ( id == "scripts" ) { this->m_tab = 6; this->m_subtab = 1; this->draw_config( col_w ); return; }\n        if ( id == "configs" ) { this->m_tab = 6; this->m_subtab = 0; this->draw_config( col_w ); return; }\n'''
new = '''        // v6.1 custom pages are local/UI-only and do not alter gameplay state.\n        if ( id == "inventory" )\n        {\n            xui::layout::set_cursor( content_x - this->m_x, content_y - this->m_y );\n            exact_panel_v61::draw_inventory( content_w, content_h );\n            return;\n        }\n        if ( id == "scripts" )\n        {\n            xui::layout::set_cursor( content_x - this->m_x, content_y - this->m_y );\n            exact_panel_v61::draw_scripts( content_w, content_h );\n            return;\n        }\n        if ( id == "configs" )\n        {\n            xui::layout::set_cursor( content_x - this->m_x, content_y - this->m_y );\n            exact_panel_v61::draw_configs( content_w, content_h );\n            return;\n        }\n'''
if old not in t:
    if 'exact_panel_v61::draw_inventory' not in t:
        raise SystemExit('[v61-pages] legacy workflow page routing block missing')
else:
    t = t.replace(old, new, 1)

exact.write_text(t, encoding='utf-8')
(root / 'MCB_V61_CUSTOM_PAGES_APPLIED.json').write_text('''{\n  "inventory": "custom local-only view",\n  "scripts": "editor presentation; EXECUTION DISABLED",\n  "configs": "local JSON/snapshot view",\n  "new_gameplay_logic": false,\n  "new_antidetection_logic": false\n}\n''', encoding='utf-8')
print('[v61-pages] custom Inventory / Scripts / Configs views applied')
