from pathlib import Path
import json, re, shutil, sys

root = Path(sys.argv[1]).resolve()
v = root / 'cs2' / 'MCB-CS2'
patchdir = Path(__file__).resolve().parent
exact = v / 'project/core/rendering/impl/menu/menu.exact.cpp'
core = v / 'project/core/rendering/impl/menu/menu.core.cpp'
hpp = v / 'project/core/rendering/rendering.hpp'

def replace_once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError('Expected exactly one anchor: ' + old[:110])
    return text.replace(old, new, 1)

def replace_function(text, signature, replacement):
    a = text.index(signature); start = text.index('{', a)
    i = start; depth = 0; mode = 'code'; quote = ''
    while i < len(text):
        c = text[i]; nxt = text[i+1:i+2]
        if mode == 'line':
            if c == '\n': mode = 'code'
        elif mode == 'block':
            if c == '*' and nxt == '/': mode = 'code'; i += 1
        elif mode == 'string':
            if c == '\\': i += 1
            elif c == quote: mode = 'code'
        else:
            if c == '/' and nxt == '/': mode='line'; i+=1
            elif c == '/' and nxt == '*': mode='block'; i+=1
            elif c in ('"', "'"): mode='string'; quote=c
            elif c == '{': depth+=1
            elif c == '}':
                depth-=1
                if depth == 0:
                    new = replacement(text[a:i+1]) if callable(replacement) else replacement
                    return text[:a] + new + text[i+1:]
        i+=1
    raise RuntimeError('Unclosed function: '+signature)

if not exact.is_file(): raise SystemExit('v6.1 patched native source required')
t = exact.read_text(encoding='utf-8')
if 'ui_views_v62.inl' in t: raise SystemExit('v6.2 patch already applied; use a clean source tree')
t = replace_once(t, '#include <pch/pch.hpp>', '#include <pch/pch.hpp>\n#include <core/rendering/ui_state_v62.hpp>\n#include <commdlg.h>\n#pragma comment(lib, "Comdlg32.lib")')
namespace = 'namespace rendering\n{\n'
t = replace_once(t,namespace,namespace+'    namespace exact_panel_v62 { bool sanitize(const nlohmann::json&, nlohmann::json&, std::string&); }\n')
t = replace_once(t,'            bool dirty{};','            bool dirty{};\n            std::string load_error{};')
t = replace_function(t,'void load_state( )',r'''void load_state( )
        {
            auto& s=values(); if(s.loaded)return; s.loaded=true; s.disk=json::object();
            try {
                std::error_code ec; const auto p=state_path();
                if(!std::filesystem::exists(p,ec) && !ec)return;
                json candidate, normalized;
                if(mcb_ui_v62::read_json(p,candidate,s.load_error) && exact_panel_v62::sanitize(candidate,normalized,s.load_error))
                    s.disk=std::move(normalized);
            } catch(const std::exception& e) {s.load_error=e.what();}
        }''')
t = replace_function(t,'void serialize_state( json& out )',lambda f:replace_once(f,'out = json::object( );','out = s.disk.is_object( ) ? s.disk : json::object( );'))
t = replace_once(t,'    namespace exact_panel_v61','    #include <core/rendering/ui_views_v62.inl>\n\n    namespace exact_panel_v61')
t = replace_function(t,'void menu::save_exact_panel_state( )','void menu::save_exact_panel_state( )\n    { exact_panel_bridge::save_active(); }')
t = t.replace('exact_panel_v61::draw_configs( content_w, content_h );','exact_panel_v62::draw_configs( content_w, content_h );')
t = t.replace('exact_panel_v61::draw_scripts( content_w, content_h );','exact_panel_v62::draw_scripts( content_w, content_h );')
t = replace_function(t,'void draw_group( std::string_view page_id',lambda f:replace_once(replace_once(f,'                bool changed{};','                xui::push_id(key);\n                bool changed{};'),'                if ( changed ) mark_dirty( );','                if ( changed ) mark_dirty( );\n                xui::pop_id();'))
t = replace_once(t,'        xui::layout::set_cursor( x0, y );\n        xui::layout::spacing( 1.0f );','        xui::layout::set_cursor( x0, y );\n        if(y == start.second) xui::text("No controls match your search", tokens::col_text_dim);\n        xui::layout::spacing( 1.0f );')
exact.write_text(t,encoding='utf-8')
for filename in ('ui_state_v62.hpp','ui_views_v62.inl'):
    shutil.copyfile(patchdir/filename,v/'project/core/rendering'/filename)

