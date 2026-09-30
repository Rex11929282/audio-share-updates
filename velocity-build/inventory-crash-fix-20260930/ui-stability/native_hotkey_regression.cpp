#include <core/rendering/hotkey_conflicts.hpp>
static void ui_hotkey_scope_regression(){
 const auto& rage=settings::g_combat.m_ragebot;
 const auto& legit=settings::g_combat.m_legitbot;
 const auto scopes=rendering::hotkey_ui::describe_groups(rage,legit);
 using rendering::hotkey_ui::overlaps;
 check(scopes.size()==9*(1+rage.groups.size()+rage.weapons.size())+8*(1+legit.groups.size()+legit.weapons.size()),"all actual production weapon-group fields have UI scope metadata");
 check(!scopes.contains(&rage.global.debug_multipoints),"potentially lingering debug overlay keeps general conflict handling");
 check(!scopes.contains(&rage.enabled)&&!scopes.contains(&legit.enabled),"master hotkeys keep general conflict handling");
 check(!scopes.contains(&rage.category_override[0])&&!scopes.contains(&rage.weapons[0].override_enabled),"override switches keep general conflict handling");
 check(overlaps(scopes.at(&rage.groups[0].silent),scopes.at(&rage.groups[0].doubletap)),"same-group shared keys warn");
 check(!overlaps(scopes.at(&rage.groups[0].silent),scopes.at(&rage.groups[1].silent)),"distinct replacement groups do not falsely warn");
 check(!overlaps(scopes.at(&rage.global.silent),scopes.at(&rage.weapons[0].values.silent)),"global and weapon replacement scopes stay distinct");
 check(!overlaps(scopes.at(&rage.global.silent),scopes.at(&legit.global.aimbot)),"mutually exclusive feature modes stay distinct");
 check(overlaps(scopes.at(&rage.global.silent),scopes.at(&legit.global.aimbot),true),"explicit exclusion relationships still warn");
}
