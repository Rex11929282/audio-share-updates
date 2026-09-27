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
    def storage(s):
        a='''DWORD size{};
			if ( RegQueryValueExW( hkey, name.data( ), nullptr, nullptr, nullptr, &size ) != ERROR_SUCCESS || size == 0 )'''
        b='''DWORD size{},value_type{};
            if ( RegQueryValueExW(hkey,name.data(),nullptr,&value_type,nullptr,&size)!=ERROR_SUCCESS || value_type!=REG_BINARY || size==0 || size>8u*1024u*1024u )'''
        s=once(s,a,b)
        s=once(s,'RegQueryValueExW( hkey, name.data( ), nullptr, nullptr, buf.data( ), &size ) != ERROR_SUCCESS','RegQueryValueExW(hkey,name.data(),nullptr,&value_type,buf.data(),&size)!=ERROR_SUCCESS || value_type!=REG_BINARY || size!=buf.size()')
        return s
    change('project/external/config.hpp',storage)
    def xui_finish(s):
        old='const auto drag_zone = rect{ abs.x + tokens::sidebar_w + 238.0f, abs.y, std::max( 0.0f, abs.w - tokens::sidebar_w - 458.0f ), std::min( 66.0f, abs.h ) };'
        s=once(s,old,'const auto drag_zone = rect{ abs.x + tokens::sidebar_w, abs.y, std::max( 0.0f, abs.w - tokens::sidebar_w ), std::min( 14.0f, abs.h ) };')
        return function(s,'const char* vk_name( int key )',r'''const char* vk_name( int key )
    {
        if(key==0)return "未綁定";
        thread_local char text[48]{};
        if(key>=0x41 && key<=0x5A){std::snprintf(text,sizeof(text),"字母鍵%02d",key-0x40);return text;}
        if(key>=0x30 && key<=0x39){std::snprintf(text,sizeof(text),"數字鍵%d",key-0x30);return text;}
        if(key>=VK_F1 && key<=VK_F24){std::snprintf(text,sizeof(text),"功能鍵%d",key-VK_F1+1);return text;}
        switch(key){
        case VK_LBUTTON:return "左鍵";case VK_RBUTTON:return "右鍵";case VK_MBUTTON:return "中鍵";
        case VK_XBUTTON1:return "側鍵一";case VK_XBUTTON2:return "側鍵二";
        case VK_SHIFT:case VK_LSHIFT:case VK_RSHIFT:return "上檔鍵";
        case VK_CONTROL:case VK_LCONTROL:case VK_RCONTROL:return "控制鍵";
        case VK_MENU:case VK_LMENU:case VK_RMENU:return "替代鍵";
        case VK_SPACE:return "空白鍵";case VK_RETURN:return "確認鍵";case VK_ESCAPE:return "退出鍵";
        case VK_TAB:return "定位鍵";case VK_CAPITAL:return "大寫鎖定";case VK_INSERT:return "插入鍵";
        case VK_DELETE:return "刪除鍵";case VK_HOME:return "起始鍵";case VK_END:return "結束鍵";
        case VK_PRIOR:return "上一頁";case VK_NEXT:return "下一頁";
        case VK_LEFT:return "向左鍵";case VK_RIGHT:return "向右鍵";case VK_UP:return "向上鍵";case VK_DOWN:return "向下鍵";
        case VK_BACK:return "退格鍵";case VK_PAUSE:return "暫停鍵";case VK_SNAPSHOT:return "截圖鍵";
        default:std::snprintf(text,sizeof(text),"按鍵%d",key);return text;
        }
    }''')
    change('project/external/xdraw/xui/xui.cpp',xui_finish)
    def shell_finish(s):
        s=once(s,'"UI ONLY · LOCAL"','"來源整合・本機"')
        return once(s,'"M", xdraw::color{ 168, 237, 255, 255 }','"我", xdraw::color{ 168, 237, 255, 255 }')
    change('project/core/rendering/impl/menu/menu.core.cpp',shell_finish)
    def translate_finish(s):
        return once(s,'#undef EXTRA','            EXTRA("clear bind","清除綁定");\n            EXTRA("unknown","未知");\n#undef EXTRA')
    change('project/core/localization/zh_tw.hpp',translate_finish)
    fixture=Path(__file__).parent/'tests/native_ui.cpp'
    t=fixture.read_text(encoding='utf-8')
    t=once(t,'  xui::layout::set_cursor(215,100);','''  if(shape==4){
   xui::layout::set_cursor(470,18);out.first=xui::button("工具列功能##toolbar_test",100,28);out.rect=xui::layout::current_window()->last_item;
  } else {
  xui::layout::set_cursor(215,100);''')
    t=once(t,'  xui::end_window();xui::end();xdraw::end_frame();return out;','  }\n  xui::end_window();xui::end();xdraw::end_frame();return out;')
    t=once(t,'  h.resize(1440,1080,1440,1080);h.frame(3);','''  h.resize(1440,1080,1440,1080);
  auto top=h.frame(4);const auto tx=top.rect.x+30,ty=top.rect.y+10;
  h.pointer(tx,ty,WM_LBUTTONDOWN);h.frame(4);check(xui::ctx().active_window==xui::null_id,"toolbar press does not start window drag");
  h.pointer(tx,ty,WM_LBUTTONUP);check(h.frame(4).first,"toolbar button releases independently of window drag");
  for(int keycode=0;keycode<256;++keycode){const std::string name=xui::vk_name(keycode);check(!name.empty()&&std::none_of(name.begin(),name.end(),[](unsigned char c){return (c>='A'&&c<='Z')||(c>='a'&&c<='z');}),"Chinese virtual-key label");}
  check(localization::tr("clear bind")=="清除綁定","keybind action translation");
  h.resize(1440,1080,1440,1080);h.frame(3);''')
    fixture.write_text(t,encoding='utf-8',newline='\n')
    changed.append({'test_harness':str(fixture.name),'after':hashlib.sha256(t.encode()).hexdigest()})
    out=root/'MCB_UNIFIED_FINALIZE.json';out.write_text(json.dumps({'changes':changed,'in_game_runtime':'NOT_TESTED'},ensure_ascii=False,indent=2),encoding='utf-8')
    return changed

if __name__=='__main__':print(json.dumps(apply(Path(sys.argv[1])),ensure_ascii=False))
