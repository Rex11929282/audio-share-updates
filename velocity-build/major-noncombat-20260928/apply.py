from pathlib import Path
import re, hashlib, json, sys

ROOT=Path(sys.argv[1]).resolve()

UNTOUCHED = [
    'project/core/rendering/impl/menu/menu.ragebot.cpp',
    'project/core/rendering/impl/menu/menu.legitbot.cpp',
    'project/core/rendering/impl/menu/menu.skins.cpp',
]
def sha(rel): return hashlib.sha256((ROOT/rel).read_bytes()).hexdigest()
untouched_before={rel:sha(rel) for rel in UNTOUCHED}

def read(rel): return (ROOT/rel).read_text(encoding='utf-8-sig')
def write(rel,s): (ROOT/rel).write_text(s,encoding='utf-8',newline='\n')

def replace_once(s, old, new, label):
    if old not in s: raise RuntimeError(f'missing {label}')
    return s.replace(old,new,1)

def replace_function(s, signature, replacement):
    pos=s.find(signature)
    if pos<0: raise RuntimeError('missing fn '+signature)
    brace=s.find('{',pos)
    depth=0;i=brace; instr=None; esc=False; line=False; block=False
    while i<len(s):
        c=s[i]; n=s[i+1] if i+1<len(s) else ''
        if line:
            if c=='\n': line=False
            i+=1; continue
        if block:
            if c=='*' and n=='/': block=False;i+=2;continue
            i+=1;continue
        if instr:
            if esc: esc=False
            elif c=='\\': esc=True
            elif c==instr: instr=None
            i+=1;continue
        if c=='/' and n=='/': line=True;i+=2;continue
        if c=='/' and n=='*': block=True;i+=2;continue
        if c in ('"',"'"): instr=c;i+=1;continue
        if c=='{': depth+=1
        elif c=='}':
            depth-=1
            if depth==0:
                return s[:pos]+replacement.rstrip()+s[i+1:]
        i+=1
    raise RuntimeError('unterminated '+signature)

rel='project/core/settings.hpp'; s=read(rel)
s=s.replace('enum class marker_type : int { classic, damage, both };','enum class marker_type : int { brackets, damage, both };')
s=s.replace('config::enm<marker_type> hit_marker_type{ marker_type::classic, "impacts", "hit marker type" };','config::enm<marker_type> hit_marker_type{ marker_type::brackets, "impacts", "hit marker type" };')
old='''\t\tstruct name_changer
\t\t{
\t\t\txui::setting clantag{ false, {}, "clantag", "name changer" };
\t\t\txui::setting override_name{ false, {}, "override name", "name changer" };
\t\t\tconfig::str name{ "Player", "name changer", "name" };
\t\t} m_name_changer{};'''
new='''\t\tstruct name_changer
\t\t{
\t\t\tenum class tag_style : int { static_tag, pulse };
\t\t\txui::setting clantag{ false, {}, "clantag", "name changer" };
\t\t\tconfig::str clantag_text{ "MCB", "name changer", "clantag text" };
\t\t\tconfig::enm<tag_style> clantag_style{ tag_style::static_tag, "name changer", "clantag style" };
\t\t\txui::setting override_name{ false, {}, "override name", "name changer" };
\t\t\tconfig::str name{ "Player", "name changer", "name" };
\t\t} m_name_changer{};'''
s=replace_once(s,old,new,'name_changer')
s=s.replace('\t\t\tenum class notice_style : int { compact, pill, glass, damage, game };\n','')
s=s.replace('\t\t\tconfig::enm<notice_style> hit_notice_style{ notice_style::glass, "profile", "hit notice style" };\n','')
write(rel,s)

