"""Integrate verified native CFG presets and shot-evidence logging.

Runs after v6.2 UI, Nixware LuaJIT and responsive input mapping patches.
This patch does not change rage targeting, hitchance, resolver or firing logic.
"""
from pathlib import Path
import shutil
import sys

root=Path(sys.argv[1]).resolve()
candidates=[
    root/"cs2"/"MCB-CS2",
    root/"cs2"/"velocity-cs2",
]
v=next((p for p in candidates if p.exists()),None)
if v is None:
    raise SystemExit("[cfg-shot] target project not found")

overlay=Path(__file__).resolve().parent/"overlay"
dst=v/"project"/"core"/"mcb"
dst.mkdir(parents=True,exist_ok=True)

for name in (
    "mcb_presets.hpp","mcb_presets.cpp",
    "mcb_shot_evidence.hpp","mcb_shot_evidence.cpp",
):
    src=overlay/name
    if not src.exists():
        raise SystemExit(f"[cfg-shot] missing overlay/{name}")
    shutil.copy2(src,dst/name)

proj_candidates=[v/"MCB-CS2.vcxproj",v/"velocity-cs2.vcxproj"]
proj=next((p for p in proj_candidates if p.exists()),None)
if proj is None:
    raise SystemExit("[cfg-shot] vcxproj missing")

t=proj.read_text(encoding="utf-8")
compile_anchor='    <ClCompile Include="project\\core\\scripting\\lua_manager.cpp" />'
header_anchor='    <ClInclude Include="project\\core\\scripting\\scripting.hpp" />'
if compile_anchor not in t or header_anchor not in t:
    raise SystemExit("[cfg-shot] vcxproj anchors missing")
for entry in (
    '    <ClCompile Include="project\\core\\mcb\\mcb_presets.cpp" />',
    '    <ClCompile Include="project\\core\\mcb\\mcb_shot_evidence.cpp" />',
):
    if entry not in t:
        t=t.replace(compile_anchor,compile_anchor+"\n"+entry,1)
for entry in (
    '    <ClInclude Include="project\\core\\mcb\\mcb_presets.hpp" />',
    '    <ClInclude Include="project\\core\\mcb\\mcb_shot_evidence.hpp" />',
):
    if entry not in t:
        t=t.replace(header_anchor,header_anchor+"\n"+entry,1)
proj.write_text(t,encoding="utf-8")

# Native gameplay preset buttons live inside the existing Configs page but are
# clearly separated from the v6.2 local-UI JSON snapshot system.
menu=v/"project"/"core"/"rendering"/"impl"/"menu"/"menu.exact.cpp"
mt=menu.read_text(encoding="utf-8")
inc='#include <core/mcb/mcb_presets.hpp>'
if inc not in mt:
    anchor='#include <pch/pch.hpp>'
    if anchor not in mt:
        raise SystemExit("[cfg-shot] menu include anchor missing")
    mt=mt.replace(anchor,anchor+"\n"+inc,1)
menu.write_text(mt,encoding="utf-8")

views=v/"project"/"core"/"rendering"/"ui_views_v62.inl"
vt=views.read_text(encoding="utf-8")
preset_marker='''    xui::text("Configs",tokens::col_text); xui::text(dirty()?"Unsaved changes":"All changes saved",dirty()?tokens::col_accent:tokens::col_text_dim);
'''
preset_block=preset_marker+'''    xui::layout::separator();
    xui::text("Native gameplay presets",tokens::col_text);
    xui::text("PARTIAL preset -> FULL native registry CFG; UI snapshots below are separate.",tokens::col_text_dim);
    if(xui::button("Legit CFG",112,30)){
        const auto r=::mcb::presets::apply_and_save(::mcb::presets::kind::legit);
        notify(r.message,!r.success);
    }
    xui::layout::same_line();
    if(xui::button("Rage CFG",112,30)){
        const auto r=::mcb::presets::apply_and_save(::mcb::presets::kind::rage);
        notify(r.message,!r.success);
    }
    xui::layout::same_line();
    if(xui::button("HVH CFG",112,30)){
        const auto r=::mcb::presets::apply_and_save(::mcb::presets::kind::hvh);
        notify(r.message,!r.success);
    }
    xui::layout::new_line();
    xui::layout::separator();
'''
if "Native gameplay presets" not in vt:
    if preset_marker not in vt:
        raise SystemExit("[cfg-shot] config view marker missing")
    vt=vt.replace(preset_marker,preset_block,1)
views.write_text(vt,encoding="utf-8")

misc_hpp=v/"project"/"core"/"features"/"misc"/"misc.hpp"
ht=misc_hpp.read_text(encoding="utf-8")
if "void on_weapon_fire( std::uintptr_t event );" not in ht:
    anchor="\t\tvoid on_bullet_impact( std::uintptr_t event );\n"
    if anchor not in ht:
        raise SystemExit("[cfg-shot] impacts public anchor missing")
    ht=ht.replace(anchor,anchor+"\t\tvoid on_weapon_fire( std::uintptr_t event );\n",1)

