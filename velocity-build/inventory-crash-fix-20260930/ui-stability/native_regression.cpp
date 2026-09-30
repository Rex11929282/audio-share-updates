#include <core/rendering/viewport_bounds.hpp>
// Executes against the real production XUI implementation in a D3D11 WARP host.
// It does not load the game DLL or call any game systems.
static void ui_stability_regression(){
 xdraw::begin_frame();xui::begin();
 float x=40,y=40,w=846,h=640;
 check(xui::begin_window("MCB##stability",x,y,w,h,false),"stability root window");
 std::vector<xui::window_state*> retained;
 retained.push_back(xui::layout::current_window());
 for(int i=0;i<64;++i){
  check(xui::begin_child("##nested_stability_"+std::to_string(i),200,100,false),"nested child opens");
  retained.push_back(xui::layout::current_window());
  for(std::size_t j=0;j<retained.size();++j)
   check(retained[j]==&xui::ctx().windows[j],"nested push preserves all parent window addresses");
 }
 for(int i=0;i<64;++i){xui::end_child();retained.pop_back();check(retained.back()==xui::layout::current_window(),"nested pop restores valid parent pointer");}
 check(xui::ctx().windows.size()==1,"nested child stack balances");
 xui::layout::set_cursor(10,50);
 static xui::setting a{},b{};
 xui::checkbox("显示图片##stability_a",a);
 const auto first=xui::layout::current_window()->last_item;
 const auto label_w=xdraw::measure_text("显示图片").first;
 check(first.w>=xui::ctx().style.checkbox_size+xui::ctx().style.item_spacing_x+label_w-1.0f,"checkbox reserves its visible label width");
 xui::layout::same_line();xui::checkbox("紧凑卡片##stability_b",b);
 const auto second=xui::layout::current_window()->last_item;
 check(second.x>=first.right()+xui::ctx().style.item_spacing_x-1.0f,"same-line checkbox starts after prior label");
 xui::layout::new_line();
 xui::button("显示选项##stability_tools",190,28);
 auto* parent=xui::layout::current_window();
 check(xui::begin_child("##stability_tools_panel",300,0,false),"inventory options child opens");
 int order{},columns=5;
 xui::combo("排序##stability_order",order,std::array<const char*,3>{"名称","编号","已应用优先"}.data(),3,132);
 xui::layout::new_line();xui::slider_int("列数##stability_columns",columns,3,7,"%d");
 xui::checkbox("显示图片##stability_images",a);xui::checkbox("紧凑卡片##stability_compact",b);
 xui::end_child();check(parent==xui::layout::current_window(),"inventory options preserve grid parent pointer");
 const auto toolbar_bottom=parent->bounds.y+parent->last_item.bottom();
 xui::layout::separator();const auto grid=xui::layout::item(300,180);
 check(grid.y>=toolbar_bottom,"inventory grid starts after options toolbar");
 xui::end_window();xui::end();xdraw::end_frame();
 check(xui::ctx().windows.empty(),"stability frame leaves no dangling child stack");
}

static void ui_autoheight_regression(){
 float last_wrapper_h{};
 for(int frame=0;frame<3;++frame){
  xdraw::begin_frame();xui::begin();
  float x=40,y=40,w=846,h=640;
  check(xui::begin_window("MCB##autoheight",x,y,w,h,false),"autoheight root opens");
  xui::layout::set_cursor(10,50);
  check(xui::begin_child("##autoheight_scroll",600,480,true),"autoheight scroll parent opens");
  check(xui::begin_child("##autoheight_wrapper",570,0,false),"autoheight section wrapper opens");
  last_wrapper_h=xui::layout::current_window()->bounds.h;
  check(xui::layout::current_window()->auto_height,"section wrapper records autoheight mode");
  check(xui::begin_child("##autoheight_content",540,0,false),"autoheight content opens");
  for(int i=0;i<30;++i)xui::button("选项##autoheight_"+std::to_string(i),240,28);
  xui::end_child();xui::end_child();xui::end_child();
  xui::end_window();xui::end();xdraw::end_frame();
 }
 check(last_wrapper_h>800,"nested autoheight section grows beyond initial placeholder inside scroll page");
}

static void ui_viewport_regression(){
 for(float viewport : {1.0f,480.0f,720.0f,1080.0f,1920.0f})
  for(float extent : {0.0f,16.0f,200.0f,2500.0f})
   for(float offset : {-10000.0f,-1.0f,0.0f,10.0f,10000.0f}){
    const auto placed=rendering::viewport::clamp_origin(offset,extent,viewport);
    check(placed>=0.0f,"HUD origin never negative");
    check(extent>viewport ? placed==0.0f : placed+extent<=viewport,"HUD bounds fit viewport or align oversized widget at edge");
   }
}

