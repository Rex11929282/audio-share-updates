from pathlib import Path
import hashlib, json, re, sys

ROOT=Path(sys.argv[1]).resolve()

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")

def write(rel,text):
    (ROOT/rel).write_text(text,encoding="utf-8",newline="\n")

def replace_once(rel,old,new,label):
    s=read(rel)
    if old not in s:
        raise RuntimeError("missing anchor: "+label)
    write(rel,s.replace(old,new,1))

# 1) UI state: inventory tool panel uses the menu member state; misc section
# state becomes a one-shot scroll target instead of an accordion selector.
replace_once(
    "project/core/rendering/rendering.hpp",
    "        bool m_inventory_tools_open{};\n        bool m_hotkey_overview_open{};\n        bool m_layout_editor_open{};\n        int m_misc_section_open{ 0 };",
    "        mutable bool m_inventory_tools_open{};\n        bool m_hotkey_overview_open{};\n        bool m_layout_editor_open{};\n        int m_misc_section_open{ -1 };",
    "render state",
)

# 2) Inventory: remove same-line overlap, reuse member disclosure state, and
# adapt actual column count to available width.
rel="project/core/rendering/impl/menu/menu.skins.cpp"
s=read(rel)
old='''\t\t\tauto& inventory_ui = settings::g_changer.ui;
\t\t\tstatic bool tools_open{};
\t\t\tif ( xui::button( tools_open ? "显示选项  ▴##inventory_tools" : "显示选项  ▾##inventory_tools", std::min( 190.0f, inner_w ), 28.0f ) ) tools_open = !tools_open;
\t\t\tif ( tools_open )
\t\t\t{
\t\t\t\tif ( xui::begin_child( "##inventory_tools_panel", inner_w, 84.0f, false ) )
\t\t\t\t{
\t\t\t\t\txui::combo( "排序##inventory_sort", inventory_ui.sort.value, std::array<const char*,3>{ "名称", "编号", "已应用优先" }.data(), 3, 132.0f );
\t\t\t\t\txui::layout::same_line( ); xui::slider_int( "列数##inventory_columns", inventory_ui.columns, 3, 7, "%d" );
\t\t\t\t\txui::checkbox( "显示图片##inventory_images", inventory_ui.show_images );
\t\t\t\t\txui::layout::same_line( ); xui::checkbox( "紧凑卡片##inventory_compact", inventory_ui.compact_cards );
\t\t\t\t\txui::end_child( );
\t\t\t\t}
\t\t\t}
\t\t\tconst auto columns = std::clamp( inventory_ui.columns.value, 3, 7 );'''
new='''\t\t\tauto& inventory_ui = settings::g_changer.ui;
\t\t\tif ( xui::button( this->m_inventory_tools_open ? "显示选项  ▴##inventory_tools" : "显示选项  ▾##inventory_tools", std::min( 190.0f, inner_w ), 28.0f ) ) this->m_inventory_tools_open = !this->m_inventory_tools_open;
\t\t\tif ( this->m_inventory_tools_open )
\t\t\t{
\t\t\t\tif ( xui::begin_child( "##inventory_tools_panel", inner_w, 146.0f, false ) )
\t\t\t\t{
\t\t\t\t\txui::combo( "排序##inventory_sort", inventory_ui.sort.value, std::array<const char*,3>{ "名称", "编号", "已应用优先" }.data(), 3, std::min( 180.0f, xui::layout::avail( ).first ) );
\t\t\t\t\txui::slider_int( "列数##inventory_columns", inventory_ui.columns, 3, 7, "%d" );
\t\t\t\t\txui::checkbox( "显示图片##inventory_images", inventory_ui.show_images );
\t\t\t\t\txui::checkbox( "紧凑卡片##inventory_compact", inventory_ui.compact_cards );
\t\t\t\t\txui::end_child( );
\t\t\t\t}
\t\t\t}
\t\t\tconst auto requested_columns = std::clamp( inventory_ui.columns.value, 3, 7 );
\t\t\tconst auto min_card_w = inventory_ui.compact_cards.value ? 112.0f : 138.0f;
\t\t\tconst auto fit_columns = std::max( 1, static_cast<int>( std::floor( ( inner_w + detail::k_card_gap ) / ( min_card_w + detail::k_card_gap ) ) ) );
\t\t\tconst auto columns = std::max( 1, std::min( requested_columns, fit_columns ) );'''
if old not in s: raise RuntimeError("missing inventory tools block")
write(rel,s.replace(old,new,1))