rel='project/core/features/misc/impl/other.cpp'; s=read(rel)
old_block='''\t\tstd::string display_name = base_name;
\t\tif ( cfg.clantag.value )
\t\t{
\t\t\tconstexpr std::string_view tag{ "velocity" };
\t\t\tconstexpr auto ticks_per_step{ 16 }; // 0.25 seconds at CS2's 64-tick interval.
\t\t\tconstexpr auto phase_count{ static_cast<int>( tag.size( ) * 2 ) };

\t\t\tconst auto global_vars = memory::safe_read<std::uintptr_t>( addresses::globals::global_vars ).value_or( 0 );
\t\t\tconst auto current_tick = global_vars
\t\t\t\t? memory::safe_read<int>( global_vars + 0x44 ).value_or( 0 )
\t\t\t\t: 0;
\t\t\tauto phase = current_tick / ticks_per_step % phase_count;
\t\t\tif ( phase < 0 )
\t\t\t{
\t\t\t\tphase += phase_count;
\t\t\t}

\t\t\tconst auto reveal_index = phase <= static_cast<int>( tag.size( ) )
\t\t\t\t? phase
\t\t\t\t: phase_count - phase;
\t\t\tconst auto visible_tag = tag.substr( 0, static_cast<std::size_t>( reveal_index ) );
\t\t\tif ( !visible_tag.empty( ) )
\t\t\t{
\t\t\t\tdisplay_name.reserve( base_name.size( ) + visible_tag.size( ) + 3 );
\t\t\t\tdisplay_name = "[";
\t\t\t\tdisplay_name += visible_tag;
\t\t\t\tdisplay_name += "] ";
\t\t\t\tdisplay_name += base_name;
\t\t\t}
\t\t}'''
new_block='''\t\tstd::string display_name = base_name;
\t\tif ( cfg.clantag.value && !cfg.clantag_text.value.empty( ) )
\t\t{
\t\t\tbool show_tag = true;
\t\t\tif ( cfg.clantag_style.value == settings::misc::name_changer::tag_style::pulse )
\t\t\t{
\t\t\t\tconst auto global_vars = memory::safe_read<std::uintptr_t>( addresses::globals::global_vars ).value_or( 0 );
\t\t\t\tconst auto current_tick = global_vars ? memory::safe_read<int>( global_vars + 0x44 ).value_or( 0 ) : 0;
\t\t\t\tshow_tag = ( ( current_tick / 32 ) & 1 ) == 0;
\t\t\t}

\t\t\tif ( show_tag )
\t\t\t{
\t\t\t\tdisplay_name.reserve( base_name.size( ) + cfg.clantag_text.value.size( ) + 3 );
\t\t\t\tdisplay_name = "[";
\t\t\t\tdisplay_name += cfg.clantag_text.value;
\t\t\t\tdisplay_name += "] ";
\t\t\t\tdisplay_name += base_name;
\t\t\t}
\t\t}'''
s=replace_once(s,old_block,new_block,'legacy clantag block')
write(rel,s)

