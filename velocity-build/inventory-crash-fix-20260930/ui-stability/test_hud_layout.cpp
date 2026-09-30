#include <core/rendering/hud_layout.hpp>
#include <cassert>
#include <iostream>
#include <limits>
using namespace rendering::hud_layout;
static unsigned checks=0;
static void checked(bool ok,int line){if(!ok){std::cerr<<"Line "<<line<<": Failed check "<<checks+1<<'\n';std::abort();} ++checks;}
#define check(ok) checked((ok),__LINE__)
static bool near(float a,float b){return std::abs(a-b)<0.01f;}
struct fixture {
    registry r; editor e;
    std::array<value,count> values{};
    bindings b{};
    std::array<rect,count> bases{{{100,80,140,24},{390,210,150,72},{700,90,174,75},{300,490,165,90},{460,10,180,24},{35,620,340,28}}};
    std::array<bool,count> visible{{true,true,true,true,true,true}};
    float vw=1280,vh=720;
    fixture(){for(std::size_t i=0;i<count;++i)b[i]={&values[i].x,&values[i].y,&values[i].scale,true};render();e.start(b);e.snapping=false;}
    void render(){
        r.begin_frame();
        for(std::size_t i=0;i<count;++i)if(visible[i] && b[i].enabled){
            auto n=bases[i]; const float s=std::clamp(values[i].scale,0.85f,1.35f);
            // Intentionally mixed fixed-text + scaled padding width, like production pills.
            n.w=80+(n.w-80)*s;n.h*=s;
            n.x=rendering::viewport::clamp_origin(n.x+values[i].x,n.w,vw);
            n.y=rendering::viewport::clamp_origin(n.y+values[i].y,n.h,vh);
            r.add(static_cast<component>(i),n,bases[i].x,bases[i].y);
        }
    }
    void step(pointer p,bool block=false){render();e.update(r,b,p,vw,vh,block);}
    pointer press(int i,corner c=corner::none){const auto n=r.nodes[i].bounds;auto h=c==corner::none?rect{n.x+n.w*.5f,n.y+n.h*.5f,0,0}:handle(n,c);pointer p{h.x+h.w*.5f,h.y+h.h*.5f,true,true};step(p);return p;}
};
int main(){
    // Registry is a union of current-frame draw rectangles, never stale previews.
    registry rg;rg.add(component::keybinds,{10,20,60,20},-20,10);rg.add(component::keybinds,{5,50,100,20},-25,40);
    check(rg.nodes[1].bounds.x==5&&rg.nodes[1].bounds.h==50&&rg.nodes[1].base_x==-25);
    rg.begin_frame();check(!rg.nodes[1].visible);
    rg.add(component::watermark,{0,0,-1,20},0,0);check(!rg.nodes[0].visible);
    rg.add(component::watermark,{0,0,20,20},std::numeric_limits<float>::infinity(),0);check(!rg.nodes[0].visible);
    for(int id=0;id<static_cast<int>(count);++id){
        fixture f;auto p=f.press(id);check(f.e.captured==id&&f.e.resizing==corner::none);
        p.pressed=false;p.x+=40;p.y+=30;f.step(p);check(near(f.values[id].x,40)&&near(f.values[id].y,30));
        p.x=-300;p.y=1700;f.step(p);f.render();check(near(f.r.nodes[id].bounds.x,0)&&near(f.r.nodes[id].bounds.bottom(),f.vh));
        p.down=false;p.released=true;f.step(p);check(f.e.captured==-1);auto saved=f.values[id];
        f.step({900,400,false,false,false});check(near(saved.x,f.values[id].x)&&near(saved.y,f.values[id].y));
        f.e.finish(f.b,true);check(!f.e.open&&near(saved.x,f.values[id].x));
    }
    for(auto c:{corner::top_left,corner::top_right,corner::bottom_left,corner::bottom_right})for(int id=0;id<static_cast<int>(count);++id){
        fixture f;f.bases[id]={300,300,200,60};f.render();auto before=f.r.nodes[id].bounds;
        auto p=f.press(id,c);check(f.e.resizing==c);p.pressed=false;p.x+=(left(c)?-20:20);p.y+=(top(c)?-6:6);f.step(p);
        check(near(f.values[id].scale,1.1f));f.step(p);f.render();
        const auto after=f.r.nodes[id].bounds;
        check(near(left(c)?before.right():before.x,left(c)?after.right():after.x));
        check(near(top(c)?before.bottom():before.y,top(c)?after.bottom():after.y));
        p.x+=(left(c)?-2000:2000);p.y+=(top(c)?-2000:2000);f.step(p);check(f.values[id].scale==1.35f);
        p.down=false;p.released=true;f.step(p);f.step({});f.render();check(!f.e.settle_resize&&f.e.captured==-1);
        check(near(left(c)?before.right():before.x,left(c)?f.r.nodes[id].bounds.right():f.r.nodes[id].bounds.x));
        auto q=f.press(id,c);q.pressed=false;q.x+=(left(c)?2000:-2000);q.y+=(top(c)?2000:-2000);f.step(q);check(f.values[id].scale==.85f);
        q.escape=true;f.step(q);check(f.e.captured==-1&&f.values[id].scale==1.35f);
        f.step({0,0,false,false,false,true});check(!f.e.open&&f.values[id].scale==1.0f);
    }
    // Gesture cancellation keeps previous completed edits; cancel-all restores the session.
    {fixture f;auto p=f.press(0);p.pressed=false;p.x+=25;f.step(p);p.down=false;p.released=true;f.step(p);
     p=f.press(1);p.pressed=false;p.x+=32;f.step(p);p.cancel=true;f.step(p);check(f.e.captured==-1&&f.values[1].x==0&&f.values[0].x==25);
     f.e.finish(f.b,false);check(f.values[0].x==0&&f.values[1].x==0);}
    // Disable, disappearance, missing release, viewport change, and explicit blocked region.
    for(int mode=0;mode<5;++mode){fixture f;auto p=f.press(0);p.pressed=false;p.x+=40;f.step(p);
      if(mode==0) f.visible[0]=false;
      if(mode==1) f.b[0].enabled=false;
      if(mode==2) p.down=false;
      if(mode==3) f.vw=720;
      if(mode==4) p.cancel=true;
      f.step(p);check(f.e.captured==-1&&f.values[0].x==0);}
    {fixture f;const auto n=f.r.nodes[0].bounds;f.step({n.x+60,n.y+12,true,true},true);check(f.e.captured==-1);}
    {fixture f;f.visible.fill(false);f.step({150,92,true,true});check(f.e.captured==-1);}
    // Latest actual draw order wins for overlaps.
    {fixture f;f.bases[1]=f.bases[0];f.render();f.press(0);check(f.e.captured==1);}
    // Saved offsets beyond display clamp should not create a dead drag range.
    {fixture f;f.values[0].x=90000;f.values[0].y=-90000;f.render();auto p=f.press(0);p.pressed=false;p.x-=50;p.y+=30;f.step(p);f.render();check(near(f.r.nodes[0].bounds.right(),f.vw-50)&&near(f.r.nodes[0].bounds.y,30));p.escape=true;f.step(p);check(f.values[0].x==90000&&f.values[0].y==-90000);}
    // Snaps to other actual nodes and viewport centers. Shift bypasses snapping.
    {fixture f;f.visible[2]=f.visible[3]=f.visible[4]=f.visible[5]=false;f.render();f.e.snapping=true;auto p=f.press(0);p.pressed=false;p.x+=285;p.y+=126;f.step(p);f.render();check(f.e.vertical.visible&&f.e.horizontal.visible);check(near(f.r.nodes[0].bounds.x,390)&&near(f.r.nodes[0].bounds.y,210));
     p.free_move=true;f.step(p);f.render();check(!f.e.vertical.visible&&!f.e.horizontal.visible&&near(f.r.nodes[0].bounds.x,385));}
    {fixture f;f.e.snapping=true;auto p=f.press(0);p.pressed=false;p.x+=466;f.step(p);f.render();check(f.e.vertical.visible&&near(f.r.nodes[0].bounds.x+70,640));}
    // Viewport shrink/oversized widgets never construct an inverted clamp range.
    {fixture f;f.vw=90;f.vh=18;f.e.viewport_w=f.e.viewport_h=0;f.render();auto p=f.press(0);p.pressed=false;p.x+=400;p.y+=400;f.step(p);f.render();check(f.r.nodes[0].bounds.x==0&&f.r.nodes[0].bounds.y==0);}
    std::cout<<"PASS HUD geometry/state: "<<checks<<" checks; six live nodes, 24 corner flows, restore/capture/snapping/viewport/hidden-state\n";
}
