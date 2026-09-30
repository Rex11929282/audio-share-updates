#include <core/rendering/impl/menu/menu.ui-model.hpp>
#include <cassert>
#include <iostream>
#include <iterator>
#include <string_view>
using namespace rendering::ui_model;
int main() {
    section_focus focus{};
    focus.request(5);
    assert(focus.update(100,false) && focus.index==5);
    assert(focus.update(100,false) && focus.index==5);
    assert(focus.update(900,false) && focus.index==5);
    assert(focus.update(900,false) && focus.index==5);
    assert(focus.update(900,false) && focus.index==-1);
    focus.request(3);assert(!focus.update(100,true) && focus.index==-1);
    assert(single_column(759.99f)); assert(!single_column(760));
    assert(single_column(320)); assert(!single_column(1024));
    assert(sidebar_width(false)==172); assert(sidebar_width(true)==58);
    for(float width : {700.f,740.f,840.f,980.f,1200.f}) {
        auto custom=width;
        for(int toggle=0;toggle<100;++toggle) {
            const auto sidebar=sidebar_width(toggle%2!=0); (void)sidebar;
            custom=clamp_window_width(custom,1896);
            assert(custom==width);
        }
    }
    assert(clamp_window_width(980,656)==656);
    assert(clamp_window_width(600,1896)==700);
    assert(category_route("anti aim").combat_tab==2);
    assert(category_route("ragebot - rifle").subtab==2);
    assert(category_route("legitbot - sniper").combat_tab==1);
    assert(category_route("legitbot - sniper").subtab==4);
    assert(category_route("esp team health").page==3 && category_route("esp team health").subtab==1);
    assert(category_route("chams team").subtab==1);
    assert(category_route("chams local").subtab==2);
    assert(category_route("viewmodel", "viewmodel adjust").misc_section==3);
    assert(category_route("viewmodel", "weapon chams").subtab==2);
    assert(category_route("camera").misc_section==3);
    assert(category_route("removals").misc_section==5);
    assert(category_route("scope overlay").misc_section==4);
    assert(category_route("weather").misc_section==2);
    assert(category_route("other esp").misc_section==1);
    assert(category_route("esp items").misc_section==1);
    assert(category_route("chams items").misc_section==1);
    assert(category_route("watermark").page==9 && category_route("watermark").profile_tab==0);
    assert(category_route("profile").page==9 && category_route("profile").profile_tab==1);
    assert(category_route("widgets").layout_editor);
    assert(category_route("changer ui").page==5);
    assert(category_route("knife").subtab==7);
    assert(category_route("glove").subtab==8);
    assert(category_route("agent").subtab==9);
    assert(category_route("lua").page==7);
    assert(category_route("config").page==8);
    int sliders=0,combos=0,actions=0,inventory_entries=0,lua_entries=0,cfg_entries=0;
    bool sort=false,columns=false,save=false,remove=false,import_script=false,opacity=false;
    for(const auto& c:controls) {
        assert(c.label[0] && c.category[0]);
        assert(c.route.page>=0 && c.route.page<=9);
        assert(c.route.tab>=0 && c.route.tab<=6);
        assert(c.route.misc_section<6);
        assert(std::string_view(c.category).find("快捷配置")==std::string_view::npos);
        const std::string_view kind=c.kind,name=c.label;
        sliders+=has(kind,"slider"); combos+=has(kind,"combo"); actions+=has(kind,"button");
        inventory_entries+=c.route.page==5;lua_entries+=c.route.page==7;cfg_entries+=c.route.page==8;
        sort|=name=="排序" && c.route.page==5;columns|=name=="列数" && c.route.page==5;
        save|=name=="保存" && c.route.page==8;remove|=name=="删除" && c.route.page==8;
        import_script|=name=="导入脚本" && c.route.page==7;
        opacity|=name=="背景透明度" && c.route.page==9 && c.route.profile_tab==1;
    }
    assert(sliders>50 && combos>15 && actions>=10);
    assert(inventory_entries>=10 && lua_entries>=4 && cfg_entries>=7);
    assert(sort && columns && save && remove && import_script && opacity);
    std::cout << "PASS: responsive boundary, 500 sidebar toggles, 27 category routes, " << std::size(controls) << " navigation entries\n";
}