rel='project/core/features/misc/impl/impacts.cpp'; s=read(rel)
new_markers=r'''\tvoid impacts::render_hit_markers( xdraw::draw_list& draw_list, float time )
\t{
\t\tconst auto& cfg = settings::g_misc.m_impacts;
\t\tstd::unique_lock lock( this->m_mtx );

\t\tfor ( auto it = this->m_hitmarkers.begin( ); it != this->m_hitmarkers.end( ); )
\t\t{
\t\t\tconst auto duration = cfg.hit_marker_duration.value;
\t\t\tconst auto elapsed = time - it->time;
\t\t\tif ( elapsed > duration )
\t\t\t{
\t\t\t\tit = this->m_hitmarkers.erase( it );
\t\t\t\tcontinue;
\t\t\t}

\t\t\tconst auto screen = systems::g_view.project( it->position );
\t\t\tconst auto progress = std::clamp( elapsed / std::max( duration, 0.001f ), 0.0f, 1.0f );
\t\t\tconst auto fade = 1.0f - progress * progress;
\t\t\tconst auto alpha = static_cast<std::uint8_t>( fade * 255.0f );
\t\t\tconst auto color = xdraw::color{ cfg.hit_marker_color.value.r, cfg.hit_marker_color.value.g, cfg.hit_marker_color.value.b, alpha };
\t\t\tconst auto show_brackets = cfg.hit_marker_type == settings::misc::impacts::marker_type::brackets || cfg.hit_marker_type == settings::misc::impacts::marker_type::both;
\t\t\tconst auto show_damage = cfg.hit_marker_type == settings::misc::impacts::marker_type::damage || cfg.hit_marker_type == settings::misc::impacts::marker_type::both;

\t\t\tconst auto settle = 1.0f - std::pow( 1.0f - std::min( elapsed * 14.0f, 1.0f ), 3.0f );
\t\t\tconst auto radius = 14.0f - 4.0f * settle;
\t\t\tconst auto arm = 4.5f;
\t\t\tconst auto thickness = 1.35f;
\t\t\tconst auto x = screen.x;
\t\t\tconst auto y = screen.y;

\t\t\tauto draw_brackets = [ & ]( xdraw::draw_list& target, xdraw::color col, float thick )
\t\t\t{
\t\t\t\ttarget.line( x - radius, y - radius, x - radius + arm, y - radius, col, thick );
\t\t\t\ttarget.line( x - radius, y - radius, x - radius, y - radius + arm, col, thick );
\t\t\t\ttarget.line( x + radius - arm, y - radius, x + radius, y - radius, col, thick );
\t\t\t\ttarget.line( x + radius, y - radius, x + radius, y - radius + arm, col, thick );
\t\t\t\ttarget.line( x - radius, y + radius, x - radius + arm, y + radius, col, thick );
\t\t\t\ttarget.line( x - radius, y + radius - arm, x - radius, y + radius, col, thick );
\t\t\t\ttarget.line( x + radius - arm, y + radius, x + radius, y + radius, col, thick );
\t\t\t\ttarget.line( x + radius, y + radius - arm, x + radius, y + radius, col, thick );
\t\t\t};

\t\t\tstd::string damage_text;
\t\t\tfloat damage_x{};
\t\t\tfloat damage_y{};
\t\t\tif ( show_damage )
\t\t\t{
\t\t\t\tdamage_text = std::to_string( it->damage );
\t\t\t\tconst auto [tw, th] = xdraw::measure_text( damage_text );
\t\t\t\tdamage_x = x - tw * 0.5f;
\t\t\t\tdamage_y = y - radius - th - 5.0f - progress * 8.0f;
\t\t\t}

\t\t\tif ( cfg.hit_marker_glow.value && alpha > 0 )
\t\t\t{
\t\t\t\tauto& glow = xdraw::get_glow( );
\t\t\t\tconst auto ga = static_cast<std::uint8_t>( static_cast<float>( alpha ) * std::clamp( cfg.hit_marker_glow_strength.value, 0.0f, 1.0f ) );
\t\t\t\tconst auto gc = xdraw::color{ color.r, color.g, color.b, ga };
\t\t\t\tif ( show_brackets ) draw_brackets( glow, gc, 3.2f );
\t\t\t\tif ( show_damage ) glow.text( damage_x, damage_y, damage_text, gc );
\t\t\t}

\t\t\tif ( show_brackets ) draw_brackets( draw_list, color, thickness );
\t\t\tif ( show_damage ) draw_list.text( damage_x, damage_y, damage_text, color );
\t\t\t++it;
\t\t}
\t}'''
new_markers=new_markers.replace('\\t','\t')
s=replace_function(s,'\tvoid impacts::render_hit_markers( xdraw::draw_list& draw_list, float time )',new_markers)
new_logs=r'''\tvoid impacts::render_logs( xdraw::draw_list& draw_list, float time )
\t{
\t\tstd::unique_lock lock( this->m_mtx );
\t\tconst auto& s = xui::ctx( ).style;

\t\tconstexpr auto fade_ratio{ 0.78f };
\t\tconstexpr auto row_w{ 352.0f };
\t\tconstexpr auto row_h{ 42.0f };
\t\tconstexpr auto row_gap{ 6.0f };
\t\tconstexpr auto base_x{ 18.0f };
\t\tconstexpr auto base_y{ 20.0f };
\t\tconstexpr auto stripe_w{ 3.0f };
\t\tconstexpr auto pad_x{ 12.0f };
\t\tconstexpr auto value_pad{ 14.0f };

\t\tconst auto miss_color = xdraw::color{ 255, 104, 104, 255 };
\t\tauto y_offset = 0.0f;

\t\tfor ( auto it = this->m_logs.begin( ); it != this->m_logs.end( ); )
\t\t{
\t\t\tconst auto elapsed = time - it->time;
\t\t\tconst auto duration = it->duration;
\t\t\tconst auto fade_start = duration * fade_ratio;
\t\t\tif ( elapsed > duration && it->alpha.finished( ) )
\t\t\t{
\t\t\t\tit = this->m_logs.erase( it );
\t\t\t\tcontinue;
\t\t\t}
\t\t\tif ( elapsed > fade_start && it->alpha.alpha( ) > 0.5f ) it->alpha.fade_out( 0.5f );
\t\t\tit->offset.update( );
\t\t\tit->alpha.update( );
\t\t\tif ( !it->snapped && it->offset.settled( ) ) { it->offset.snap( 0.0f ); it->snapped = true; }

\t\t\tconst auto alpha = it->alpha.alpha( );
\t\t\tif ( alpha > 0.01f )
\t\t\t{
\t\t\t\tauto scale = [ & ]( xdraw::color c ) { c.a = static_cast<std::uint8_t>( static_cast<float>( c.a ) * alpha ); return c; };
\t\t\t\tconst auto accent = scale( it->is_miss ? miss_color : s.accent );
\t\t\t\tconst auto primary = scale( s.text );
\t\t\t\tconst auto secondary = scale( s.text_dim );
\t\t\t\tauto bg = scale( xdraw::color{ 11, 14, 20, 228 } );
\t\t\t\tauto rule = scale( xdraw::color{ 255, 255, 255, 22 } );

\t\t\t\tconst auto x = base_x + ( it->snapped ? 0.0f : it->offset.value( ) );
\t\t\t\tconst auto y = base_y + y_offset;
\t\t\t\tdraw_list.rect_filled( x, y, row_w, row_h, bg, xdraw::corner_radius{ 4.0f } );
\t\t\t\tdraw_list.rect_filled( x, y, stripe_w, row_h, accent, xdraw::corner_radius{ 2.0f } );
\t\t\t\tdraw_list.line( x + 68.0f, y + 7.0f, x + 68.0f, y + row_h - 7.0f, rule, 1.0f );

\t\t\t\tconst std::string status = it->is_miss ? "失誤" : "命中";
\t\t\t\tconst std::string target = it->name.empty( ) ? "目標" : it->name;
\t\t\t\tstd::string detail;
\t\t\t\tstd::string value;
\t\t\t\tif ( it->is_miss )
\t\t\t\t{
\t\t\t\t\tdetail = it->reason.empty( ) ? "未確認原因" : it->reason;
\t\t\t\t\tif ( !it->hitgroup.empty( ) ) detail += " · " + it->hitgroup;
\t\t\t\t\tvalue = "未中";
\t\t\t\t}
\t\t\t\telse
\t\t\t\t{
\t\t\t\t\tdetail = it->hitgroup.empty( ) ? "命中確認" : it->hitgroup;
\t\t\t\t\tdetail += std::format( " · 剩餘 {}", it->health );
\t\t\t\t\tvalue = std::format( "-{}", it->damage );
\t\t\t\t}

\t\t\t\tconst auto [value_w, value_h] = xdraw::measure_text( value );
\t\t\t\tdraw_list.text( x + pad_x, y + 8.0f, status, accent );
\t\t\t\tdraw_list.text( x + 78.0f, y + 5.0f, target, primary );
\t\t\t\tdraw_list.text( x + 78.0f, y + 23.0f, detail, secondary );
\t\t\t\tdraw_list.text( x + row_w - value_pad - value_w, y + ( row_h - value_h ) * 0.5f, value, accent );

\t\t\t\ty_offset += row_h + row_gap;
\t\t\t}
\t\t\t++it;
\t\t}
\t}'''
new_logs=new_logs.replace('\\t','\t')
s=replace_function(s,'\tvoid impacts::render_logs( xdraw::draw_list& draw_list, float time )',new_logs)
s=s.replace('void chat_print_velocity( const char* msg )','void chat_print_mcb( const char* msg )')
s=s.replace('detail::chat_print_velocity( chat_msg.c_str( ) );','detail::chat_print_mcb( chat_msg.c_str( ) );')
s=s.replace('const auto root = std::wstring( app_data ) + L"\\\\velocity";','const auto root = std::wstring( app_data ) + L"\\\\MCB";')
write(rel,s)

