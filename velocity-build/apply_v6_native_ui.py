from pathlib import Path
import json, re, shutil, sys

TARGET='MCB-CS2-v6-exact-panel'
root=Path(sys.argv[1]).resolve()
v=root/'cs2'/'MCB-CS2'
if not (v/'MCB-CS2.vcxproj').exists():
    raise SystemExit('[v6-ui] MCB v5 source layout not found')

def backup(p):
    b=p.with_suffix(p.suffix+'.v6bak')
    if not b.exists(): shutil.copy2(p,b)

def brace_end(text, sig):
    s=text.find(sig)
    if s<0: raise SystemExit(f'[v6-ui] function not found: {sig}')
    o=text.find('{',s)
    depth=0; i=o; state='code'; quote=''
    while i<len(text):
        c=text[i]; n=text[i+1] if i+1<len(text) else ''
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c in ('\"',"'"): state='str'; quote=c; i+=1; continue
            if c=='{': depth+=1
            elif c=='}':
                depth-=1
                if depth==0: return s,i+1
        elif state=='line':
            if c=='\n': state='code'
        elif state=='block':
            if c=='*' and n=='/': state='code'; i+=2; continue
        else:
            if c=='\\': i+=2; continue
            if c==quote: state='code'
        i+=1
    raise SystemExit('[v6-ui] unmatched function brace')

def replace_fn(text,sig,new):
    a,b=brace_end(text,sig)
    return text[:a]+new.rstrip()+text[b:]

proj=v/'MCB-CS2.vcxproj'; backup(proj)
t=proj.read_text(encoding='utf-8')
t=t.replace('<TargetName>MCB-CS2-v5-stable</TargetName>',f'<TargetName>{TARGET}</TargetName>')
if TARGET not in t: raise SystemExit('[v6-ui] target rename failed')
proj.write_text(t,encoding='utf-8')

rc=v/'project'/'mcb_version.rc'
if rc.exists():
    backup(rc); t=rc.read_text(encoding='utf-8',errors='replace')
    t=re.sub(r'FILEVERSION\s+1,[0-9]+,[0-9]+,[0-9]+','FILEVERSION 1,6,0,0',t,count=1)
    t=re.sub(r'PRODUCTVERSION\s+1,[0-9]+,[0-9]+,[0-9]+','PRODUCTVERSION 1,6,0,0',t,count=1)
    t=re.sub(r'(VALUE\s+\"FileVersion\"\s*,\s*\")[^\"]*(\")',lambda m:m.group(1)+r'1.6.0\0'+m.group(2),t,count=1)
    t=re.sub(r'(VALUE\s+\"ProductVersion\"\s*,\s*\")[^\"]*(\")',lambda m:m.group(1)+r'1.6.0\0'+m.group(2),t,count=1)
    t=re.sub(r'(VALUE\s+\"OriginalFilename\"\s*,\s*\")[^\"]*(\")',lambda m:m.group(1)+TARGET+r'.dll\0'+m.group(2),t,count=1)
    rc.write_text(t,encoding='utf-8')

hpp=v/'project/core/rendering/rendering.hpp'; backup(hpp)
t=hpp.read_text(encoding='utf-8')
bridge='''\n\tnamespace exact_panel_bridge\n\t{\n\t\tint number( std::string_view page, std::string_view group, std::string_view name, int def );\n\t\tbool toggle( std::string_view page, std::string_view group, std::string_view name, bool def );\n\t\tint choice( std::string_view page, std::string_view group, std::string_view name, int def );\n\t}\n'''
if 'namespace exact_panel_bridge' not in t:
    marker='namespace rendering {\n'
    if marker not in t: raise SystemExit('[v6-ui] rendering namespace marker missing')
    t=t.replace(marker,marker+bridge,1)
hpp.write_text(t,encoding='utf-8')

exact=v/'project/core/rendering/impl/menu/menu.exact.cpp'; backup(exact)
t=exact.read_text(encoding='utf-8')
pat=r'static constexpr std::string_view k_model_json = R\"MCBMODEL\((.*?)\)MCBMODEL\";'
m=re.search(pat,t,re.S)
if not m: raise SystemExit('[v6-ui] exact panel JSON model not found')
model=json.loads(m.group(1))
settings=None
for p in model.get('hiddenPages',[]):
    if p.get('id')=='settings': settings=p
if not settings: raise SystemExit('[v6-ui] settings page missing')
if not any(g.get('title')=='Glass / Theme' for g in settings.get('groups',[])):
    settings.setdefault('groups',[]).append({'title':'Glass / Theme','items':[
        ['range','Glass Blur',18,[0,32,'px']],
        ['range','White Glass',8,[0,28,'%']],
        ['range','Glass Saturation',120,[80,160,'%']],
        ['toggle','Border Glow',True,'UI only'],
        ['toggle','Noise Texture',True,'UI only'],
        ['select','Theme','Cyan',['Cyan','Ice','Violet','Mint','Amber','Rose']]
    ]})