if "std::uint64_t evidence_id{};" not in ht:
    anchor="\t\tstruct shot_record\n\t\t{\n"
    if anchor not in ht:
        raise SystemExit("[cfg-shot] shot_record anchor missing")
    ht=ht.replace(anchor,anchor+"\t\t\tstd::uint64_t evidence_id{};\n",1)

if "bool weapon_fire_confirmed{};" not in ht:
    anchor="\t\t\tbool server_confirmed{};\n"
    if anchor not in ht:
        raise SystemExit("[cfg-shot] server_confirmed anchor missing")
    ht=ht.replace(
        anchor,
        anchor+
        "\t\t\tbool weapon_fire_confirmed{};\n"
        "\t\t\tfloat weapon_fire_time{};\n",
        1)

if "m_next_evidence_id" not in ht:
    anchor="\t\tstd::vector<shot_record> m_pending_shots{};\n"
    if anchor not in ht:
        raise SystemExit("[cfg-shot] pending shots member anchor missing")
    ht=ht.replace(anchor,anchor+"\t\tstd::uint64_t m_next_evidence_id{ 1 };\n",1)
misc_hpp.write_text(ht,encoding="utf-8")

imp=v/"project"/"core"/"features"/"misc"/"impl"/"impacts.cpp"
it=imp.read_text(encoding="utf-8")
evidence_inc="#include <core/mcb/mcb_shot_evidence.hpp>"
if evidence_inc not in it:
    anchor="#include <pch/pch.hpp>"
    if anchor not in it:
        raise SystemExit("[cfg-shot] impacts include anchor missing")
    it=it.replace(anchor,anchor+"\n"+evidence_inc,1)

# Add local weapon_fire evidence callback before bullet_impact.
if "void impacts::on_weapon_fire" not in it:
    anchor="\tvoid impacts::on_bullet_impact( std::uintptr_t event )\n"
    if anchor not in it:
        raise SystemExit("[cfg-shot] bullet impact function anchor missing")
    fn=r'''	void impacts::on_weapon_fire( std::uintptr_t event )
	{
		if ( !event )
			return;

		const auto userid_key = cstypes::event_hash{ 0, "userid" };
		const auto controller = memory::call<std::uintptr_t>(
			PATTERN( patterns::game_event_get_controller ),
			event,
			&userid_key );

		if ( controller != systems::g_local.get( ).controller )
			return;

		const auto global_vars =
			memory::read<std::uintptr_t>( addresses::globals::global_vars );
		const auto current_time =
			memory::read<float>( global_vars + 0x30 );

		std::unique_lock lock( this->m_mtx );
		shot_record* matched{};

		for ( auto& shot : this->m_pending_shots )
		{
			if ( shot.resolved || shot.weapon_fire_confirmed )
				continue;

			const auto age = current_time - shot.time;
			if ( age < -0.10f || age > 1.0f )
				continue;

			matched = &shot;
			break;
		}

		if ( matched )
		{
			matched->weapon_fire_confirmed = true;
			matched->weapon_fire_time = current_time;
			::mcb::shot_evidence::weapon_fire(
				matched->evidence_id, current_time, true );
		}
		else
		{
			// Local manual shots can legitimately have no rage request.
			::mcb::shot_evidence::weapon_fire(
				0, current_time, false );
		}
	}

'''
    it=it.replace(anchor,fn+anchor,1)

# Give each predicted rage shot a stable evidence id and log the request.
boom_anchor='''		std::unique_lock lock( this->m_mtx );

		const auto target_velocity = memory::read<math::vector3>( victim_pawn + SCHEMA( "C_BaseEntity", "m_vecVelocity"_hash ) );

		this->m_pending_shots.push_back(
			{
				.victim_pawn = victim_pawn,
'''
if ".evidence_id = evidence_id," not in it:
    replacement='''		std::unique_lock lock( this->m_mtx );

		const auto evidence_id = this->m_next_evidence_id++;
		const auto target_velocity = memory::read<math::vector3>( victim_pawn + SCHEMA( "C_BaseEntity", "m_vecVelocity"_hash ) );

		this->m_pending_shots.push_back(
			{
				.evidence_id = evidence_id,
				.victim_pawn = victim_pawn,
'''
    if boom_anchor not in it:
        raise SystemExit("[cfg-shot] on_boom initializer anchor missing")
    it=it.replace(boom_anchor,replacement,1)

