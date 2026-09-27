"""Source integration follow-up: functional toolbar, safe config IO, transient cleanup."""
from pathlib import Path
import sys,json,hashlib
sys.path.insert(0,str(Path(__file__).parent/'unified'))
from mcb_unify import once,function

def apply(root):
    changed=[]
    def change(rel,fn):
        p=root/rel;s=p.read_text(encoding='utf-8-sig');t=fn(s)
        if s==t:raise ValueError('no source change: '+rel)
        p.write_text(t,encoding='utf-8',newline='\n')
        changed.append({'path':rel,'before':hashlib.sha256(s.encode()).hexdigest(),'after':hashlib.sha256(t.encode()).hexdigest()})
    def toolbar(s):
        old="""if ( input.ctrl_held( ) && vk == 'S' )
			{
				this->save_exact_panel_state( );
				continue;
			}"""
        s=once(s,old,"""if ( input.ctrl_held( ) && vk == 'S' )
            {
                this->m_exact_page=8;this->m_subtab=0;this->m_search_open=false;this->m_search_query.clear();
                continue;
            }""")
        s=once(s,"this->m_search_open = true;\n\t\t\t\tcontinue;","this->m_exact_page=8;this->m_search_open=false;\n\t\t\t\tcontinue;")
        return function(s,'void menu::draw_top_bar( float w )',r'''void menu::draw_top_bar( float w )
    {
        auto& dl=xui::draw::current();const auto sw=tokens::sidebar_w;
        dl.rect_filled(this->m_x+sw,this->m_y,w,66.0f,tokens::col_elevated.alpha(135),xdraw::corner_radius{6.0f});
        dl.line(this->m_x+sw,this->m_y+66,this->m_x+this->m_w,this->m_y+66,xdraw::color{20,37,54,180});
        xui::layout::set_cursor(sw+12.0f,18.0f);
        const auto bw=std::max(32.0f,(w-24.0f-xui::ctx().style.item_spacing_x*5.0f)/6.0f);
        const auto navigate=[this](int page){m_exact_page=page;m_subtab=0;m_search_open=false;m_search_query.clear();};
        if(xui::button("功能設定##mcb_native_cfg",bw,28))navigate(8);
        xui::layout::same_line();if(xui::button("腳本管理##mcb_native_scripts",bw,28))navigate(7);
        xui::layout::same_line();if(xui::button("外觀設定##mcb_theme",bw,28))navigate(9);
        xui::layout::same_line();if(xui::button("保存外觀##mcb_appearance_save",bw,28))this->save_exact_panel_state();
        xui::layout::same_line();if(xui::button("背景遮罩##mcb_dim",bw,28))this->m_dim_interface=!this->m_dim_interface;
        xui::layout::same_line();if(xui::button(this->m_sidebar_compact?"展開側欄##mcb_sidebar":"收合側欄##mcb_sidebar",bw,28))this->m_sidebar_compact=!this->m_sidebar_compact;
    }''')
    change('project/core/rendering/impl/menu/menu.core.cpp',toolbar)
    def cfg(s):
        s=once(s,'auto needs_refresh{ true };','auto needs_refresh{ true };\n        std::string operation_status{};')
        s=once(s,'if ( detail::needs_refresh )','static ULONGLONG last_refresh{};\n        const auto now=GetTickCount64();\n        if ( detail::needs_refresh || now-last_refresh>=1000 )')
        s=once(s,'detail::needs_refresh = false;','detail::needs_refresh = false;last_refresh=now;')
        s=once(s,'config::registry::load(wname);settings::finalize_binds();','''if(config::registry::load(wname)){settings::finalize_binds();detail::operation_status="已載入功能設定";}
                    else detail::operation_status="載入失敗：設定格式或資料不符";''')
        s=once(s,'const auto save_name = has_selection ? detail::selected_name( ) : detail::search_buf;','const auto save_name = !detail::search_buf.empty() ? detail::search_buf : detail::selected_name();')
        s=once(s,'config::registry::save( detail::utf8_to_wide( save_name ) );','detail::operation_status=config::registry::save(detail::utf8_to_wide(save_name))?"已保存功能設定":"保存失敗";')
        s=once(s,'xui::text_input( "##cfg_search", detail::search_buf, 64, "search configs..." );','xui::text_input( "##cfg_search", detail::search_buf, 64, "search configs..." );\n        if(!detail::operation_status.empty())xui::text(detail::operation_status,tokens::col_text_dim);')
        return s
    change('project/core/rendering/impl/menu/menu.config.cpp',cfg)
    def rage(s):
        old='''if ( !ctx.valid )
		{
			this->m_revolver_cock_ticks = 0;
			return;
		}'''
        return once(s,old,'''if ( !cmd || !ctx.valid || !local.is_alive || !local.pawn || !local.controller )
        {
            this->reset_transient();
            return;
        }''')
    change('project/core/features/combat/impl/rage.cpp',rage)
    def hooks(s):
        old='''if ( !local.is_alive || !systems::g_view.has_camera( ) )
		{
			return;
		}'''
        return once(s,old,'''if ( !local.is_alive || !systems::g_view.has_camera( ) )
        {
            features::combat::g_rage.reset_transient();
            features::combat::g_legit.reset_transient();
            return;
        }''')
    change('project/core/hooks/impl/cheat.cpp',hooks)
    # Existing migration already uses the 8 MiB limit; enforce it at ordinary loads too.
    def storage(s):
        a='''DWORD size{};
			if ( RegQueryValueExW( hkey, name.data( ), nullptr, nullptr, nullptr, &size ) != ERROR_SUCCESS || size == 0 )'''
        b='''DWORD size{},value_type{};
            if ( RegQueryValueExW(hkey,name.data(),nullptr,&value_type,nullptr,&size)!=ERROR_SUCCESS || value_type!=REG_BINARY || size==0 || size>8u*1024u*1024u )'''
        s=once(s,a,b)
        s=once(s,'RegQueryValueExW( hkey, name.data( ), nullptr, nullptr, buf.data( ), &size ) != ERROR_SUCCESS','RegQueryValueExW(hkey,name.data(),nullptr,&value_type,buf.data(),&size)!=ERROR_SUCCESS || value_type!=REG_BINARY || size!=buf.size()')
        return s
    change('project/external/config.hpp',storage)
    out=root/'MCB_UNIFIED_FINALIZE.json';out.write_text(json.dumps({'changes':changed,'in_game_runtime':'NOT_TESTED'},ensure_ascii=False,indent=2),encoding='utf-8')
    return changed

if __name__=='__main__':print(json.dumps(apply(Path(sys.argv[1])),ensure_ascii=False))