# 3) LEGIT becomes responsive like RAGE/player/misc.
rel="project/core/rendering/impl/menu/menu.legitbot.cpp"
s=read(rel)
old='''\t\tconst auto content_w = xui::layout::current_window()->bounds.w - tokens::gap * 2.0f;
\t\tconst auto col_w = ( content_w - tokens::gap ) * 0.5f;
\t\tconst auto right_x = content_x + col_w + tokens::gap;'''
new='''\t\tconst auto content_w = std::max( 280.0f, xui::layout::current_window()->bounds.w - tokens::gap * 2.0f );
\t\tconst auto single_col = content_w < 760.0f;
\t\tconst auto col_w = single_col ? content_w : ( content_w - tokens::gap ) * 0.5f;
\t\tconst auto right_x = content_x + col_w + tokens::gap;'''
if old not in s: raise RuntimeError("missing legit geometry")
s=s.replace(old,new,1)
old="\t\txui::layout::set_cursor( right_x - wx, body_y - wy );"
if old not in s: raise RuntimeError("missing legit right column")
s=s.replace(old,"\t\tif ( !single_col ) xui::layout::set_cursor( right_x - wx, body_y - wy );",1)
write(rel,s)

# 4) Other: all sections stay visible in one scrolling page; bordered children
# are the only grouping. m_misc_section_open is only a search scroll request.
rel="project/core/rendering/impl/menu/menu.exact.cpp"
s=read(rel)
signature="    void menu::draw_misc_hub( float x, float y, float w, float h )"
start=s.find(signature)
if start<0: raise RuntimeError("missing draw_misc_hub")
brace=s.find("{",start); depth=0; end=None
for i in range(brace,len(s)):
    if s[i]=="{": depth+=1
    elif s[i]=="}":
        depth-=1
        if depth==0:
            end=i+1
            break
if end is None: raise RuntimeError("unterminated draw_misc_hub")
replacement='''    void menu::draw_misc_hub( float x, float y, float w, float h )
    {
        xui::layout::set_cursor( x - this->m_x, y - this->m_y );
        const auto root_id = xui::make_id( "##misc_all_in_one" );
        if ( this->m_misc_section_open >= 0 )
        {
            static constexpr float section_offsets[]{ 0.0f, 782.0f, 1438.0f, 2034.0f, 2550.0f, 3196.0f };
            const auto idx = std::clamp( this->m_misc_section_open, 0, 5 );
            auto& scroll = xui::ctx( ).child_scroll_cache[ root_id ];
            scroll.scroll = section_offsets[ idx ];
            scroll.scroll_target = section_offsets[ idx ];
            this->m_misc_section_open = -1;
        }
        if ( !xui::begin_child( "##misc_all_in_one", w, h, true ) ) return;

        struct section { const char* title; float height; int kind; };
        static constexpr section sections[]{
            { "反馈、移动与系统", 720.0f, 0 },
            { "物品、投掷物与战局信息", 594.0f, 1 },
            { "环境与天气", 534.0f, 2 },
            { "镜头与第一人称", 454.0f, 3 },
            { "HUD 与屏幕组件", 584.0f, 4 },
            { "画面移除", 320.0f, 5 },
        };

        for ( int i = 0; i < static_cast<int>( std::size( sections ) ); ++i )
        {
            const auto aw = std::max( 260.0f, xui::layout::avail( ).first );
            xui::text( sections[i].title, tokens::col_text );
            std::string child = "##misc_section_body_" + std::to_string( i );
            if ( xui::begin_child( child, aw, sections[i].height, false ) )
            {
                const auto saved = this->m_subtab;
                if ( sections[i].kind == 0 ) { this->m_subtab = 0; this->draw_misc( aw ); }
                else if ( sections[i].kind == 1 ) { this->m_subtab = 0; this->draw_world( aw ); }
                else if ( sections[i].kind == 2 ) this->draw_environment_page( aw );
                else if ( sections[i].kind == 3 ) { this->m_subtab = 2; this->draw_misc( aw ); }
                else if ( sections[i].kind == 4 ) { this->m_subtab = 3; this->draw_misc( aw ); }
                else { this->m_subtab = 1; this->draw_misc( aw ); }
                this->m_subtab = saved;
                xui::end_child( );
            }
            xui::layout::spacing( 10.0f );
        }
        xui::end_child( );
    }'''