rel='project/core/rendering/impl/menu/menu.misc.cpp'; s=read(rel)
s=s.replace('constexpr const char* marker_types[ ]{ "十字", "傷害數字", "兩者" };','constexpr const char* marker_types[ ]{ "角標", "傷害數字", "角標 + 數字" };')
s=s.replace('constexpr const char* hat_types[ ]{ "斗笠", "漁夫帽" };','constexpr const char* hat_types[ ]{ "斗笠", "漁夫帽" };\n\t\tconstexpr const char* tag_styles[ ]{ "固定", "脈衝" };')
s=s.replace('if ( xui::begin_child( "##misc_impacts", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "hit logs", impacts.hit_log );','if ( xui::begin_child( "##misc_impacts", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "戰術回饋", tokens::col_text );\n\t\t\t\txui::text( "命中與失誤使用固定資訊列，不再使用舊卡片提示", tokens::col_text_dim );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "命中資訊列##hit_logs", impacts.hit_log );')
s=s.replace('xui::checkbox( "console logs", impacts.console_log );\n\t\t\t\txui::checkbox( "chat logs", impacts.chat_log );\n\n\t\t\t\txui::checkbox( "miss logs", impacts.miss_log );','xui::checkbox( "未命中資訊列##miss_logs", impacts.miss_log );')
s=s.replace('\n\t\t\t\txui::checkbox( "miss logs", impacts.miss_log );\n','\n')
anchor='''\t\t\t\tif ( xui::begin_popup( "##misslog_popup", 220.0f ) )
\t\t\t\t{
\t\t\t\t\txui::slider_float( "duration##ml", impacts.miss_log_duration, 0.5f, 10.0f, "%.1f 秒" );
\t\t\t\t\txui::end_popup( );
\t\t\t\t}
'''
insert=anchor+'''
\t\t\t\txui::layout::separator( );
\t\t\t\txui::text( "外部輸出", tokens::col_text );
\t\t\t\txui::checkbox( "控制台紀錄##feedback_console", impacts.console_log );
\t\t\t\txui::checkbox( "聊天紀錄##feedback_chat", impacts.chat_log );
\t\t\t\txui::layout::separator( );
\t\t\t\txui::text( "命中回饋", tokens::col_text );
'''
s=replace_once(s,anchor,insert,'miss popup')
s=s.replace('xui::checkbox( "hit sound", impacts.hit_sound );','xui::checkbox( "命中音效##hit_sound_new", impacts.hit_sound );',1)
s=s.replace('xui::checkbox( "hit marker", impacts.hit_marker );','xui::checkbox( "命中角標##hit_marker_new", impacts.hit_marker );',1)
s=s.replace('xui::checkbox( "hit effect", impacts.hit_effect );','xui::checkbox( "畫面脈衝##hit_effect_new", impacts.hit_effect );',1)
s=s.replace('xui::checkbox( "death sound", impacts.death_sound );','xui::checkbox( "擊殺音效##death_sound_new", impacts.death_sound );',1)
s=s.replace('xui::checkbox( "death effect", impacts.death_effect );','xui::checkbox( "擊殺畫面回饋##death_effect_new", impacts.death_effect );',1)
s=s.replace('if ( xui::begin_child( "##misc_visuals", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "projectile trajectory", traj.enabled );','if ( xui::begin_child( "##misc_visuals", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "輔助視覺", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "projectile trajectory", traj.enabled );')
s=s.replace('if ( xui::begin_child( "##misc_movement", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "bhop", mov.bhop );','if ( xui::begin_child( "##misc_movement", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "移動", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "bhop", mov.bhop );')
start=s.find('\t\t\tif ( xui::begin_child( "##misc_other", col_w ) )')
if start<0: raise RuntimeError('misc_other start')
brace=s.find('{',start); depth=0;i=brace
while i<len(s):
    if s[i]=='{': depth+=1
    elif s[i]=='}':
        depth-=1
        if depth==0: break
    i+=1