if ".weapon_fire_confirmed = false," not in it:
    anchor='''				.server_confirmed = false,
				.impact_confirmed = false,
'''
    repl='''				.server_confirmed = false,
				.weapon_fire_confirmed = false,
				.weapon_fire_time = 0.0f,
				.impact_confirmed = false,
'''
    if anchor not in it:
        raise SystemExit("[cfg-shot] on_boom confirmation anchor missing")
    it=it.replace(anchor,repl,1)

request_anchor='''		if ( this->m_pending_shots.size( ) > 10 )
		{
			this->m_pending_shots.erase( this->m_pending_shots.begin( ) );
		}
	}

	const char* impacts::classify_shot_deviation'''
if "::mcb::shot_evidence::request" not in it:
    request_block='''		if ( this->m_pending_shots.size( ) > 10 )
		{
			this->m_pending_shots.erase( this->m_pending_shots.begin( ) );
		}

		::mcb::shot_evidence::request(
			evidence_id,
			victim_pawn,
			hitgroup,
			damage,
			hitchance,
			inaccuracy,
			spread,
			aim_angle.x, aim_angle.y, aim_angle.z,
			shoot_position.x, shoot_position.y, shoot_position.z,
			tick,
			features::combat::g_shared.ctx( ).weapon_type,
			forced,
			current_time );
	}

	const char* impacts::classify_shot_deviation'''
    if request_anchor not in it:
        raise SystemExit("[cfg-shot] on_boom tail anchor missing")
    it=it.replace(request_anchor,request_block,1)

# Server inaccuracy evidence.
inacc_anchor='''				it->server_inaccuracy = inaccuracy;
				it->server_confirmed = true;
				break;
'''
if "::mcb::shot_evidence::server_inaccuracy" not in it:
    inacc_repl='''				it->server_inaccuracy = inaccuracy;
				it->server_confirmed = true;
				const auto global_vars =
					memory::read<std::uintptr_t>( addresses::globals::global_vars );
				const auto current_time =
					memory::read<float>( global_vars + 0x30 );
				::mcb::shot_evidence::server_inaccuracy(
					it->evidence_id, inaccuracy, current_time );
				break;
'''
    if inacc_anchor not in it:
        raise SystemExit("[cfg-shot] inaccuracy anchor missing")
    it=it.replace(inacc_anchor,inacc_repl,1)

# Server shoot position evidence.
shoot_anchor='''				shot.server_shoot_position = shoot_position;
				shot.server_shoot_position_confirmed = true;
				break;
'''
if "::mcb::shot_evidence::server_shoot_position" not in it:
    shoot_repl='''				shot.server_shoot_position = shoot_position;
				shot.server_shoot_position_confirmed = true;
				const auto global_vars =
					memory::read<std::uintptr_t>( addresses::globals::global_vars );
				const auto current_time =
					memory::read<float>( global_vars + 0x30 );
				::mcb::shot_evidence::server_shoot_position(
					shot.evidence_id,
					shoot_position.x,
					shoot_position.y,
					shoot_position.z,
					current_time );
				break;
'''
    if shoot_anchor not in it:
        raise SystemExit("[cfg-shot] shoot position anchor missing")
    it=it.replace(shoot_anchor,shoot_repl,1)

# Bullet impact evidence only when a pending shot was matched.
impact_anchor='''					matched_shot->impact_time = current_time;
					matched_shot->impact_confirmed = true;
				}
			}

			this->check_misses( );
'''
if "::mcb::shot_evidence::impact" not in it:
    impact_repl='''					matched_shot->impact_time = current_time;
					matched_shot->impact_confirmed = true;
				}

				::mcb::shot_evidence::impact(
					matched_shot->evidence_id,
					pos.x, pos.y, pos.z,
					current_time );
			}

			this->check_misses( );
'''
    if impact_anchor not in it:
        raise SystemExit("[cfg-shot] impact anchor missing")
    it=it.replace(impact_anchor,impact_repl,1)

# Preserve evidence id from the exact shot selected by parse_event.
if "auto evidence_id{ 0ull };" not in it:
    anchor='''		auto weapon_type{ 0u };
		std::string mismatch_reason{};
'''
    repl='''		auto weapon_type{ 0u };
		auto evidence_id{ 0ull };
		std::string mismatch_reason{};
'''
    if anchor not in it:
        raise SystemExit("[cfg-shot] parse event locals anchor missing")
    it=it.replace(anchor,repl,1)

if "evidence_id = matched_shot->evidence_id;" not in it:
    anchor='''				expected_damage = matched_shot->damage;
				matched_shot->resolved = true;
'''
    repl='''				expected_damage = matched_shot->damage;
				evidence_id = matched_shot->evidence_id;
				matched_shot->resolved = true;
'''
    if anchor not in it:
        raise SystemExit("[cfg-shot] parse event matched shot anchor missing")
    it=it.replace(anchor,repl,1)