new_json=json.dumps(model,ensure_ascii=False,separators=(',',':'))
t=t[:m.start(1)]+new_json+t[m.end(1):]
bridge_impl=r'''\n    namespace exact_panel_bridge\n    {\n        int number( std::string_view page, std::string_view group, std::string_view name, int def )\n        {\n            using namespace exact_panel_detail;\n            load_state( );\n            return number_value( make_key( page, group, name ), def );\n        }\n        bool toggle( std::string_view page, std::string_view group, std::string_view name, bool def )\n        {\n            using namespace exact_panel_detail;\n            load_state( );\n            return toggle_value( make_key( page, group, name ), def, name, group );\n        }\n        int choice( std::string_view page, std::string_view group, std::string_view name, int def )\n        {\n            using namespace exact_panel_detail;\n            load_state( );\n            return choice_value( make_key( page, group, name ), def );\n        }\n    }\n'''.replace('\\n','\n').replace('\\t','\t')
if 'namespace exact_panel_bridge' not in t:
    marker='    void menu::save_exact_panel_state( )'
    if marker not in t: raise SystemExit('[v6-ui] save state marker missing')
    t=t.replace(marker,bridge_impl+'\n'+marker,1)
exact.write_text(t,encoding='utf-8')

core=v/'project/core/rendering/impl/menu/menu.core.cpp'; backup(core)
t=core.read_text(encoding='utf-8')
theme_fn=r'''void menu::apply_theme_preset( int preset )\n\t{\n\t\tthis->m_theme_preset = std::clamp( preset, 0, 5 );\n\t\tswitch ( this->m_theme_preset )\n\t\t{\n\t\tdefault:\n\t\tcase 0: tokens::col_accent=xdraw::color{19,191,245,255}; tokens::col_dark=xdraw::color{5,7,10,255}; tokens::col_text=xdraw::color{238,243,247,245}; tokens::col_text_dim=xdraw::color{138,150,163,180}; tokens::col_card=xdraw::color{7,17,26,190}; tokens::col_elevated=xdraw::color{8,18,27,215}; break;\n\t\tcase 1: tokens::col_accent=xdraw::color{125,211,252,255}; tokens::col_dark=xdraw::color{5,9,14,255}; tokens::col_text=xdraw::color{239,248,255,245}; tokens::col_text_dim=xdraw::color{150,196,220,180}; tokens::col_card=xdraw::color{7,18,28,190}; tokens::col_elevated=xdraw::color{9,22,34,215}; break;\n\t\tcase 2: tokens::col_accent=xdraw::color{168,85,247,255}; tokens::col_dark=xdraw::color{10,6,15,255}; tokens::col_text=xdraw::color{246,239,255,245}; tokens::col_text_dim=xdraw::color{186,153,218,180}; tokens::col_card=xdraw::color{18,10,27,190}; tokens::col_elevated=xdraw::color{27,14,39,215}; break;\n\t\tcase 3: tokens::col_accent=xdraw::color{52,211,153,255}; tokens::col_dark=xdraw::color{5,12,10,255}; tokens::col_text=xdraw::color{235,252,246,245}; tokens::col_text_dim=xdraw::color{132,190,169,180}; tokens::col_card=xdraw::color{7,21,17,190}; tokens::col_elevated=xdraw::color{10,31,25,215}; break;\n\t\tcase 4: tokens::col_accent=xdraw::color{245,158,11,255}; tokens::col_dark=xdraw::color{14,10,5,255}; tokens::col_text=xdraw::color{255,246,229,245}; tokens::col_text_dim=xdraw::color{210,170,105,180}; tokens::col_card=xdraw::color{26,18,7,190}; tokens::col_elevated=xdraw::color{39,27,10,215}; break;\n\t\tcase 5: tokens::col_accent=xdraw::color{244,63,94,255}; tokens::col_dark=xdraw::color{15,5,8,255}; tokens::col_text=xdraw::color{255,238,242,245}; tokens::col_text_dim=xdraw::color{211,137,151,180}; tokens::col_card=xdraw::color{27,8,13,190}; tokens::col_elevated=xdraw::color{40,11,19,215}; break;\n\t\t}\n\t}'''.replace('\\n','\n').replace('\\t','\t')
t=replace_fn(t,'void menu::apply_theme_preset( int preset )',theme_fn)
sig='void menu::sync_theme_style( ) const'
a,b=brace_end(t,sig)
body=t[a:b]
if 'exact_panel_bridge::number( "settings", "Interface", "Menu Opacity"' not in body:
    o=body.find('{')+1
    inject='''\n\t\tconst auto mcb_opacity = std::clamp( exact_panel_bridge::number( "settings", "Interface", "Menu Opacity", 96 ), 40, 100 );\n\t\tconst auto mcb_alpha = static_cast<std::uint8_t>( std::clamp( mcb_opacity * 2.45f, 0.0f, 245.0f ) );'''
    body=body[:o]+inject+body[o:]
    body=re.sub(r'style\.window_bg\s*=\s*[^;]+;', 'style.window_bg = tokens::col_elevated.alpha( mcb_alpha );', body, count=1)
    t=t[:a]+body+t[b:]
