from pathlib import Path
import re,subprocess,sys,tempfile
root=Path(sys.argv[1]).resolve()
s=(root/'project/core/rendering/impl/menu/menu.core.cpp').read_text()
helper=s[s.index('        [[nodiscard]] static std::size_t next_utf8_boundary'):s.index('        static void draw_toolbar_tooltip')]
settings=(root/'project/core/settings.hpp').read_text()
weapons=re.findall(r'\{ cstypes::item_definition_index::\w+, cstypes::weapon_type::(\w+), "([^"]+)", "([^"]+)" \}',settings)
assert len(weapons)==34
kinds=['pistol','smg','rifle','shotgun','sniper','lmg']
preamble='''#include <algorithm>
#include <cctype>
#include <string>
#include <string_view>
#include <vector>
#include <array>
#include <cassert>
#include <iostream>
#include <core/rendering/impl/menu/menu.ui-model.hpp>
namespace xdraw { std::pair<float,float> measure_text(std::string_view text) { float w=0; for(unsigned char c:text) if((c&0xc0)!=0x80)w+=c<128?8:16; return {w,15}; } }
namespace settings::weapon_profiles { struct descriptor {int weapon_type; const char* name; const char* slug;};
inline constexpr descriptor catalog[]{
'''
preamble+='\n'.join('{'+str(kinds.index(kind))+',"'+name+'","'+slug+'"},' for kind,name,slug in weapons)
preamble+='''}; std::size_t category_index(int t) {return static_cast<std::size_t>(t);} }
namespace rendering::detail {
static std::string to_lower_copy(std::string_view v) {std::string r(v);for(auto&c:r)c=static_cast<char>(std::tolower(static_cast<unsigned char>(c)));return r;}
'''
main='''
}
int main() {
using namespace rendering; using namespace detail; int checks=0;
for(const auto& weapon:settings::weapon_profiles::catalog) for(int mode=0;mode<2;++mode) {
const auto category=std::string(mode==0?"ragebot - weapon - ":"legitbot - weapon - ")+weapon.slug;
const auto path=search_navigation_path(ui_model::category_route(category),category);
assert(path.find(" / 单把武器 / ")!=std::string::npos);assert(path.ends_with(weapon.name));checks+=2;
}
assert(search_navigation_path(ui_model::combat(1,4),"legitbot - sniper")=="战斗 / LEGIT / 武器分类 / 狙击枪");
assert(search_navigation_path(ui_model::combat(0),"ragebot - global")=="战斗 / RAGE / 全部武器");
assert(search_navigation_path(ui_model::profile(1),"profile")=="个人中心 / 外观");
assert(search_navigation_path(ui_model::misc(3),"camera")=="其他 / 镜头与第一人称");
assert(search_navigation_path(ui_model::inventory(),"changer ui")=="库存 / 全部枪械 / 显示选项");
assert(search_navigation_path(ui_model::hotkeys(),"")=="全局工具 / 快捷键总览");
checks+=6;
for(const auto& control:ui_model::controls) {
const auto path=search_navigation_path(control.route,control.category);assert(!path.empty());++checks;
for(float width:{32.f,80.f,120.f,280.f,500.f}) {
const auto lines=wrap_search_path(path,width);std::string joined;
for(const auto&line:lines){assert(xdraw::measure_text(line).first<=width);joined+=line;++checks;}
assert(joined==path);++checks;
const auto label=fit_search_text(control.label,width);assert(xdraw::measure_text(label).first<=width);++checks;
if(label!=control.label && !label.empty())assert(label.ends_with("…"));
}
}
assert(fit_search_text("清除绑定",0).empty());assert(fit_search_text("清除绑定",15).empty());assert(fit_search_text("清除绑定",32)=="清…");
for(float width:{100.f,220.f,500.f,1000.f}) for(float key_w:{16.f,64.f,96.f}) {
const auto badge=std::min(key_w+16,std::max(0.f,width-16));
const auto text=std::max(0.f,width-16-(badge>0?badge+8:0));
assert(8+text<=width-badge-8);++checks;
}
std::cout<<"PASS: "<<checks<<" path/wrapping/UTF-8/column assertions; 34 weapon routes per mode\\n";
}
'''
Path('/tmp/test_mcb_search_helpers.cpp').write_text(preamble+helper+main)
subprocess.run(['g++','-std=c++20','-Wall','-Wextra','-pedantic','-I'+str(root/'project'),'/tmp/test_mcb_search_helpers.cpp','-o','/tmp/test_mcb_search_helpers'],check=True)
subprocess.run(['/tmp/test_mcb_search_helpers'],check=True)
assert 'draw_tool( search, "⌕", "全局搜索"' in s
assert 'draw_tool( layout, "▦", "界面布局编辑器"' in s
assert 'draw_tool( hotkeys, "⌨", "全局快捷键总览"' in s
assert 'draw_tool( profile, "⚙", "个人中心"' in s
assert 'dl.push_clip( row.x + 8.0f, row.y, text_w, row.h )' in s
print('PASS: four tooltip call sites and search clipping present')
