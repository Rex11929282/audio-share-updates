from pathlib import Path
import sys, json, hashlib

root = Path(sys.argv[1])
changed=[]

def change(rel, fn):
    p=root/rel
    s=p.read_text(encoding='utf-8-sig')
    t=fn(s)
    if t==s:
        raise RuntimeError('no change: '+rel)
    p.write_text(t,encoding='utf-8',newline='\n')
    changed.append({'path':rel,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})

def once(s, old, new, label):
    n=s.count(old)
    if n!=1:
        raise RuntimeError(f'{label}: expected 1 anchor, got {n}')
    return s.replace(old,new,1)

# 1) Core XUI: exact hover hitbox, no lingering hover glow, hard width/text containment.
def patch_xui(s):
    old='''\tvoid text( std::string_view label, xdraw::color col )\n\t{\n\t\tif ( !layout::current_window( ) )\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\tconst auto [display, full] = parse_label( label );\n\t\tconst auto [lw, lh] = xdraw::measure_text( display );\n\t\tconst auto abs = layout::item( lw, lh );\n\n\t\tdraw::current( ).text( abs.x, abs.y, display, col );\n\t}\n'''
    new='''\tvoid text( std::string_view label, xdraw::color col )\n\t{\n\t\tif ( !layout::current_window( ) )\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\tconst auto [display, full] = parse_label( label );\n\t\tconst auto [avail_w, avail_h] = layout::avail( );\n\t\t( void )avail_h;\n\t\tconst auto shown = truncate( display, std::max( 0.0f, avail_w ) );\n\t\tconst auto [lw, lh] = xdraw::measure_text( shown );\n\t\tconst auto abs = layout::item( std::min( lw, std::max( 0.0f, avail_w ) ), lh );\n\n\t\tdraw::current( ).text( abs.x, abs.y, shown, col );\n\t}\n'''
    s=once(s,old,new,'text clamp')
    old2='''\t\tconst auto abs = layout::item( w, h );\n\t\tconst auto can_interact = !c.overlay_blocking( );\n\t\tconst auto interaction = abs.expand( 4.0f );\n\t\tconst auto hovered = can_interact && input.in_rect( interaction );\n\t\tconst auto held = c.active_press == id && input.mouse_down;\n\t\tconst auto pressed = released_click(id,hovered);\n\n\t\tconst auto hover_anim = anim::lerp( id, hovered ? 1.0f : 0.0f, 12.0f );\n\t\tconst auto active_anim = anim::lerp( id + 1, held ? 1.0f : 0.0f, 15.0f );\n'''
    new2='''\t\tconst auto [avail_w, avail_h] = layout::avail( );\n\t\t( void )avail_h;\n\t\tw = std::clamp( w, 0.0f, std::max( 0.0f, avail_w ) );\n\t\tconst auto abs = layout::item( w, h );\n\t\tconst auto can_interact = !c.overlay_blocking( );\n\t\tconst auto hovered = can_interact && input.in_rect( abs );\n\t\tconst auto held = c.active_press == id && input.mouse_down;\n\t\tconst auto pressed = released_click(id,hovered);\n\n\t\tconst auto hover_anim = hovered ? 1.0f : 0.0f;\n\t\tconst auto active_anim = anim::lerp( id + 1, held ? 1.0f : 0.0f, 15.0f );\n'''
    s=once(s,old2,new2,'button exact hover')
    return s
change('project/external/xdraw/xui/xui.cpp',patch_xui)