new_other=r'''\t\t\tif ( xui::begin_child( "##misc_other", col_w ) )
\t\t\t{
\t\t\t\txui::text( "系統", tokens::col_text );
\t\t\t\txui::checkbox( "雷達揭示##misc_radar", m.reveal_radar );
\t\t\t\txui::checkbox( "保留擊殺資訊##misc_killfeed", m.preserve_killfeed );
\t\t\t\txui::checkbox( "關閉遊戲紀錄##misc_logs", m.disable_game_logs );
\t\t\t\txui::checkbox( "自動購買##misc_autobuy", ab.enabled );
\t\t\t\tif ( xui::begin_popup( "##autobuy_popup", 220.0f ) )
\t\t\t\t{
\t\t\t\t\txui::combo( "主武器##ab", ab.primary_weapon, detail::primary_weapons, 6 );
\t\t\t\t\txui::combo( "副武器##ab", ab.secondary_weapon, detail::secondary_weapons, 5 );
\t\t\t\t\txui::checkbox( "護甲##ab", ab.armor );
\t\t\t\t\txui::checkbox( "拆彈器##ab", ab.defuser );
\t\t\t\t\txui::checkbox( "電擊槍##ab", ab.taser );
\t\t\t\t\txui::multicombo( "投擲物##ab", ab.grenades, detail::grenade_names, 5 );
\t\t\t\t\txui::end_popup( );
\t\t\t\t}

\t\t\t\txui::layout::separator( );
\t\t\t\txui::text( "品牌識別", tokens::col_text );
\t\t\t\txui::checkbox( "隊伍標籤##identity_tag", m.m_name_changer.clantag );
\t\t\t\tif ( xui::begin_popup( "##identity_tag_popup", 244.0f ) )
\t\t\t\t{
\t\t\t\t\txui::text_input( "標籤文字##identity_tag_text", m.m_name_changer.clantag_text.value, 24, "MCB" );
\t\t\t\t\txui::combo( "呈現方式##identity_tag_style", m.m_name_changer.clantag_style.value, detail::tag_styles, 2 );
\t\t\t\t\txui::text( "預設以 [標籤] 名稱 顯示", tokens::col_text_dim );
\t\t\t\t\txui::end_popup( );
\t\t\t\t}
\t\t\t\txui::checkbox( "自訂顯示名稱##identity_name", m.m_name_changer.override_name );
\t\t\t\tif ( xui::begin_popup( "##override_name_popup", 244.0f ) )
\t\t\t\t{
\t\t\t\t\txui::text_input( "名稱##nc", m.m_name_changer.name.value, 32, "輸入名稱…" );
\t\t\t\t\txui::end_popup( );
\t\t\t\t}
\t\t\t\txui::text( "水印改由左下角人物 → 外觀設定管理", tokens::col_text_dim );
\t\t\t\txui::end_child( );
\t\t\t}'''
new_other=new_other.replace('\\t','\t')
s=s[:start]+new_other+s[i+1:]
s=s.replace('if ( xui::begin_child( "##misc_removals", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "remove crosshair", rem.crosshair );','if ( xui::begin_child( "##misc_removals", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "畫面移除", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "remove crosshair", rem.crosshair );')
s=s.replace('if ( xui::begin_child( "##misc_camera", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "custom fov", cam.change_fov );','if ( xui::begin_child( "##misc_camera", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "鏡頭", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "custom fov", cam.change_fov );')
s=s.replace('if ( xui::begin_child( "##misc_viewmodel", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "viewmodel adjust", vm.enabled );','if ( xui::begin_child( "##misc_viewmodel", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "第一人稱模型", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "viewmodel adjust", vm.enabled );')
s=s.replace('if ( xui::begin_child( "##misc_hud", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "crosshair overlay", hud.m_crosshair.enabled );','if ( xui::begin_child( "##misc_hud", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "畫面疊加", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "crosshair overlay", hud.m_crosshair.enabled );')
s=s.replace('if ( xui::begin_child( "##misc_hud_hat", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "hat", hud.m_hat.enabled );','if ( xui::begin_child( "##misc_hud_hat", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "模型裝飾", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "hat", hud.m_hat.enabled );')
new_env=r'''    void menu::draw_environment_page( float group_w ) const
    {
        (void)group_w;
        auto* win=xui::layout::current_window();if(!win)return;
        auto& w=settings::g_world;
        const auto wx=win->bounds.x,wy=win->bounds.y;
        const auto cx=wx+tokens::gap,cy=wy+tokens::gap-win->scroll_y;
        const auto cw=win->bounds.w-tokens::gap*2.0f;
        const auto col=(cw-tokens::gap)*0.5f;

        xui::layout::set_cursor(cx-wx,cy-wy);
        if(xui::begin_child("##env_scene",col))
        {
            auto& sc=w.m_scene;
            xui::text("場景",tokens::col_text);xui::layout::separator();
            xui::checkbox("天空材質",sc.skybox.custom_skybox);
            if(xui::begin_popup("##env_sky",240.0f)){const auto& boxes=features::world::g_scene.get_skyboxes();if(!boxes.empty()){std::vector<const char*> names;for(const auto& b:boxes)names.push_back(b.display_name.c_str());sc.skybox.selected_skybox.value=std::clamp(sc.skybox.selected_skybox.value,0,static_cast<int>(boxes.size())-1);xui::combo("天空##env_sel",sc.skybox.selected_skybox.value,names.data(),static_cast<int>(names.size()));}xui::end_popup();}
            xui::checkbox("天空顏色",sc.skybox.custom_color);if(xui::begin_popup("##env_skycolor",220.0f)){xui::color_picker("天空##env_sc",sc.skybox.skybox_color);xui::color_picker("雲層##env_cc",sc.skybox.cloud_color);xui::color_picker("太陽##env_sun",sc.skybox.sun_color);xui::end_popup();}
            xui::checkbox("世界顏色",sc.world_setting);if(xui::begin_popup("##env_world",220.0f)){xui::color_picker("顏色##env_wc",sc.world_color);xui::end_popup();}
            xui::checkbox("環境光",sc.lighting);if(xui::begin_popup("##env_light",220.0f)){xui::slider_float("強度##env_li",sc.lighting_intensity,0.0f,2.0f,"%.2f");xui::color_picker("顏色##env_lc",sc.lighting_color);xui::end_popup();}
            xui::checkbox("泛光",sc.bloom);if(xui::begin_popup("##env_bloom",220.0f)){xui::slider_float("強度##env_bi",sc.bloom_value,0.0f,8.0f,"%.2f");xui::end_popup();}
            xui::checkbox("伽瑪",sc.gamma);if(xui::begin_popup("##env_gamma",220.0f)){xui::slider_float("數值##env_gv",sc.gamma_value,1.0f,4.0f,"%.2f");xui::end_popup();}
            xui::checkbox("景深",sc.dof);if(xui::begin_popup("##env_dof",240.0f)){xui::slider_float("近景模糊##dof1",sc.dof_near_blurry,0.0f,50.0f,"%.0f");xui::slider_float("近景清晰##dof2",sc.dof_near_crisp,0.0f,100.0f,"%.0f");xui::slider_float("遠景清晰##dof3",sc.dof_far_crisp,100.0f,2000.0f,"%.0f");xui::slider_float("遠景模糊##dof4",sc.dof_far_blurry,200.0f,5000.0f,"%.0f");xui::end_popup();}
            xui::end_child();
        }

        xui::layout::set_cursor(cx-wx+col+tokens::gap,cy-wy);
        if(xui::begin_child("##env_weather",col))
        {
            auto& wt=w.m_weather;
            xui::text("大氣",tokens::col_text);xui::layout::separator();
            xui::checkbox("天氣效果",wt.enabled);if(xui::begin_popup("##env_weather_popup",220.0f)){static constexpr const char* types[]{"雪","雨","星空"};xui::combo("類型##env_wt",wt.type.value,types,3);xui::color_picker("顏色##env_wcol",wt.color);xui::end_popup();}
            xui::checkbox("霧效",wt.fog_enabled);if(xui::begin_popup("##env_fog",240.0f)){xui::slider_float("密度##env_fd",wt.fog_density,0.0f,1.0f,"%.2f");xui::slider_float("各向異性##env_fa",wt.fog_anisotropy,0.0f,1.0f,"%.2f");xui::slider_float("距離##env_fdist",wt.fog_draw_distance,500.0f,20000.0f,"%.0f");xui::color_picker("顏色##env_fc",wt.fog_color);xui::end_popup();}
            xui::checkbox("濕潤效果",wt.wetness);if(xui::begin_popup("##env_wet",220.0f)){xui::slider_float("密度##env_wd",wt.wetness_density,0.0f,5.0f,"%.1f");xui::slider_float("速度##env_ws",wt.wetness_speed,0.0f,3.0f,"%.1f");xui::end_popup();}
            xui::checkbox("風場",wt.wind);if(xui::begin_popup("##env_wind",240.0f)){xui::slider_float("強度##env_wstr",wt.wind_strength,0.0f,5.0f,"%.1f");xui::slider_float("方向##env_wdir",wt.wind_direction,0.0f,360.0f,"%.0f°");xui::slider_float("亂流##env_wtur",wt.wind_turbulence,0.0f,5.0f,"%.1f");xui::end_popup();}
            xui::text("環境參數現在全部在同一頁，不再藏在舊世界頁",tokens::col_text_dim);
            xui::end_child();
        }
    }'''