h=hpp.read_text(encoding='utf-8')
marker='\tnamespace exact_panel_bridge\n\t{\n'
h=replace_once(h,marker,marker+'\t\tvoid save_active();\n\t\tbool has_unsaved();\n\t\tvoid config_selector();\n\t\tvoid draw_feedback(float x,float y,float w,float h);\n')
hpp.write_text(h,encoding='utf-8')

c=core.read_text(encoding='utf-8')
c=replace_function(c,'void menu::draw_top_bar( float w )',r'''void menu::draw_top_bar( float w )
    {
        auto& dl=xui::draw::current(); const auto sw=tokens::sidebar_w;
        dl.rect_filled(this->m_x+sw,this->m_y,w,66.0f,tokens::col_elevated.alpha(220),xdraw::corner_radius{6.0f});
        dl.line(this->m_x+sw,this->m_y+66,this->m_x+this->m_w,this->m_y+66,xdraw::color{20,37,54,255});
        xui::layout::set_cursor(sw+12.0f,18.0f);
        if(this->m_search_open) {
            if(auto* win=xui::layout::current_window()) {
                const auto old=win->bounds; win->bounds.w=this->m_w-84.0f;
                xui::text_input("##v62_menu_search",this->m_search_query,96,"Search current page...");win->bounds=old;
            }
            xui::layout::set_cursor(this->m_w-72.0f,18.0f);
            if(xui::button("Close",60,28)){this->m_search_open=false;this->m_search_query.clear();}
            return;
        }
        if(xui::button(exact_panel_bridge::has_unsaved()?"Save *##v62_save":"Save##v62_save",62,28))this->save_exact_panel_state();
        xui::layout::same_line();exact_panel_bridge::config_selector();
        xui::layout::same_line();if(xui::button("Search",68,28)){this->m_search_open=true;this->m_search_query.clear();}
        xui::layout::same_line();if(xui::button("Dim",40,28))this->m_dim_interface=!this->m_dim_interface;
        xui::layout::same_line();if(xui::button("Settings",76,28)){this->m_exact_page=9;this->m_search_query.clear();}
        xui::layout::same_line();if(xui::button(this->m_sidebar_compact?">>##v62_sidebar":"<<##v62_sidebar",42,28))this->m_sidebar_compact=!this->m_sidebar_compact;
    }''')
marker='\t\tthis->draw_exact_page( );'
c=replace_once(c,marker,marker+'\n\t\texact_panel_bridge::draw_feedback(this->m_x,this->m_y,this->m_w,this->m_h);')
c=replace_once(c,'\t\tthis->sync_theme_style( );','\t\tthis->apply_theme_preset(std::clamp(exact_panel_bridge::choice("settings","Glass / Theme","Theme",0),0,5));\n\t\tthis->sync_theme_style( );')
c=replace_function(c,'void menu::draw( )',lambda f:replace_once(f,'\t\tfor ( const auto vk : input.key_presses( ) )','\t\tif(this->m_open) for ( const auto vk : input.key_presses( ) )'))
core.write_text(c,encoding='utf-8')

proj=v/'MCB-CS2.vcxproj';p=proj.read_text(encoding='utf-8')
p=replace_once(p,'<TargetName>MCB-CS2-v6-exact-panel</TargetName>','<TargetName>MCB-CS2-v6.2-exact-panel</TargetName>')
proj.write_text(p,encoding='utf-8')
rc=v/'project/mcb_version.rc';r=rc.read_text(encoding='utf-8')
r=re.sub(r'FILEVERSION\s+1,6,0,0','FILEVERSION 1,6,2,0',r,count=1)
r=re.sub(r'PRODUCTVERSION\s+1,6,0,0','PRODUCTVERSION 1,6,2,0',r,count=1)
r=r.replace('1.6.0\\0','1.6.2\\0').replace('MCB-CS2-v6-exact-panel.dll','MCB-CS2-v6.2-exact-panel.dll')
rc.write_text(r,encoding='utf-8')
report={'version':'6.2','target':'MCB-CS2-v6.2-exact-panel','config_load_save':'implemented; native data tests required','config_stable_ids':True,'bounded_typed_json':True,'same_directory_atomic_replace':True,'preserve_unvisited_page_state':True,'scripts_independent_local_drafts':True,'script_execution_in_view':False,'native_visual_qa':'NOT_TESTED','in_game_runtime':'NOT_TESTED','full_dpi_font_scaling':'NOT_COMPLETED','48_state_acceptance':'NOT_COMPLETED','new_gameplay_logic':False}
(root/'MCB_V62_APPLIED.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('[v62-ui] native config operations, typed persistence, local drafts, scoped IDs applied')
