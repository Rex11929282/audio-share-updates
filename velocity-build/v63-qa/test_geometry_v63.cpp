#include <core/rendering/ui_geometry_v63.hpp>
#include <iostream>
#include <stdexcept>
using namespace mcb_ui_v63;
int count=0;
void check(bool x,const char* what){if(!x)throw std::runtime_error(what);++count;std::cout<<"PASS "<<what<<'\n';}
bool near(float a,float b){return std::abs(a-b)<0.002f;}
int main(){try{
check(panel_w==846&&panel_h==682&&sidebar_w+643==panel_w,"fixed panel and workspace geometry");
for(auto t:{point{1920,1080},point{1440,1080},point{1280,720},point{2560,1440}}){auto m=mapping::make(1920,1080,t.x,t.y);auto a=m.render({215,77});auto b=m.client(a);check(near(b.x,215)&&near(b.y,77),"logical-render inverse");auto extent=m.client(m.render({panel_w,panel_h}));check(near(extent.x,panel_w)&&near(extent.y,panel_h),"render target resize preserves panel size");}
auto m=mapping::make(0,0,1280,720);check(m.sx==1&&m.sy==1,"invalid viewport avoids division by zero");
rect a{0,0,20,20},b{20,0,20,20};check(!a.contains(20,10)&&b.contains(20,10),"exclusive shared edge");
check(!rect{0,0,0,10}.contains(0,0),"zero width cannot receive input");
auto p=clamp_panel({9999,9999},1920,1080);check(p.x==1920-panel_w&&p.y==1080-panel_h,"large-window position clamp only");
p=clamp_panel({-999,-999},640,480);check(panel_w==846&&panel_h==682&&p.x==640-panel_w&&p.y==480-panel_h,"small window clips instead of deforming");
activation c;check(!c.test(1,true,false,true,true,true)&&c.owner==1,"press arms one owner without activation");check(!c.test(2,true,false,true,true,true)&&c.owner==1,"second control cannot steal press");
check(c.test(1,false,true,true,true,true)&&c.owner==0,"release same control activates once");check(!c.test(1,false,true,true,true,true),"repeat release does not activate");
c.test(1,true,false,true,true,true);check(!c.test(1,false,true,true,false,true)&&c.owner==0,"release outside cancels");
c.test(1,true,false,false,false,true);check(!c.test(1,false,true,false,true,true),"down outside up inside is not a click");
c.test(1,true,false,true,true,true);c.cancel();check(!c.test(1,false,true,true,true,true),"focus loss cancels armed click");
check(!c.test(1,true,true,true,true,false),"disabled control cannot activate");
check(c.test(1,true,true,true,true,true),"coalesced down-up event not lost");
std::cout<<"geometry_tests_passed="<<count<<'\n';return 0;
}catch(std::exception& e){std::cerr<<"FAIL "<<e.what()<<'\n';return 1;}}