write(rel,s[:start]+replacement+s[end:])

# 5) Search: rebuild on every open, include major non-bind controls, route
# ANTI-AIM/misc/profile correctly, and reserve text width for key badges.
rel="project/core/rendering/impl/menu/menu.core.cpp"
s=read(rel)
s=s.replace(
'''\t\tif ( this->m_sidebar_compact != this->m_exact_last_compact )
\t\t{
\t\t\tconst auto delta_w = this->m_sidebar_compact ? -140.0f : 140.0f;
\t\t\tthis->m_w = std::max( this->m_sidebar_compact ? 840.0f : 980.0f, this->m_w + delta_w );
\t\t\tthis->m_exact_last_compact = this->m_sidebar_compact;
\t\t}''',
'''\t\tif ( this->m_sidebar_compact != this->m_exact_last_compact )
\t\t{
\t\t\t// Compacting the sidebar must not overwrite the user's chosen window size.
\t\t\tthis->m_exact_last_compact = this->m_sidebar_compact;
\t\t}''',
1)
s=s.replace(
"this->m_w = std::clamp( this->m_w, std::min( this->m_sidebar_compact ? 700.0f : 780.0f, available_w ), available_w );",
"this->m_w = std::clamp( this->m_w, std::min( 780.0f, available_w ), available_w );",
1)
s=s.replace(
'this->m_sidebar_compact ? 700.0f : 780.0f, 500.0f, menu_reveal',
'780.0f, 500.0f, menu_reveal',
1)
s=s.replace(
'if ( this->m_search_open && this->m_search_entries.empty( ) ) this->rebuild_search_index( );',
'if ( this->m_search_open ) this->rebuild_search_index( );',
1)