#define UI_ASSERT(...) check((__VA_ARGS__), #__VA_ARGS__)
#include <core/rendering/impl/menu/menu.ui-model.hpp>
#include <cassert>
#include <iostream>
#include <iterator>
#include <string_view>
using namespace rendering::ui_model;
static void ui_model_regression() {
    section_focus focus{};
    focus.request(5);
    UI_ASSERT(focus.update(100,false) && focus.index==5);
    UI_ASSERT(focus.update(100,false) && focus.index==5);
    UI_ASSERT(focus.update(900,false) && focus.index==5);
    UI_ASSERT(focus.update(900,false) && focus.index==5);
    UI_ASSERT(focus.update(900,false) && focus.index==-1);
    focus.request(3);UI_ASSERT(!focus.update(100,true) && focus.index==-1);
    UI_ASSERT(single_column(759.99f)); UI_ASSERT(!single_column(760));
    UI_ASSERT(single_column(320)); UI_ASSERT(!single_column(1024));
    UI_ASSERT(sidebar_width(false)==172); UI_ASSERT(sidebar_width(true)==58);
    for(float width : {700.f,740.f,840.f,980.f,1200.f}) {
        auto custom=width;
        for(int toggle=0;toggle<100;++toggle) {
            const auto sidebar=sidebar_width(toggle%2!=0); (void)sidebar;
            custom=clamp_window_width(custom,1896);
            UI_ASSERT(custom==width);
        }
    }
    UI_ASSERT(clamp_window_width(980,656)==656);
    UI_ASSERT(clamp_window_width(600,1896)==700);
    UI_ASSERT(category_route("anti aim").combat_tab==2);
    UI_ASSERT(category_route("ragebot - rifle").subtab==2);
    UI_ASSERT(category_route("legitbot - sniper").combat_tab==1);
    UI_ASSERT(category_route("legitbot - sniper").subtab==4);
    UI_ASSERT(category_route("esp team health").page==3 && category_route("esp team health").subtab==1);
    UI_ASSERT(category_route("chams team").subtab==1);
    UI_ASSERT(category_route("chams local").subtab==2);
    UI_ASSERT(category_route("viewmodel", "viewmodel adjust").misc_section==3);
    UI_ASSERT(category_route("viewmodel", "weapon chams").subtab==2);
    UI_ASSERT(category_route("camera").misc_section==3);
    UI_ASSERT(category_route("removals").misc_section==5);
    UI_ASSERT(category_route("scope overlay").misc_section==4);
    UI_ASSERT(category_route("weather").misc_section==2);
    UI_ASSERT(category_route("other esp").misc_section==1);
    UI_ASSERT(category_route("esp items").misc_section==1);
    UI_ASSERT(category_route("chams items").misc_section==1);
    UI_ASSERT(category_route("watermark").page==9 && category_route("watermark").profile_tab==0);
    UI_ASSERT(category_route("profile").page==9 && category_route("profile").profile_tab==1);
    UI_ASSERT(category_route("widgets").layout_editor);
    UI_ASSERT(category_route("changer ui").page==5);
    UI_ASSERT(category_route("knife").subtab==7);
    UI_ASSERT(category_route("glove").subtab==8);
    UI_ASSERT(category_route("agent").subtab==9);
    UI_ASSERT(category_route("lua").page==7);
    UI_ASSERT(category_route("config").page==8);
    int sliders=0,combos=0,actions=0,inventory_entries=0,lua_entries=0,cfg_entries=0;
    bool sort=false,columns=false,save=false,remove=false,import_script=false,opacity=false;
    for(const auto& c:controls) {
        UI_ASSERT(c.label[0] && c.category[0]);
        UI_ASSERT(c.route.page>=0 && c.route.page<=9);
        UI_ASSERT(c.route.tab>=0 && c.route.tab<=6);
        UI_ASSERT(c.route.misc_section<6);
        UI_ASSERT(std::string_view(c.category).find("快捷配置")==std::string_view::npos);
        const std::string_view kind=c.kind,name=c.label;
        sliders+=has(kind,"slider"); combos+=has(kind,"combo"); actions+=has(kind,"button");
        inventory_entries+=c.route.page==5;lua_entries+=c.route.page==7;cfg_entries+=c.route.page==8;
        sort|=name=="排序" && c.route.page==5;columns|=name=="列数" && c.route.page==5;
        save|=name=="保存" && c.route.page==8;remove|=name=="删除" && c.route.page==8;
        import_script|=name=="导入脚本" && c.route.page==7;
        opacity|=name=="背景透明度" && c.route.page==9 && c.route.profile_tab==1;
    }
    UI_ASSERT(sliders>50 && combos>15 && actions>=10);
    UI_ASSERT(inventory_entries>=10 && lua_entries>=4 && cfg_entries>=7);
    UI_ASSERT(sort && columns && save && remove && import_script && opacity);
    std::cout << "PASS: responsive boundary, 500 sidebar toggles, 27 category routes, " << std::size(controls) << " navigation entries\n";
}

#undef UI_ASSERT