# 2) Inventory: weapon image area is the explicit button; inventory hover does not linger.
def patch_skins(s):
    # Stable direct hover on all inventory tile types.
    s=s.replace('const auto hover_anim = xui::anim::lerp( xui::fnv1a( "ateam" ) + static_cast< std::uintptr_t >( team ), hovered ? 1.0f : 0.0f, 14.0f );',
                'const auto hover_anim = hovered ? 1.0f : 0.0f;')
    s=s.replace('const auto hover_anim = xui::anim::lerp( xui::fnv1a( "atile" ) + static_cast< std::uintptr_t >( def->def_index ), hovered ? 1.0f : 0.0f, 14.0f );',
                'const auto hover_anim = hovered ? 1.0f : 0.0f;')
    s=s.replace('const auto hover_anim = xui::anim::lerp( xui::fnv1a( "scard" ) + static_cast< std::uintptr_t >( pk->id ), hovered ? 1.0f : 0.0f, 14.0f );',
                'const auto hover_anim = hovered ? 1.0f : 0.0f;')

    old='''\t\t\tconst auto image_h = std::floor( card.h * k_image_h_ratio );\n\n\t\t\tconst auto hovered = !xui::ctx( ).overlay_blocking( ) && input.in_rect( card );\n\t\t\tconst auto hover_anim = xui::anim::lerp( xui::fnv1a( "wcard" ) + static_cast< std::uintptr_t >( def->def_index ), hovered ? 1.0f : 0.0f, 14.0f );\n\n\t\t\tauto card_bg = tokens::col_card;\n'''
    new='''\t\t\tconst auto image_h = std::floor( card.h * k_image_h_ratio );\n\t\t\tconst auto image_button = xui::rect{ card.x + 5.0f, card.y + 5.0f, std::max( 0.0f, card.w - 10.0f ), std::max( 20.0f, image_h - 8.0f ) };\n\n\t\t\tconst auto hovered = !xui::ctx( ).overlay_blocking( ) && input.in_rect( image_button );\n\t\t\tconst auto hover_anim = hovered ? 1.0f : 0.0f;\n\n\t\t\tauto card_bg = tokens::col_card;\n'''
    s=once(s,old,new,'weapon image button hover')
    anchor='''\t\t\tdl.rect_filled( card.x, card.y, card.w, card.h, card_bg, xdraw::corner_radius{ tokens::btn_rounding } );\n\n\t\t\tif ( is_skinned )\n'''
    rep='''\t\t\tdl.rect_filled( card.x, card.y, card.w, card.h, card_bg, xdraw::corner_radius{ tokens::btn_rounding } );\n\n\t\t\tauto image_bg = xui::darken( tokens::col_card, 0.92f );\n\t\t\timage_bg.a = static_cast< std::uint8_t >( image_bg.a * fade_alpha );\n\t\t\tif ( hovered ) image_bg = xui::lighten( image_bg, 1.28f );\n\t\t\tdl.rect_filled( image_button.x, image_button.y, image_button.w, image_button.h, image_bg, xdraw::corner_radius{ std::max( 3.0f, tokens::btn_rounding - 2.0f ) } );\n\t\t\tif ( hovered )\n\t\t\t{\n\t\t\t\tauto hc = tokens::col_accent;\n\t\t\t\thc.a = static_cast< std::uint8_t >( 190.0f * fade_alpha );\n\t\t\t\tdl.rect( image_button.x, image_button.y, image_button.w, image_button.h, hc, xdraw::corner_radius{ std::max( 3.0f, tokens::btn_rounding - 2.0f ) }, 1.0f );\n\t\t\t}\n\n\t\t\tif ( is_skinned )\n'''
    s=once(s,anchor,rep,'weapon image button draw')
    # Ensure image itself stays inside the button area.
    s=s.replace('const auto target_h = image_h - 12.0f;', 'const auto target_h = std::max( 1.0f, image_button.h - 8.0f );', 1)
    s=s.replace('if ( iw > card.w - 12.0f )\n\t\t\t\t{\n\t\t\t\t\tiw = card.w - 12.0f;', 'if ( iw > image_button.w - 8.0f )\n\t\t\t\t{\n\t\t\t\t\tiw = image_button.w - 8.0f;', 1)
    s=s.replace('const auto ix = std::floor( card.x + ( card.w - iw ) * 0.5f );\n\t\t\t\tconst auto iy = std::floor( card.y + ( image_h - ih ) * 0.5f );',
                'const auto ix = std::floor( image_button.x + ( image_button.w - iw ) * 0.5f );\n\t\t\t\tconst auto iy = std::floor( image_button.y + ( image_button.h - ih ) * 0.5f );',1)
    return s
change('project/core/rendering/impl/menu/menu.skins.cpp',patch_skins)