needle='''\t\t\tthis->m_search_entries.emplace_back( std::move( entry ) );
\t\t}
\t}

\tvoid menu::close_search( )'''
extras='''\t\t\tthis->m_search_entries.emplace_back( std::move( entry ) );
\t\t}

\t\tauto add_manual = [ & ]( std::string name, std::string category )
\t\t{
\t\t\tsearch_entry entry{};
\t\t\tentry.name = std::move( name );
\t\t\tentry.category = std::move( category );
\t\t\tentry.name_lower = detail::to_lower_copy( entry.name );
\t\t\tentry.category_lower = detail::to_lower_copy( entry.category );
\t\t\tstd::tie( entry.tab, entry.subtab ) = detail::map_category_to_tab( entry.category_lower );
\t\t\tthis->m_search_entries.emplace_back( std::move( entry ) );
\t\t};

\t\tfor ( const auto& e : std::array<std::pair<const char*, const char*>, 41>{
\t\t\tstd::pair{ "最大视野角", "战斗 · RAGE" }, { "命中率", "战斗 · RAGE" }, { "最低伤害", "战斗 · RAGE" }, { "点缩放", "战斗 · RAGE" }, { "最大回溯", "战斗 · RAGE" },
\t\t\t{ "平滑", "战斗 · LEGIT" }, { "触发延迟", "战斗 · LEGIT" }, { "触发命中率", "战斗 · LEGIT" }, { "最低伤害", "战斗 · LEGIT" },
\t\t\t{ "俯仰", "战斗 · ANTI-AIM" }, { "方向指示器", "战斗 · ANTI-AIM" }, { "指示器半径", "战斗 · ANTI-AIM" },
\t\t\t{ "最大显示距离", "玩家 · 敌人" }, { "只显示可见目标", "玩家 · 敌人" }, { "最大显示距离", "玩家 · 队友" },
\t\t\t{ "分类", "库存" }, { "排序", "库存" }, { "列数", "库存" }, { "显示图片", "库存" }, { "紧凑卡片", "库存" },
\t\t\t{ "命中信息列", "其他 · 战术反馈" }, { "移动", "其他 · 移动" }, { "自动购买", "其他 · 系统" },
\t\t\t{ "投掷物", "其他 · 投掷物" }, { "炸弹计时器", "其他 · 战局信息" }, { "观战列表", "其他 · 战局信息" },
\t\t\t{ "天气效果", "其他 · 环境与天气" }, { "雾效", "其他 · 环境与天气" }, { "风场", "其他 · 环境与天气" }, { "景深", "其他 · 环境与天气" },
\t\t\t{ "自定义视野角", "其他 · 镜头" }, { "第三人称", "其他 · 镜头" }, { "第一人称模型", "其他 · 第一人称" },
\t\t\t{ "准星叠加", "其他 · HUD" }, { "速度计", "其他 · HUD" }, { "快捷键面板", "其他 · HUD" }, { "状态监控面板", "其他 · HUD" },
\t\t\t{ "画面移除", "其他 · 画面移除" }, { "水印", "个人 · 外观" }, { "界面布局编辑器", "个人 · 外观" }, { "全局快捷键总览", "个人 · 外观" }
\t\t} ) add_manual( e.first, e.second );
\t}

\tvoid menu::close_search( )'''
if needle not in s: raise RuntimeError("missing search index insert anchor")
s=s.replace(needle,extras,1)

signature="\tvoid menu::activate_search_result( const std::size_t index )"
start=s.find(signature)
if start<0: raise RuntimeError("missing activate_search_result")
brace=s.find("{",start); depth=0; end=None
for i in range(brace,len(s)):
    if s[i]=="{": depth+=1
    elif s[i]=="}":
        depth-=1
        if depth==0:
            end=i+1
            break