health_old='''.health = memory::call<int>( PATTERN (patterns::game_event_get_int), event, "health", false ),'''
if health_old in it and "const auto health = memory::call<int>" not in it:
    insert_anchor='''		if ( !was_aimbot )
		{
'''
    health_block='''		const auto health = memory::call<int>(
			PATTERN( patterns::game_event_get_int ),
			event,
			"health",
			false );

		if ( evidence_id != 0 )
		{
			const auto global_vars =
				memory::read<std::uintptr_t>( addresses::globals::global_vars );
			const auto current_time =
				memory::read<float>( global_vars + 0x30 );
			::mcb::shot_evidence::hurt(
				evidence_id,
				damage,
				health,
				hitgroup,
				expected_hitgroup,
				expected_damage,
				mismatch_reason,
				current_time );
		}

'''
    if insert_anchor not in it:
        raise SystemExit("[cfg-shot] parse event health insertion anchor missing")
    it=it.replace(insert_anchor,health_block+insert_anchor,1)
    it=it.replace(health_old,'.health = health,',1)

# Classify and record every expired confirmed shot regardless of HUD miss-log
# visibility. Also explicitly record predicted commands that never became a shot.
old='''				if ( !it->server_confirmed && !it->impact_confirmed )
				{
					it = this->m_pending_shots.erase( it );
					continue;
				}

				it->resolved = true;

				if ( cfg.miss_log.value )
				{
					const char* reason;

					if ( it->forced )
					{
						reason = "forced";
					}
					else if ( !it->impact_confirmed )
					{
						reason = "death";
					}
					else
					{
						reason = this->classify_shot_deviation( *it );
					}

					this->add_miss_log( *it, reason );
				}
'''
new='''				if ( !it->server_confirmed && !it->impact_confirmed )
				{
					::mcb::shot_evidence::discarded(
						it->evidence_id,
						"no server inaccuracy callback or bullet impact",
						current_time );
					it = this->m_pending_shots.erase( it );
					continue;
				}

				it->resolved = true;

				const char* reason;
				if ( it->forced )
					reason = "forced";
				else if ( !it->impact_confirmed )
					reason = "no impact";
				else
					reason = this->classify_shot_deviation( *it );

				::mcb::shot_evidence::miss(
					it->evidence_id,
					reason,
					it->weapon_fire_confirmed,
					it->server_confirmed,
					it->impact_confirmed,
					current_time );

				if ( cfg.miss_log.value )
					this->add_miss_log( *it, reason );
'''
if "::mcb::shot_evidence::discarded" not in it:
    if old not in it:
        raise SystemExit("[cfg-shot] miss classifier anchor missing")
    it=it.replace(old,new,1)

imp.write_text(it,encoding="utf-8")

# Register local weapon_fire in the existing event map after LuaJIT has been
# integrated. This is a normal game-event listener, not a new gameplay hook.
events=v/"project"/"core"/"systems"/"impl"/"events.cpp"
et=events.read_text(encoding="utf-8")
import re
if not re.search(r'm_events\s*\[\s*"weapon_fire"\s*\]', et):
    match=re.search(r'm_events\s*\[\s*"bullet_impact"\s*\]', et)
    if not match:
        raise SystemExit("[cfg-shot] bullet_impact event map entry missing")
    line_start=et.rfind("\\n",0,match.start())+1
    indent=et[line_start:match.start()]
    weapon=indent + 'm_events[ "weapon_fire" ] = [ ]( void* event ) { scripting::g_nix.on_game_event( "weapon_fire", reinterpret_cast< std::uintptr_t >( event ) ); features::misc::g_impacts.on_weapon_fire( reinterpret_cast< std::uintptr_t >( event ) ); };\\n'
    et=et[:line_start]+weapon+et[line_start:]
events.write_text(et,encoding="utf-8")

report=root/"MCB_CFG_SHOT_APPLIED.txt"
report.write_text(
    "native_presets=Legit,Rage,HVH\\n"
    "preset_semantics=PARTIAL\\n"
    "saved_cfg_semantics=FULL_NATIVE_REGISTRY\\n"
    "cfg_full_snapshot=ENABLED\\n"
    "cfg_non_target_field_verification=ENABLED\\n"
    "cfg_save_load_readback=ENABLED\\n"
    "cfg_failure_rollback=ENABLED\\n"
    "shot_evidence=%LOCALAPPDATA%\\\\MCB\\\\shot_evidence.jsonl\\n"
    "shot_stages=REQUEST,WEAPON_FIRE,SERVER_INACCURACY,SERVER_SHOOT_POS,IMPACT,HURT,MISS,DISCARDED\\n"
    "rage_algorithm_changed=NO\\n"
    "hitchance_changed_by_diagnostics=NO\\n"
    "in_game_verification=NOT_TESTED\\n",
    encoding="utf-8"
)

print("[cfg-shot] native CFG presets and evidence logging integrated")