# 3) Lua page: left scrolling script/status list, right fixed actions. No same-line overflow.
def patch_lua(s):
    start=s.find('\t\t// Dedicated Lua tab so script import is never hidden below the config list.')
    if start<0: raise RuntimeError('lua block start missing')
    end=s.find('\n\t\tstatic ULONGLONG last_refresh{};',start)
    if end<0: raise RuntimeError('lua block end missing')
    block='''\t\t// Dedicated Lua workspace: script list on the left, actions on the right.\n\t\tif ( this->m_subtab == 1 )\n\t\t{\n\t\t\tconst auto& style = xui::ctx( ).style;\n\t\t\tconst auto gap = std::max( 8.0f, style.item_spacing_x );\n\t\t\tconst auto right_w = std::clamp( panel_w * 0.34f, 190.0f, 250.0f );\n\t\t\tconst auto left_w = std::max( 180.0f, panel_w - right_w - gap );\n\n\t\t\txui::layout::set_cursor( panel_x - parent->bounds.x, panel_y - parent->bounds.y );\n\t\t\tif ( xui::begin_child( "##lua_script_list", left_w, panel_h, true ) )\n\t\t\t{\n\t\t\t\txui::text( "腳本", tokens::col_text );\n\t\t\t\tconst auto count = scripting::g_lua.script_count( );\n\t\t\t\tconst auto count_text = std::string( "已偵測 " ) + std::to_string( count ) + " 個腳本";\n\t\t\t\txui::text( count_text, tokens::col_text_dim );\n\t\t\t\txui::layout::separator( );\n\n\t\t\t\tconst auto lua_statuses = scripting::g_lua.script_statuses( );\n\t\t\t\tif ( lua_statuses.empty( ) )\n\t\t\t\t{\n\t\t\t\t\txui::text( "目前沒有腳本", tokens::col_text_dim );\n\t\t\t\t}\n\t\t\t\telse\n\t\t\t\t{\n\t\t\t\t\tfor ( const auto& status : lua_statuses )\n\t\t\t\t\t{\n\t\t\t\t\t\tconst auto suffix = status.suspended ? " · 已暫停" : status.loaded ? " · 運行中" : " · 載入失敗";\n\t\t\t\t\t\tconst auto line = status.name + suffix;\n\t\t\t\t\t\tconst auto col = status.suspended || !status.loaded ? xdraw::color{ 255, 145, 145, 235 } : tokens::col_accent;\n\t\t\t\t\t\txui::text( line, col );\n\t\t\t\t\t\tif ( !status.last_error.empty( ) )\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\txui::text( std::string( "錯誤：" ) + status.last_error, xdraw::color{ 255, 145, 145, 210 } );\n\t\t\t\t\t\t}\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t\txui::end_child( );\n\t\t\t}\n\n\t\t\txui::layout::set_cursor( panel_x - parent->bounds.x + left_w + gap, panel_y - parent->bounds.y );\n\t\t\tif ( xui::begin_child( "##lua_actions", right_w, panel_h, false ) )\n\t\t\t{\n\t\t\t\txui::text( "操作", tokens::col_text );\n\t\t\t\txui::layout::separator( );\n\t\t\t\tauto action_width = xui::layout::avail( ).first;\n\t\t\t\tif ( xui::button( "導入 Lua 腳本##lua_import_dedicated", action_width, 32.0f ) )\n\t\t\t\t\tscripting::g_lua.import_script_dialog( rendering::g_context.get_window( ) );\n\t\t\t\taction_width = xui::layout::avail( ).first;\n\t\t\t\tif ( xui::button( "重新載入 Lua##lua_reload_dedicated", action_width, 32.0f ) )\n\t\t\t\t\tscripting::g_lua.reload_all( );\n\t\t\t\taction_width = xui::layout::avail( ).first;\n\t\t\t\tif ( xui::button( "開啟腳本資料夾##lua_folder_dedicated", action_width, 32.0f ) )\n\t\t\t\t\tscripting::g_lua.open_script_directory( );\n\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::text( "腳本資料夾", tokens::col_text );\n\t\t\t\txui::text( scripting::g_lua.script_directory( ).string( ), tokens::col_text_dim );\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::text( "導入後會立即重新載入。", tokens::col_text_dim );\n\t\t\t\txui::text( "修改檔案後支援熱重載。", tokens::col_text_dim );\n\t\t\t\txui::end_child( );\n\t\t\t}\n\t\t\treturn;\n\t\t}\n'''
    return s[:start]+block+s[end:]
change('project/core/rendering/impl/menu/menu.config.cpp',patch_lua)

# 4) Regression: 2px outside the visible button must never activate.
test=Path('velocity-build/unified/tests/native_ui.cpp')
if not test.exists():
    test=root.parent.parent/'build-tools/unified/tests/native_ui.cpp'
if test.exists():
    t=test.read_text(encoding='utf-8')
    anchor='''   h.pointer(x,y,WM_LBUTTONDOWN);check(h.frame().first,"button activates on press edge");\n   check(!h.frame().first,"held button does not repeat");\n'''
    insert='''   h.pointer(x,y,WM_LBUTTONDOWN);check(h.frame().first,"button activates on press edge");\n   check(!h.frame().first,"held button does not repeat");\n'''
    # Keep the existing sequence; add exact-hitbox check after release below.
    anchor2='''   h.pointer(x,y,WM_LBUTTONUP);check(!h.frame().first,"release does not fire a second activation");\n'''
    add='''   h.pointer(x,y,WM_LBUTTONUP);check(!h.frame().first,"release does not fire a second activation");\n   r=h.frame();h.pointer(r.rect.right()+2.0f,r.rect.y+r.rect.h*0.5f,WM_LBUTTONDOWN);check(!h.frame().first,"two pixels outside visible button is not hovered or clickable");h.pointer(r.rect.right()+2.0f,r.rect.y+r.rect.h*0.5f,WM_LBUTTONUP);h.frame();\n'''
    if t.count(anchor2)!=1: raise RuntimeError('native exact hitbox anchor mismatch')
    t=t.replace(anchor2,add,1)
    test.write_text(t,encoding='utf-8',newline='\n')
    changed.append({'path':'build-tools/unified/tests/native_ui.cpp','sha256':hashlib.sha256(test.read_bytes()).hexdigest()})

report={'changes':changed,'goals':['exact hover','inventory photo buttons','no overflow','lua layout'],'in_game_runtime':'NOT_TESTED'}
(root/'MCB_UI_BUGFIX_POLISH.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