replacement='''\tvoid menu::activate_search_result( const std::size_t index )
\t{
\t\tif ( index >= this->m_search_visible_indices.size( ) ) return;
\t\tconst auto& chosen = this->m_search_entries[ this->m_search_visible_indices[ index ] ];
\t\tconst auto& cat = chosen.category_lower;
\t\tauto has = [ & ]( std::string_view v ) { return cat.find( v ) != std::string::npos; };

\t\tif ( has( "anti aim" ) || has( "ANTI-AIM" ) || has( "反瞄准" ) ) { this->m_exact_page = 0; this->m_combat_tab = 2; }
\t\telse if ( has( "legitbot" ) || has( "LEGIT" ) ) { this->m_exact_page = 0; this->m_combat_tab = 1; }
\t\telse if ( has( "ragebot" ) || has( "RAGE" ) || has( "peek assistance" ) || has( "zeusbot" ) || has( "knifebot" ) ) { this->m_exact_page = 0; this->m_combat_tab = 0; }
\t\telse if ( has( "enemy" ) || has( "敌人" ) ) { this->m_exact_page = 3; this->m_subtab = 0; }
\t\telse if ( has( "allies" ) || has( "teammate" ) || has( "friendly" ) || has( "队友" ) ) { this->m_exact_page = 3; this->m_subtab = 1; }
\t\telse if ( has( "local" ) ) { this->m_exact_page = 3; this->m_subtab = 2; }
\t\telse if ( has( "skin" ) || has( "paint" ) || has( "sticker" ) || has( "changer" ) || has( "inventory" ) || has( "库存" ) || has( "knife" ) || has( "glove" ) || has( "agent" ) ) { this->m_exact_page = 5; }
\t\telse if ( has( "config" ) ) { this->m_exact_page = 8; }
\t\telse if ( has( "lua" ) || has( "script" ) ) { this->m_exact_page = 7; }
\t\telse if ( has( "profile" ) || has( "watermark" ) || has( "个人" ) || has( "外观" ) ) { this->m_exact_page = 9; this->m_profile_tab = 0; }
\t\telse
\t\t{
\t\t\tthis->m_exact_page = 6;
\t\t\tif ( has( "world" ) || has( "projectile" ) || has( "item" ) || has( "bomb" ) || has( "spectator" ) || has( "投掷物" ) || has( "战局信息" ) ) this->m_misc_section_open = 1;
\t\t\telse if ( has( "scene" ) || has( "weather" ) || has( "environment" ) || has( "环境与天气" ) ) this->m_misc_section_open = 2;
\t\t\telse if ( has( "camera" ) || has( "viewmodel" ) || has( "镜头" ) || has( "第一人称" ) ) this->m_misc_section_open = 3;
\t\t\telse if ( has( "hud" ) || has( "widgets" ) || has( "HUD" ) ) this->m_misc_section_open = 4;
\t\t\telse if ( has( "removal" ) || has( "画面移除" ) ) this->m_misc_section_open = 5;
\t\t\telse this->m_misc_section_open = 0;
\t\t}
\t\tthis->m_hotkey_overview_open = false;
\t\tthis->m_layout_editor_open = false;
\t\txui::set_highlight_target( chosen.name, 1.2f );
\t\tthis->close_search( );
\t}'''
if end is None: raise RuntimeError("unterminated activate_search_result")
s=s[:start]+replacement+s[end:]
s=s.replace("if ( this->m_search_visible_indices.size( ) >= 50 )","if ( this->m_search_visible_indices.size( ) >= 120 )",1)

old='''\t\t\t\tconst auto name_y = text_start_y;
\t\t\t\tdl.text( row.x + 8.0f, name_y, display_name, label_col );

\t\t\t\tconst auto sub_y = name_y + name_th + 3.0f;
\t\t\t\tdl.text( row.x + 8.0f, sub_y, display_category, sub_col.alpha( 170 ) );

\t\t\t\tif ( item.bind_key != 0 )'''
new='''\t\t\t\tconst auto name_y = text_start_y;
\t\t\t\tfloat reserved_right = 12.0f;
\t\t\t\tif ( item.bind_key != 0 ) { const auto key = xui::vk_name( item.bind_key ); reserved_right += xdraw::measure_text( key ).first + 30.0f; }
\t\t\t\tconst auto max_text_w = std::max( 40.0f, row.w - reserved_right - 16.0f );
\t\t\t\tdl.text( row.x + 8.0f, name_y, xui::truncate( display_name, max_text_w ), label_col );

\t\t\t\tconst auto sub_y = name_y + name_th + 3.0f;
\t\t\t\tdl.text( row.x + 8.0f, sub_y, xui::truncate( display_category, max_text_w ), sub_col.alpha( 170 ) );

\t\t\t\tif ( item.bind_key != 0 )'''
if old not in s: raise RuntimeError("missing search truncation anchor")
s=s.replace(old,new,1)
write(rel,s)

