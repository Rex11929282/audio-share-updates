"""Checked final source reconciliation and regression generation."""
from pathlib import Path
import hashlib,json,re,sys
root=Path(sys.argv[1]);root=root/'cs2/MCB-CS2' if (root/'cs2/MCB-CS2/project').is_dir() else root
p=root/'project';changes=[]
def replace(t,a,b,n=1):
    if t.count(a)!=n:raise ValueError(f'Unexpected anchor count {t.count(a)}: {a}')
    return t.replace(a,b)
def edit(name,fn):
    f=p/name;s=f.read_text(encoding='utf-8-sig');o=fn(s);f.write_text(o,encoding='utf-8')
    changes.append({'path':name,'before':hashlib.sha256(s.encode()).hexdigest(),'after':hashlib.sha256(o.encode()).hexdigest()})
def presets(t):
    t=replace(t,'group.fov.value - 5.0f','group.fov.value - 1.5f')
    t=replace(t,'group.smooth.value != 5','group.smooth.value != 25')
    t=replace(t,'group.hitboxes[0] != true','group.hitboxes[0] != false')
    t=replace(t,'if (group.hitboxes[i])','if (group.hitboxes[i] != (i == 1 || i == 2))')
    t=replace(t,'group.doubletap.value != expected_hvh','group.doubletap.value')
    t=replace(t,'group.min_damage.value != 101','group.min_damage.value != (&group == &combat.m_ragebot.groups[4] ? 40 : 20)')
    t=replace(t,'!aa.hide_shots.value','aa.hide_shots.value')
    translations={
      'CFG verification failed: missing fields object':'參數驗證失敗：缺少欄位物件',
      'CFG verification failed: registry field count changed':'參數驗證失敗：欄位數量改變',
      'CFG verification failed: invalid registry key':'參數驗證失敗：無效的內部鍵',
      'CFG verification failed: non-preset field changed':'參數驗證失敗：非預設欄位被修改',
      'CFG verification failed: mode exclusivity mismatch':'參數驗證失敗：模式互斥不符',
      'CFG verification failed: legit group mismatch':'參數驗證失敗：低調組內容不符',
      'CFG verification failed: legit hitbox mismatch':'參數驗證失敗：低調命中部位不符',
      'CFG verification failed: rage group mismatch':'參數驗證失敗：進攻組內容不符',
      'CFG verification failed: rage hitbox mismatch':'參數驗證失敗：進攻命中部位不符',
      'CFG verification failed: HVH anti-aim mismatch':'參數驗證失敗：私人對抗方向內容不符',
      '; rollback verification FAILED':'；還原驗證失敗',
      'Native CFG save failed':'原生參數保存失敗',
      'Native CFG readback verification failed':'原生參數讀回驗證失敗',
      'MCB Legit: PARTIAL preset applied; FULL native CFG saved':'已套用低調欄位，並保存完整原生設定',
      'MCB Rage: PARTIAL preset applied; FULL native CFG saved':'已套用進攻欄位，並保存完整原生設定',
      'MCB HVH: PARTIAL preset applied; FULL native CFG saved':'已套用私人對抗欄位，並保存完整原生設定',
      'MCB Legit':'MCB 低調','MCB Rage':'MCB 進攻','MCB HVH':'MCB 私人對抗','MCB Preset':'MCB 預設'}
    for a,b in translations.items():t=t.replace(a,b)
    return t
edit('core/mcb/mcb_presets.cpp',presets)
edit('core/settings.hpp',lambda t:replace(replace(t,'xui::setting enabled{ true, {}, "anti aim", "anti aim" };','xui::setting enabled{ false, {}, "anti aim", "anti aim" };'),'xui::setting hide_shots{ true, {}, "hide onshot", "anti aim" };','xui::setting hide_shots{ false, {}, "hide onshot", "anti aim" };'))
def labels(t):
    at=t.index('[[nodiscard]] inline std::string_view tr( std::string_view s )');at=t.index('{',at)+1
    pairs={'peek assistance':'探身輔助','spectator':'觀戰者','agent':'探員','glove':'手套','player enemies':'敵方玩家'}
    code='\n'
    for a,b in pairs.items():code+=f'        if (s == "{a}") return "{b}";\n'
    return t[:at]+code+t[at:]
