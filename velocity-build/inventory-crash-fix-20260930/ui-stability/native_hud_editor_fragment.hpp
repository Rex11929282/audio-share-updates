#pragma once
#include <core/rendering/hud_layout_xui.hpp>

// Include after the existing standalone native_ui.cpp host/check definitions.
// Call ui_hud_editor_regression(h) before the preset tests. This loads no game DLL.
template<class Host>
void ui_hud_editor_regression(Host& h) {
    namespace hud=rendering::hud_layout;
    struct fixture {
        hud::registry geometry;
        hud::editor editor;
        hud::editor_surface toolbar;
        std::array<hud::value,hud::count> values{};
        hud::bindings links{};
        std::array<bool,hud::count> visible{{true,true,true,true,true,true}};
        fixture(){
            for(std::size_t i=0;i<hud::count;++i) links[i]={&values[i].x,&values[i].y,&values[i].scale,true};
            editor.start(links);editor.snapping=false;
        }
        void frame(bool cancel_pointer=false){
            xdraw::begin_frame();geometry.begin_frame();
            const auto [wi,hi]=xdraw::viewport_size();const float w=float(wi),height=float(hi);
            const std::array<hud::rect,hud::count> bases{{{20,20,190,24},{20,350,180,78},{w-230,120,174,75},{w-230,350,180,90},{w/2-110,20,220,24},{20,height-60,340,28}}};
            auto& dl=xdraw::get(xdraw::layer::middle);
            for(std::size_t i=0;i<hud::count;++i) if(visible[i]&&links[i].enabled){
                auto r=bases[i];r.w=80+(r.w-80)*values[i].scale;r.h*=values[i].scale;
                r.x=rendering::viewport::clamp_origin(r.x+values[i].x,r.w,w);
                r.y=rendering::viewport::clamp_origin(r.y+values[i].y,r.h,height);
                dl.rect_filled(r.x,r.y,r.w,r.h,xdraw::color{34,38,47,230},xdraw::corner_radius{5});
                geometry.add(static_cast<hud::component>(i),r,bases[i].x,bases[i].y);
            }
            xui::begin();hud::draw_editor(editor,geometry,links,cancel_pointer,toolbar);xui::end();xdraw::end_frame();
        }
    };
    auto close=[](float a,float b){return std::abs(a-b)<1.5f;};
    for(const auto sizes:std::array<std::array<int,4>,4>{{{1440,1080,1440,1080},{1280,720,1280,720},{1920,1080,1440,1080},{1440,1080,1920,1080}}}) {
        h.resize(sizes[0],sizes[1],sizes[2],sizes[3]);
        for(int id=0;id<int(hud::count);++id){
            fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();
            auto n=f.geometry.nodes[id].bounds;float px=n.x+n.w/2,py=n.y+n.h/2;
            h.pointer(px,py,WM_LBUTTONDOWN);f.frame();check(f.editor.captured==id,"HUD real rendered rectangle acquires sole pointer capture");
            h.pointer(px+40,py+25);f.frame();check(close(f.values[id].x,40)&&close(f.values[id].y,25),"HUD drag follows mapped viewport pointer");
            h.pointer(-100,float(sizes[3]+200),WM_LBUTTONUP);f.frame();f.frame();
            n=f.geometry.nodes[id].bounds;check(f.editor.captured<0&&close(n.x,0)&&close(n.bottom(),float(sizes[3])),"HUD release outside component clamps and releases");
            auto saved=f.values[id];h.pointer(450,450);f.frame();check(close(saved.x,f.values[id].x)&&close(saved.y,f.values[id].y),"HUD released pointer never resumes movement");
            f.editor.finish(f.links,false);check(f.values[id].x==0&&f.values[id].y==0,"HUD cancel-all restores session offsets");
        }
        for(const auto corner:{hud::corner::top_left,hud::corner::top_right,hud::corner::bottom_left,hud::corner::bottom_right}){
            fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();auto before=f.geometry.nodes[0].bounds;
            auto hit=hud::handle(before,corner);float px=hit.x+hit.w/2,py=hit.y+hit.h/2;
            h.pointer(px,py,WM_LBUTTONDOWN);f.frame();check(f.editor.resizing==corner,"HUD each visible corner begins resize");
            px+=hud::left(corner)?-19:19;py+=hud::top(corner)?-3:3;h.pointer(px,py);f.frame();
            check(f.values[0].scale>1.05f&&f.values[0].scale<1.2f,"HUD corner input adjusts existing scale");
            h.pointer(px,py,WM_LBUTTONUP);f.frame();f.frame();f.frame();auto after=f.geometry.nodes[0].bounds;
            check(close(hud::left(corner)?before.right():before.x,hud::left(corner)?after.right():after.x)&&
                  close(hud::top(corner)?before.bottom():before.y,hud::top(corner)?after.bottom():after.y),"HUD resize settles with opposite corner anchored to measured geometry");
        }
        for(const auto message:{WM_KILLFOCUS,WM_CANCELMODE,WM_CAPTURECHANGED}){
            fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();h.pointer(100,32,WM_LBUTTONDOWN);f.frame();h.pointer(150,70);f.frame();
            xui::wndproc(message,0,0);f.frame();check(f.editor.captured<0&&f.values[0].x==0&&f.values[0].y==0,"HUD Win32 cancellation restores gesture and clears capture");
        }
        {fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();h.pointer(100,32,WM_LBUTTONDOWN);f.frame();h.pointer(150,70);f.frame();
         xui::wndproc(WM_KEYDOWN,VK_ESCAPE,0);f.frame();check(f.editor.open&&f.editor.captured<0&&f.values[0].x==0,"HUD Escape restores only active gesture");
         xui::wndproc(WM_KEYUP,VK_ESCAPE,0);h.pointer(150,70,WM_LBUTTONUP);f.frame();xui::wndproc(WM_KEYDOWN,VK_ESCAPE,0);f.frame();check(!f.editor.open,"HUD second Escape cancels editing session");xui::wndproc(WM_KEYUP,VK_ESCAPE,0);f.frame();}
        {fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();h.pointer(100,32,WM_LBUTTONDOWN);f.frame();h.pointer(150,70);f.frame();f.visible[0]=false;f.frame();
         check(f.editor.captured<0&&f.values[0].x==0&&!f.geometry.nodes[0].visible,"HUD disappearance cancels capture without stale region or placeholder");h.pointer(100,32,WM_LBUTTONUP);f.frame();}
        {fixture f;f.visible.fill(false);h.pointer(100,32,WM_LBUTTONDOWN);f.frame();check(f.editor.captured<0,"HUD hidden nodes never become selectable");h.pointer(100,32,WM_LBUTTONUP);f.frame();}
        {fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();h.pointer(100,32,WM_LBUTTONDOWN);f.frame();h.pointer(150,70);f.frame();h.pointer(150,70,WM_LBUTTONUP);f.frame();
         const auto gap=xui::ctx().style.item_spacing_x;
         h.pointer(f.toolbar.x+10+78+gap+45,f.toolbar.y+84,WM_LBUTTONDOWN);f.frame();
         check(!f.editor.open&&f.values[0].x==0&&f.values[0].y==0,"HUD Cancel toolbar button restores completed session edits");h.pointer(0,0,WM_LBUTTONUP);f.frame();}
        {fixture f;h.pointer(0,0,WM_LBUTTONUP);f.frame();const float x=f.toolbar.x,y=f.toolbar.y;
         h.pointer(x+20,y+20,WM_LBUTTONDOWN);f.frame();h.pointer(x+60,y+45);f.frame();check(f.toolbar.dragging&&close(f.toolbar.x,x+40),"HUD toolbar uses independent visible drag strip");
         f.frame(true);check(!f.toolbar.dragging&&close(f.toolbar.x,x)&&xui::ctx().active_window==xui::null_id,"HUD toolbar missing-release recovery clears capture");h.pointer(x+60,y+45,WM_LBUTTONUP);f.frame();
         const auto gap=xui::ctx().style.item_spacing_x;
         h.pointer(f.toolbar.x+10+78+gap+90+gap+50,f.toolbar.y+84,WM_LBUTTONDOWN);f.frame();check(f.editor.snapping&&!f.toolbar.dragging,"HUD snapping toolbar button is not stolen by window drag");
         h.pointer(f.toolbar.x+10,f.toolbar.y+84,WM_LBUTTONUP);f.frame();
         h.pointer(f.toolbar.x+35,f.toolbar.y+84,WM_LBUTTONDOWN);f.frame();check(!f.editor.open,"HUD Done toolbar button completes editor");h.pointer(0,0,WM_LBUTTONUP);f.frame();}
    }
    std::cout<<"HUD_EDITOR_BACKEND=D3D11_WARP_XUI\nHUD_EDITOR_INPUT=synthetic_window_messages\nHUD_GAME_RUNTIME_TESTED=NO\n";
}