s=replace_function(s,'    void menu::draw_environment_page( float group_w ) const',new_env)
write(rel,s)

rel='project/core/rendering/impl/menu/menu.player.cpp'; s=read(rel)
s=s.replace('if ( xui::begin_child( "##player_esp", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "esp overlay", ov.enabled );','if ( xui::begin_child( "##player_esp", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "資訊覆蓋", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "esp overlay", ov.enabled );')
s=s.replace('if (xui::begin_child ("##player_glow", col_w)) {\n\t\t\t\t\txui::checkbox ("glow", glow.enabled);','if (xui::begin_child ("##player_glow", col_w)) {\n\t\t\t\t\txui::text( "發光效果", tokens::col_text );\n\t\t\t\t\txui::layout::separator( );\n\t\t\t\t\txui::checkbox ("glow", glow.enabled);')
s=s.replace('if ( xui::begin_child( "##local_chams_glow", col_w ) )\n\t\t\t{\n\t\t\t\tdetail::draw_chams_config( "chams", "local_main", p.m_chams.local );','if ( xui::begin_child( "##local_chams_glow", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "本機角色", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tdetail::draw_chams_config( "chams", "local_main", p.m_chams.local );')
s=s.replace('if ( xui::begin_child( "##player_chams", col_w ) )\n\t\t\t{\n\t\t\t\tdetail::draw_chams_config( "chams", "main", chams );','if ( xui::begin_child( "##player_chams", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "材質透視", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tdetail::draw_chams_config( "chams", "main", chams );')
s=s.replace('if ( xui::begin_child( "##viewmodel", col_w ) )\n\t\t\t{\n\t\t\t\tdetail::draw_chams_config( "weapon chams", "vm_weapon", esp.m_viewmodel.weapon );','if ( xui::begin_child( "##viewmodel", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "第一人稱材質", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tdetail::draw_chams_config( "weapon chams", "vm_weapon", esp.m_viewmodel.weapon );')
write(rel,s)