# 6) Clamp all user-positioned overlay widgets to the current viewport.
rel="project/core/rendering/impl/widgets.cpp"
s=read(rel)
pairs=[
(
'const auto x = ( right ? static_cast<float>( screen_w ) - w - margin : margin ) + wm.offset_x.value;\n\t\tconst auto y = ( bottom ? static_cast<float>( screen_h ) - h - margin : margin ) + wm.offset_y.value;',
'const auto base_x = ( right ? static_cast<float>( screen_w ) - w - margin : margin ) + wm.offset_x.value;\n\t\tconst auto base_y = ( bottom ? static_cast<float>( screen_h ) - h - margin : margin ) + wm.offset_y.value;\n\t\tconst auto x = std::clamp( base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - w ) );\n\t\tconst auto y = std::clamp( base_y, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - h ) );'
),
(
'const auto x = ( right ? static_cast<float>( screen_w ) - panel_w - margin : margin ) + cfg.offset_x.value;\n\t\tauto y = ( top ? margin : bottom ? static_cast<float>( screen_h ) - panel_h - margin : ( static_cast<float>( screen_h ) - panel_h ) * 0.5f ) + cfg.offset_y.value;',
'const auto base_x = ( right ? static_cast<float>( screen_w ) - panel_w - margin : margin ) + cfg.offset_x.value;\n\t\tauto base_y = ( top ? margin : bottom ? static_cast<float>( screen_h ) - panel_h - margin : ( static_cast<float>( screen_h ) - panel_h ) * 0.5f ) + cfg.offset_y.value;\n\t\tconst auto x = std::clamp( base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - panel_w ) );\n\t\tauto y = std::clamp( base_y, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - panel_h ) );'
),
(
'const auto target_base_y = ( top_side ? margin : bottom_side ? static_cast<float>( screen_h ) - margin - total_h : ( static_cast<float>( screen_h ) * 0.5f ) - ( total_h * 0.5f ) ) + kb_cfg.offset_y.value;',
'const auto unclamped_base_y = ( top_side ? margin : bottom_side ? static_cast<float>( screen_h ) - margin - total_h : ( static_cast<float>( screen_h ) * 0.5f ) - ( total_h * 0.5f ) ) + kb_cfg.offset_y.value;\n\t\tconst auto target_base_y = std::clamp( unclamped_base_y, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - total_h ) );'
),
(
'const auto header_x = ( right_side ? static_cast<float>( screen_w ) - margin - header_w : margin ) + kb_cfg.offset_x.value;',
'const auto header_base_x = ( right_side ? static_cast<float>( screen_w ) - margin - header_w : margin ) + kb_cfg.offset_x.value;\n\t\tconst auto header_x = std::clamp( header_base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - header_w ) );'
),
]
for old,new in pairs:
    if old not in s: raise RuntimeError("missing widget clamp anchor")
    s=s.replace(old,new,1)
old='const auto row_x = ( right_side ? static_cast<float>( screen_w ) - margin - row_w : margin ) + kb_cfg.offset_x.value;'
new='const auto row_base_x = ( right_side ? static_cast<float>( screen_w ) - margin - row_w : margin ) + kb_cfg.offset_x.value;\n\t\t\t\tconst auto row_x = std::clamp( row_base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - row_w ) );'
if s.count(old)<3: raise RuntimeError("missing keybind row clamp anchors")
s=s.replace(old,new)
write(rel,s)

replace_once(
    "project/core/features/misc/impl/impacts.cpp",
    'const auto x = ( on_right ? static_cast<float>( screen_w ) - w - margin : margin ) + cfg.stats_offset_x.value;\n\t\tconst auto y = ( on_bottom ? static_cast<float>( screen_h ) - h - margin : margin ) + cfg.stats_offset_y.value;',
    'const auto base_x = ( on_right ? static_cast<float>( screen_w ) - w - margin : margin ) + cfg.stats_offset_x.value;\n\t\tconst auto base_y = ( on_bottom ? static_cast<float>( screen_h ) - h - margin : margin ) + cfg.stats_offset_y.value;\n\t\tconst auto x = std::clamp( base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - w ) );\n\t\tconst auto y = std::clamp( base_y, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - h ) );',
    "stats clamp",
)