edit('core/localization/zh_tw.hpp',labels)
man=root/'MCB_INTEGRATION_MANIFEST.json';j=json.loads(man.read_text(encoding='utf-8'));j['final_reconciliation']=changes;man.write_text(json.dumps(j,ensure_ascii=False,indent=2),encoding='utf-8')
# Compile the actual source's apply and verification functions against typed synthetic storage.
text=(p/'core/mcb/mcb_presets.cpp').read_text(encoding='utf-8')
body=text[text.index('        using key_set'):text.index('        bool verify_untouched')]
start=text.index('        bool verify_mode');end=text.index('        bool rollback',start)
body+=text[start:end]
preamble=r'''
#include <array>
#include <string>
#include <string_view>
#include <unordered_set>
#include <cstdint>
#include <cmath>
#include <cassert>
#include <iostream>
namespace xui { enum class bind_mode{toggle,hold_on};struct setting {bool value{};struct binding {int key{};bind_mode mode{};bool active{};}bind;std::string category,name;}; }
namespace config { namespace detail {inline std::uint32_t make_key(std::string_view a,std::string_view b){std::uint32_t x=2166136261u;for(char c:a)x=(x^c)*16777619u;for(char c:b)x=(x^c)*16777619u;return x;} }
template<class T>struct val{T value{};val& operator=(T v){value=v;return *this;}};
template<class T>using enm=val<T>;
template<std::uint32_t N>struct bools{std::array<bool,N> a{};bool& operator[](std::size_t i){return a[i];}bool operator[](std::size_t i)const{return a[i];}}; }
namespace settings {struct combat {
struct legitbot {struct group {xui::setting aimbot,rcs,standalone_rcs,triggerbot,trigger_head_only,give_me_your_seed,autowall,visualize_fov;config::val<float> fov;config::val<int> smooth,rcs_min,rcs_max,standalone_rcs_strength,standalone_rcs_min,standalone_rcs_max,trigger_delay,trigger_hitchance,min_damage;config::bools<5> hitboxes;};xui::setting enabled;std::array<group,6> groups;}m_legitbot;
struct ragebot {struct group{xui::setting silent,no_spread,doubletap,body_aim,force_shot_air,force_shot,min_damage_override,hitchance_override,dynamic_pointscale,debug_multipoints;config::val<float> max_fov,pointscale;config::val<int> hitchance,min_damage,min_damage_override_value,hitchance_override_value;config::bools<6> hitboxes;};xui::setting enabled;std::array<group,6>groups;}m_ragebot;
struct antiaim {enum class pitch_mode:std::uint8_t{none,down,up};xui::setting enabled,auto_yaw_adjust,manual_left,manual_right,hide_shots,avoid_backstab,direction_indicator,direction_indicator_glow;config::enm<pitch_mode>pitch;config::val<float>direction_indicator_glow_strength;}m_antiaim;
struct lagcomp{xui::setting extrapolation;config::val<int>max_backtrack_ticks,max_extrapolate_ticks;}m_lagcomp;
};inline combat g_combat;inline void finalize_binds(){} }
namespace mcb::presets {enum class kind{legit,rage,hvh};namespace {
'''
main=r'''
}}
int main(){using namespace mcb::presets;unsigned checks=0;
 for(auto mode:{kind::legit,kind::rage,kind::hvh}){
  settings::g_combat={};key_set keys;std::string e;apply(mode,keys);assert(verify_mode(mode,e));++checks;
  auto& c=settings::g_combat;
  if(mode==kind::legit){for(auto& g:c.m_legitbot.groups){assert(g.fov.value==1.5f&&g.smooth.value==25&&g.trigger_delay.value==120&&!g.autowall.value);++checks;}c.m_legitbot.groups[0].fov=9.f;assert(!verify_mode(mode,e));++checks;}
  else {for(unsigned i=0;i<6;i++){const auto& g=c.m_ragebot.groups[i];assert(!g.doubletap.value&&g.min_damage.value==(i==4?40:20));++checks;}c.m_ragebot.groups[0].min_damage=999;assert(!verify_mode(mode,e));++checks;}
  apply(mode,keys);assert(verify_mode(mode,e));++checks;c.m_ragebot.enabled.value=!c.m_ragebot.enabled.value;assert(!verify_mode(mode,e));++checks;
 }
 std::cout<<"native_preset_source_checks="<<checks<<"\nactual_apply_and_verify_functions=PASS\nregistry_backend=SYNTHETIC_NOT_WINDOWS_READBACK\n";
}
'''
Path('mcb-native-patch/test_presets.cpp').write_text(preamble+body+main,encoding='utf-8')
print('Preset apply/verification reconciled; fresh aim defaults inactive; native feedback translated')