rel='project/core/rendering/impl/menu/menu.world.cpp'; s=read(rel)
s=s.replace('if ( xui::begin_child( "##esp_items", col_w ) )\n\t\t\t{\n\t\t\t\tstatic int item_group{};','if ( xui::begin_child( "##esp_items", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "物品資訊", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tstatic int item_group{};')
s=s.replace('if ( xui::begin_child( "##esp_projectiles", col_w ) )\n\t\t\t{\n\t\t\t\tstatic auto proj_group{ 0 };','if ( xui::begin_child( "##esp_projectiles", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "投擲物資訊", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tstatic auto proj_group{ 0 };')
s=s.replace('if ( xui::begin_child( "##esp_other", col_w ) )\n\t\t\t{\n\t\t\t\txui::checkbox( "bomb timer", other.bomb_timer );','if ( xui::begin_child( "##esp_other", col_w ) )\n\t\t\t{\n\t\t\t\txui::text( "賽局資訊", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::checkbox( "bomb timer", other.bomb_timer );')
write(rel,s)

rel='project/core/rendering/impl/menu/menu.exact.cpp'; s=read(rel)
s=s.replace('xui::checkbox("顯示水印##profile_watermark",wm.enabled);','xui::checkbox("顯示水印##profile_watermark",wm.enabled);\n            xui::text("戰術提示與命中回饋請到「其他 → 雜項」調整",tokens::col_text_dim);')
write(rel,s)