rel="project/core/features/esp/other/other.overlay.cpp"
s=read(rel)
repls=[
(
'const auto x = ( static_cast< float >( screen_w ) - total_w ) * 0.5f + other_cfg.bomb_offset_x.value;\n\t\tconst auto y = ( other_cfg.bomb_placement.value == settings::esp::other::bomb_position::bottom_center ? static_cast<float>( screen_h ) - h - top_offset : top_offset ) + other_cfg.bomb_offset_y.value;',
'const auto base_x = ( static_cast< float >( screen_w ) - total_w ) * 0.5f + other_cfg.bomb_offset_x.value;\n\t\tconst auto base_y = ( other_cfg.bomb_placement.value == settings::esp::other::bomb_position::bottom_center ? static_cast<float>( screen_h ) - h - top_offset : top_offset ) + other_cfg.bomb_offset_y.value;\n\t\tconst auto x = std::clamp( base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - total_w ) );\n\t\tconst auto y = std::clamp( base_y, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - h ) );'
),
(
'auto ry = ( top_side ? margin : bottom_side ? static_cast<float>( screen_h ) - margin - total_h : ( static_cast<float>( screen_h ) - total_h ) * 0.5f ) + other_cfg.spectator_offset_y.value;\n\t\tconst auto x = ( right_side ? static_cast<float>( screen_w ) - header_w - margin : margin ) + other_cfg.spectator_offset_x.value;',
'auto ry = ( top_side ? margin : bottom_side ? static_cast<float>( screen_h ) - margin - total_h : ( static_cast<float>( screen_h ) - total_h ) * 0.5f ) + other_cfg.spectator_offset_y.value;\n\t\try = std::clamp( ry, 0.0f, std::max( 0.0f, static_cast<float>( screen_h ) - total_h ) );\n\t\tconst auto header_base_x = ( right_side ? static_cast<float>( screen_w ) - header_w - margin : margin ) + other_cfg.spectator_offset_x.value;\n\t\tconst auto x = std::clamp( header_base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - header_w ) );'
),
]
for old,new in repls:
    if old not in s: raise RuntimeError("missing other overlay clamp anchor")
    s=s.replace(old,new,1)
old='const auto rx = ( right_side ? static_cast< float >( screen_w ) - row_w - margin : margin ) + other_cfg.spectator_offset_x.value;'
new='const auto row_base_x = ( right_side ? static_cast< float >( screen_w ) - row_w - margin : margin ) + other_cfg.spectator_offset_x.value;\n\t\t\tconst auto rx = std::clamp( row_base_x, 0.0f, std::max( 0.0f, static_cast<float>( screen_w ) - row_w ) );'
if old not in s: raise RuntimeError("missing spectator row clamp")
s=s.replace(old,new)
write(rel,s)

# 7) Simplified Chinese leftovers and font preference.
maps={
"project/external/xdraw/xui/xui.cpp":{
    "未綁定":"未绑定","上一頁":"上一页","下一頁":"下一页","清除綁定":"清除绑定",
    "切換":"切换","按住啟用":"按住启用","按住禁用":"按住禁用","數字鍵":"数字键",
    "上檔鍵":"Shift 键","大寫鎖定":"大写锁定","插入鍵":"插入键","刪除鍵":"删除键",
},
"project/core/scripting/lua_manager.cpp":{
    "導入":"导入","未知錯誤":"未知错误","開啟":"打开","載入":"加载",
    "複製":"复制","資料夾":"文件夹","腳本":"脚本","偵測":"检测",
},
}
for rel,mp in maps.items():
    text=read(rel)
    for a,b in mp.items(): text=text.replace(a,b)
    write(rel,text)

