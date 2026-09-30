#include <core/rendering/hotkey_conflicts.hpp>
#include <array>
#include <cassert>
#include <iostream>
struct Setting {int value{};};
struct RageGroup {Setting silent,no_spread,doubletap,body_aim,force_shot_air,force_shot,min_damage_override,hitchance_override,dynamic_pointscale,debug_multipoints;};
struct LegitGroup {Setting aimbot,rcs,standalone_rcs,triggerbot,trigger_head_only,give_me_your_seed,autowall,visualize_fov;};
template<class Group> struct Config {struct Weapon {Setting override_enabled; Group values;};Setting enabled;Group global;std::array<Group,6> groups;std::array<Weapon,34> weapons;std::array<Setting,6> category_override;};
int main(){
 using namespace rendering::hotkey_ui;
 Config<RageGroup> rage{};Config<LegitGroup> legit{};
 const auto map=describe_groups(rage,legit);
 assert(map.size()==17*(1+6+34));
 assert(!map.contains(&rage.global.debug_multipoints));assert(!map.contains(&rage.enabled));assert(!map.contains(&legit.enabled));
 assert(!map.contains(&rage.category_override[0]));assert(!map.contains(&rage.weapons[0].override_enabled));
 assert(overlaps(map.at(&rage.groups[0].silent),map.at(&rage.groups[0].doubletap)));
 assert(!overlaps(map.at(&rage.groups[0].silent),map.at(&rage.groups[1].doubletap)));
 assert(!overlaps(map.at(&rage.groups[0].silent),map.at(&rage.weapons[1].values.silent)));
 assert(!overlaps(map.at(&rage.global.silent),map.at(&rage.groups[0].silent)));
 assert(!overlaps(map.at(&rage.global.silent),map.at(&legit.global.aimbot)));
 assert(overlaps(scope{},map.at(&rage.global.silent)));assert(overlaps(scope{},scope{}));
 int checks=0;
 for(const auto& [a,sa]:map)for(const auto& [b,sb]:map){
  ++checks;assert(overlaps(sa,sb)==overlaps(sb,sa));
  assert(overlaps(sa,sb,true));
  if(a==b)assert(overlaps(sa,sb));
 }
 std::cout<<"PASS actual group fields=697, pairwise scope checks="<<checks<<", masters/overrides/exclusions retained\n";
}