rel='project/core/rendering/impl/menu/menu.config.cpp'; s=read(rel)
s=s.replace('xui::text("腳本",tokens::col_text);const auto statuses=scripting::g_lua.script_statuses();','xui::text("腳本管理",tokens::col_text);xui::text("載入狀態與錯誤集中顯示",tokens::col_text_dim);xui::layout::separator();const auto statuses=scripting::g_lua.script_statuses();',1)
s=s.replace('if(!xui::begin_child("##cfg_panel",panel_w,panel_h,false))return;\n        xui::text_input','if(!xui::begin_child("##cfg_panel",panel_w,panel_h,false))return;\n        xui::text("設定檔",tokens::col_text);xui::text("設定檔來源：本機 MCB 資料夾",tokens::col_text_dim);xui::layout::separator();\n        xui::text_input')
write(rel,s)

checks={
 'old_notice_enum_removed': 'notice_style' not in read('project/core/settings.hpp') and 'hit_notice_style' not in read('project/core/features/misc/impl/impacts.cpp'),
 'legacy_velocity_tag_removed': 'constexpr std::string_view tag{ "velocity" }' not in read('project/core/features/misc/impl/other.cpp'),
 'new_tag_controls': 'clantag_text' in read('project/core/settings.hpp') and 'tag_styles' in read('project/core/rendering/impl/menu/menu.misc.cpp'),
 'new_feed': '戰術回饋' in read('project/core/rendering/impl/menu/menu.misc.cpp') and '未中' in read('project/core/features/misc/impl/impacts.cpp'),
 'new_marker': 'marker_type::brackets' in read('project/core/features/misc/impl/impacts.cpp'),
 'environment_full': all(x in read('project/core/rendering/impl/menu/menu.misc.cpp') for x in ('霧效','濕潤效果','風場','景深')),
}
checks['battle_inventory_files_untouched']=all(sha(rel)==untouched_before[rel] for rel in UNTOUCHED)
print(json.dumps(checks,ensure_ascii=False,indent=2))
if not all(checks.values()): raise SystemExit(2)
report={'name':'MCB non-combat major UI 2026-09-28','scope':'all non-combat UI; combat/inventory source files untouched','checks':checks,'untouched':UNTOUCHED}
(ROOT/'MCB_MAJOR_NONCOMBAT_20260928.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