replace_once(
    "project/external/xdraw/xdraw.cpp",
'''\t\t\tconst std::array candidates{
\t\t\t\tfonts_dir / L"msjh.ttc",   // Microsoft JhengHei (Traditional Chinese)
\t\t\t\tfonts_dir / L"msjhbd.ttc",
\t\t\t\tfonts_dir / L"mingliu.ttc",
\t\t\t\tfonts_dir / L"msyh.ttc",
\t\t\t\tfonts_dir / L"simsun.ttc"
\t\t\t};''',
'''\t\t\tconst std::array candidates{
\t\t\t\tfonts_dir / L"msyh.ttc",    // Microsoft YaHei (Simplified Chinese)
\t\t\t\tfonts_dir / L"simsun.ttc", // SimSun (Simplified Chinese)
\t\t\t\tfonts_dir / L"msjh.ttc",    // Microsoft JhengHei fallback
\t\t\t\tfonts_dir / L"msjhbd.ttc",
\t\t\t\tfonts_dir / L"mingliu.ttc"
\t\t\t};''',
    "CJK font preference",
)

# Final postconditions.
checks={
    "inventory_member_state":"this->m_inventory_tools_open" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_no_checkbox_overlap":"same_line( ); xui::checkbox( \"紧凑卡片" not in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_adaptive_columns":"fit_columns" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "legit_responsive":"single_col = content_w < 760.0f" in read("project/core/rendering/impl/menu/menu.legitbot.cpp"),
    "misc_all_visible":"const bool open = this->m_misc_section_open" not in read("project/core/rendering/impl/menu/menu.exact.cpp"),
    "misc_scroll_route":"child_scroll_cache" in read("project/core/rendering/impl/menu/menu.exact.cpp"),
    "search_anti_aim_route":"this->m_combat_tab = 2" in read("project/core/rendering/impl/menu/menu.core.cpp"),
    "search_manual_controls":"最大视野角" in read("project/core/rendering/impl/menu/menu.core.cpp") and "界面布局编辑器" in read("project/core/rendering/impl/menu/menu.core.cpp"),
    "search_text_reserved":"reserved_right" in read("project/core/rendering/impl/menu/menu.core.cpp"),
    "sidebar_size_preserved":"delta_w = this->m_sidebar_compact" not in read("project/core/rendering/impl/menu/menu.core.cpp"),
    "overlay_clamp":"std::clamp( base_x" in read("project/core/rendering/impl/widgets.cpp"),
    "simplified_keybinds":"未綁定" not in read("project/external/xdraw/xui/xui.cpp") and "清除綁定" not in read("project/external/xdraw/xui/xui.cpp"),
    "simplified_font_priority":"msyh.ttc" in read("project/external/xdraw/xdraw.cpp") and read("project/external/xdraw/xdraw.cpp").find("msyh.ttc") < read("project/external/xdraw/xdraw.cpp").find("msjh.ttc"),
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("UI audit bugfix postcondition failed: "+", ".join(bad))

changed=[
    "project/core/rendering/rendering.hpp",
    "project/core/rendering/impl/menu/menu.skins.cpp",
    "project/core/rendering/impl/menu/menu.legitbot.cpp",
    "project/core/rendering/impl/menu/menu.exact.cpp",
    "project/core/rendering/impl/menu/menu.core.cpp",
    "project/core/rendering/impl/widgets.cpp",
    "project/core/features/misc/impl/impacts.cpp",
    "project/core/features/esp/other/other.overlay.cpp",
    "project/external/xdraw/xui/xui.cpp",
    "project/external/xdraw/xdraw.cpp",
    "project/core/scripting/lua_manager.cpp",
]
report={
    "name":"MCB UI audit bugfix 2026-09-29",
    "checks":checks,
    "files":{rel:hashlib.sha256((ROOT/rel).read_bytes()).hexdigest() for rel in changed},
    "runtime":"NOT_TESTED",
}
(ROOT/"MCB_UI_AUDIT_BUGFIX_20260929.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