old='\t\tconst auto anim_speed = this->m_open ? 14.0f : 16.0f;'
if old in t:
    new='''\t\tconst auto mcb_anim_enabled = exact_panel_bridge::toggle( "settings", "Interface", "Menu Animations", true );\n\t\tconst auto mcb_anim_pct = std::clamp( exact_panel_bridge::number( "settings", "Interface", "Animation Speed", 72 ), 0, 100 );\n\t\tconst auto anim_speed = mcb_anim_enabled ? ( 4.0f + 18.0f * static_cast<float>( mcb_anim_pct ) / 100.0f ) : 1000.0f;'''
    t=t.replace(old,new,1)
marker='\t\t// Exact shell: one 203/58px sidebar, one 66px topbar, one workspace.\n'
if marker not in t:
    marker='\t\t// Exact shell: one 203/64px sidebar, one 66px topbar, one workspace.\n'
if marker not in t: raise SystemExit('[v6-ui] exact shell marker missing')
if 'MCB v6 R2 glass layer' not in t:
    glass=r'''\t\t// MCB v6 R2 glass layer. UI presentation only.\n\t\t{\n\t\t\tauto& shell_dl = xui::draw::current( );\n\t\t\tconst auto theme = std::clamp( exact_panel_bridge::choice( "settings", "Glass / Theme", "Theme", 0 ), 0, 5 );\n\t\t\tif ( theme != this->m_theme_preset ) this->apply_theme_preset( theme );\n\t\t\tconst auto blur = std::clamp( exact_panel_bridge::number( "settings", "Glass / Theme", "Glass Blur", 18 ), 0, 32 );\n\t\t\tconst auto white = std::clamp( exact_panel_bridge::number( "settings", "Glass / Theme", "White Glass", 8 ), 0, 28 );\n\t\t\tconst auto saturation = std::clamp( exact_panel_bridge::number( "settings", "Glass / Theme", "Glass Saturation", 120 ), 80, 160 );\n\t\t\tconst auto border = exact_panel_bridge::toggle( "settings", "Glass / Theme", "Border Glow", true );\n\t\t\tconst auto noise = exact_panel_bridge::toggle( "settings", "Glass / Theme", "Noise Texture", true );\n\t\t\tif ( blur > 0 ) shell_dl.rect_filled_blurred( this->m_x, this->m_y, this->m_w, this->m_h, xdraw::corner_radius{ 6.0f }, xdraw::color{255,255,255,static_cast<std::uint8_t>(std::clamp(blur*4,18,128))} );\n\t\t\tif ( white > 0 ) shell_dl.rect_filled( this->m_x, this->m_y, this->m_w, this->m_h, xdraw::color{255,255,255,static_cast<std::uint8_t>(white*2)}, xdraw::corner_radius{6.0f} );\n\t\t\tif ( saturation != 100 )\n\t\t\t{\n\t\t\t\tconst auto delta=std::abs(saturation-100); const auto a=static_cast<std::uint8_t>(std::clamp(delta/2,0,30));\n\t\t\t\tconst auto tint=saturation>100 ? tokens::col_accent.alpha(a) : xdraw::color{160,170,180,a};\n\t\t\t\tshell_dl.rect_filled(this->m_x,this->m_y,this->m_w,this->m_h,tint,xdraw::corner_radius{6.0f});\n\t\t\t}\n\t\t\tif ( noise ) for ( int i=0;i<48;++i ) { const auto nx=this->m_x+11.0f+std::fmod(static_cast<float>(i*73),std::max(1.0f,this->m_w-22.0f)); const auto ny=this->m_y+11.0f+std::fmod(static_cast<float>(i*47),std::max(1.0f,this->m_h-22.0f)); shell_dl.rect_filled(nx,ny,1.0f,1.0f,xdraw::color{255,255,255,13}); }\n\t\t\tif ( border ) shell_dl.rect(this->m_x,this->m_y,this->m_w,this->m_h,tokens::col_accent.alpha(72),xdraw::corner_radius{6.0f},1.0f);\n\t\t}\n\n'''.replace('\\n','\n').replace('\\t','\t')
    t=t.replace(marker,glass+marker,1)
core.write_text(t,encoding='utf-8')

(root/'MCB_V6_EXACT_PANEL_APPLIED.json').write_text(json.dumps({
 'target':TARGET,'base_size':[846,682],'sidebar':203,'topbar':66,
 'views':10,'glass_controls':['Glass Blur','White Glass','Glass Saturation','Border Glow','Noise Texture','Theme'],
 'themes':['Cyan','Ice','Violet','Mint','Amber','Rose'],'new_gameplay_logic':False,'new_antidetection_logic':False
},indent=2),encoding='utf-8')
print('[v6-ui] applied')
