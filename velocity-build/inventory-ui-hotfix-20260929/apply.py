from pathlib import Path
import hashlib
import json
import sys

ROOT = Path(sys.argv[1]).resolve()

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")

def write(rel, text):
    (ROOT / rel).write_text(text, encoding="utf-8", newline="\n")

def replace_once(text, old, new, label):
    if old not in text:
        raise RuntimeError("missing anchor: " + label)
    return text.replace(old, new, 1)

# 1) Remove all quick-config / preset buttons added by previous expansion layers.
preset_blocks = {
    "project/core/rendering/impl/menu/menu.ragebot.cpp": [
'''			const auto pw = std::max( 70.0f, ( xui::layout::avail( ).first - 12.0f ) / 3.0f );
			if ( xui::button( "稳健##rage_p1", pw, 24.0f ) ) { wg.hitchance = 88; wg.min_damage = 60; wg.pointscale = 72.0f; }
			xui::layout::same_line( ); if ( xui::button( "均衡##rage_p2", pw, 24.0f ) ) { wg.hitchance = 80; wg.min_damage = 45; wg.pointscale = 82.0f; }
			xui::layout::same_line( ); if ( xui::button( "激进##rage_p3", pw, 24.0f ) ) { wg.hitchance = 68; wg.min_damage = 25; wg.pointscale = 92.0f; }
'''],
    "project/core/rendering/impl/menu/menu.legitbot.cpp": [
'''			const auto preset_w = std::max( 70.0f, ( xui::layout::avail( ).first - 12.0f ) / 3.0f );
			if ( xui::button( "自然##legit_p1", preset_w, 24.0f ) ) { wg.fov = 2.2f; wg.smooth = 18; wg.trigger_delay = 85; wg.trigger_hitchance = 78; }
			xui::layout::same_line( ); if ( xui::button( "均衡##legit_p2", preset_w, 24.0f ) ) { wg.fov = 4.5f; wg.smooth = 10; wg.trigger_delay = 45; wg.trigger_hitchance = 72; }
			xui::layout::same_line( ); if ( xui::button( "快速##legit_p3", preset_w, 24.0f ) ) { wg.fov = 7.5f; wg.smooth = 5; wg.trigger_delay = 15; wg.trigger_hitchance = 65; }
'''],
    "project/core/rendering/impl/menu/menu.player.cpp": [
'''				const auto preset_w = std::max( 68.0f, ( xui::layout::avail( ).first - 8.0f ) / 3.0f );
				if ( xui::button( "完整##esp_full", preset_w, 23.0f ) ) { ov.enabled.value=true; ov.m_box.enabled.value=true; ov.m_skeleton.enabled.value=true; ov.m_health_bar.enabled.value=true; ov.m_ammo_bar.enabled.value=true; ov.m_name.enabled.value=true; ov.m_weapon.enabled.value=true; ov.m_info_flags.enabled.value=true; ov.m_oof_arrow.enabled.value=true; }
				xui::layout::same_line( ); if ( xui::button( "精简##esp_min", preset_w, 23.0f ) ) { ov.enabled.value=true; ov.m_box.enabled.value=true; ov.m_skeleton.enabled.value=false; ov.m_health_bar.enabled.value=true; ov.m_ammo_bar.enabled.value=false; ov.m_name.enabled.value=true; ov.m_weapon.enabled.value=true; ov.m_info_flags.enabled.value=false; ov.m_oof_arrow.enabled.value=false; }
				xui::layout::same_line( ); if ( xui::button( "关闭##esp_off", preset_w, 23.0f ) ) ov.enabled.value=false;
''',
'''				const auto mat_w = std::max( 68.0f, ( xui::layout::avail( ).first - 8.0f ) / 3.0f );
				if ( xui::button( "单层##cham_one", mat_w, 23.0f ) ) { chams.enabled.value=true; chams.primary.enabled.value=true; chams.secondary.enabled.value=false; chams.overlay.enabled.value=false; }
				xui::layout::same_line( ); if ( xui::button( "双层##cham_two", mat_w, 23.0f ) ) { chams.enabled.value=true; chams.primary.enabled.value=true; chams.secondary.enabled.value=true; }
				xui::layout::same_line( ); if ( xui::button( "关闭##cham_off", mat_w, 23.0f ) ) chams.enabled.value=false;
'''],
    "project/core/rendering/impl/menu/menu.world.cpp": [
'''				const auto pw = std::max( 64.0f, ( xui::layout::avail( ).first - 8.0f ) / 3.0f );
				if ( xui::button( "全部##item_all", pw, 23.0f ) ) { item.m_overlay.pistol.value=true; item.m_overlay.smg.value=true; item.m_overlay.rifle.value=true; item.m_overlay.shotgun.value=true; item.m_overlay.sniper.value=true; item.m_overlay.utility.value=true; }
				xui::layout::same_line( ); if ( xui::button( "重点##item_key", pw, 23.0f ) ) { item.m_overlay.pistol.value=false; item.m_overlay.smg.value=false; item.m_overlay.rifle.value=false; item.m_overlay.shotgun.value=false; item.m_overlay.sniper.value=true; item.m_overlay.utility.value=true; }
				xui::layout::same_line( ); if ( xui::button( "关闭##item_off", pw, 23.0f ) ) item.m_overlay.enabled.value=false;
'''],
}
for rel, blocks in preset_blocks.items():
    s = read(rel)
    for i, block in enumerate(blocks):
        s = replace_once(s, block, "", f"{rel}: preset {i}")
    write(rel, s)

# 2) Make LEGIT responsive like the other pages.
rel = "project/core/rendering/impl/menu/menu.legitbot.cpp"
s = read(rel)
s = replace_once(
    s,
'''		const auto content_w = xui::layout::current_window()->bounds.w - tokens::gap * 2.0f;
		const auto col_w = ( content_w - tokens::gap ) * 0.5f;
		const auto right_x = content_x + col_w + tokens::gap;
''',
'''		const auto content_w = std::max( 280.0f, xui::layout::current_window()->bounds.w - tokens::gap * 2.0f );
		const auto single_col = content_w < 760.0f;
		const auto col_w = single_col ? content_w : ( content_w - tokens::gap ) * 0.5f;
		const auto right_x = content_x + col_w + tokens::gap;
''',
    "legit responsive geometry",
)
s = replace_once(
    s,
'''		xui::layout::set_cursor( right_x - wx, body_y - wy );

		if ( xui::begin_child( "##legitbot_triggerbot", col_w ) )
''',
'''		if ( !single_col ) xui::layout::set_cursor( right_x - wx, body_y - wy );

		if ( xui::begin_child( "##legitbot_triggerbot", col_w ) )
''',
    "legit responsive second column",
)
write(rel, s)

# 3) Inventory UI/render crash hardening.
rel = "project/core/rendering/impl/menu/menu.skins.cpp"
s = read(rel)

s = replace_once(
    s,
'''		constexpr auto k_rarity_bar_h{ 2.0f };
''',
'''		constexpr auto k_rarity_bar_h{ 2.0f };
		constexpr auto k_min_card_w{ 104.0f };

		static inline bool valid_image( const features::changer::econ_item_system::skin_image* img )
		{
			return img && img->srv && img->width > 0 && img->height > 0;
		}

		static inline int fitted_columns( float inner_w, int requested )
		{
			if ( !std::isfinite( inner_w ) || inner_w <= 0.0f ) return 2;
			const auto fit = static_cast<int>( std::floor( ( inner_w + k_card_gap ) / ( k_min_card_w + k_card_gap ) ) );
			return std::clamp( std::min( requested, std::max( 2, fit ) ), 2, 7 );
		}
''',
    "inventory safety helpers",
)

s = replace_once(
    s,
'''		static inline void draw_weapon_card( const xui::rect& card, const features::changer::econ_item_system::item_def* def, float fade_alpha, bool show_images )
		{
			auto& econ = features::changer::g_econ_item_system;
''',
'''		static inline void draw_weapon_card( const xui::rect& card, const features::changer::econ_item_system::item_def* def, float fade_alpha, bool show_images )
		{
			if ( !def || !std::isfinite( card.w ) || !std::isfinite( card.h ) || card.w < 32.0f || card.h < 32.0f ) return;
			auto& econ = features::changer::g_econ_item_system;
''',
    "weapon card null/geometry guard",
)
s = replace_once(
    s,
'''		static inline void draw_agent_tile( const xui::rect& card, const features::changer::econ_item_system::item_def* def, bool is_equipped, float fade_alpha )
		{
			auto& econ = features::changer::g_econ_item_system;
''',
'''		static inline void draw_agent_tile( const xui::rect& card, const features::changer::econ_item_system::item_def* def, bool is_equipped, float fade_alpha )
		{
			if ( !def || !std::isfinite( card.w ) || !std::isfinite( card.h ) || card.w < 32.0f || card.h < 32.0f ) return;
			auto& econ = features::changer::g_econ_item_system;
''',
    "agent card null/geometry guard",
)
s = replace_once(
    s,
'''		static inline void draw_skin_tile( const xui::rect& card, const features::changer::econ_item_system::paint_kit* pk, const features::changer::econ_item_system::item_def* weapon, int current_kit_id, float fade_alpha )
		{
			auto& econ = features::changer::g_econ_item_system;
''',
'''		static inline void draw_skin_tile( const xui::rect& card, const features::changer::econ_item_system::paint_kit* pk, const features::changer::econ_item_system::item_def* weapon, int current_kit_id, float fade_alpha )
		{
			if ( !pk || !std::isfinite( card.w ) || !std::isfinite( card.h ) || card.w < 32.0f || card.h < 32.0f ) return;
			auto& econ = features::changer::g_econ_item_system;
''',
    "skin card null/geometry guard",
)

# Every texture draw must have a valid SRV and dimensions.
s = s.replace("if ( img )\n\t\t\t{", "if ( valid_image( img ) )\n\t\t\t{")
s = s.replace("if ( fallback_img )\n\t\t\t\t\t{", "if ( valid_image( fallback_img ) )\n\t\t\t\t\t{")

s = replace_once(
    s,
'if ( xui::begin_child( "##inventory_tools_panel", inner_w, 84.0f, false ) )',
'if ( xui::begin_child( "##inventory_tools_panel", inner_w, 112.0f, false ) )',
    "inventory tools panel height",
)
s = replace_once(
    s,
'''					xui::checkbox( "显示图片##inventory_images", inventory_ui.show_images );
					xui::layout::same_line( ); xui::checkbox( "紧凑卡片##inventory_compact", inventory_ui.compact_cards );
''',
'''					xui::checkbox( "显示图片##inventory_images", inventory_ui.show_images );
					xui::checkbox( "紧凑卡片##inventory_compact", inventory_ui.compact_cards );
''',
    "inventory checkbox overlap",
)

s = s.replace(
    "const auto columns = std::clamp( inventory_ui.columns.value, 3, 7 );",
    "const auto columns = detail::fitted_columns( inner_w, std::clamp( inventory_ui.columns.value, 3, 7 ) );",
)

s = replace_once(
    s,
'''			for ( const auto* w : weapons )
			{
''',
'''			for ( const auto* w : weapons )
			{
				if ( !w ) continue;
''',
    "inventory weapon pointer guard",
)

s = s.replace(
'''				for ( const auto* a : econ.agents( ) )
				{
					const auto agent_team = a->team( );
''',
'''				for ( const auto* a : econ.agents( ) )
				{
					if ( !a ) continue;
					const auto agent_team = a->team( );
''',
)

s = s.replace(
'''							for ( const auto* k : econ.knives( ) )
							{
								skin_map( ).erase( k->def_index );
''',
'''							for ( const auto* k : econ.knives( ) )
							{
								if ( k ) skin_map( ).erase( k->def_index );
''',
)
s = s.replace(
'''							for ( const auto* g : econ.gloves( ) )
							{
								skin_map( ).erase( g->def_index );
''',
'''							for ( const auto* g : econ.gloves( ) )
							{
								if ( g ) skin_map( ).erase( g->def_index );
''',
)

s = replace_once(
    s,
'''			const auto weapon = econ.find_def( detail::skins_ui.browsing_def );

			std::unordered_set<int> valid_kits;
''',
'''			const auto weapon = econ.find_def( detail::skins_ui.browsing_def );
			if ( !weapon )
			{
				detail::request_page( detail::skins_page::grid );
				detail::skins_ui.search_buf.clear( );
				xui::end_child( );
				return;
			}

			std::unordered_set<int> valid_kits;
''',
    "inventory browser stale def guard",
)

write(rel, s)

# Fresh installs no longer launch image decoding immediately on opening inventory.
rel = "project/core/settings.hpp"
s = read(rel)
s = replace_once(
    s,
'xui::setting show_images{ true, {}, "show images", "changer ui" };',
'xui::setting show_images{ false, {}, "show images", "changer ui" };',
    "inventory images safe default",
)
write(rel, s)

# 4) Guard all game-schema strings instead of dereferencing raw char pointers.
rel = "project/utilities/memory/memory.cpp"
s = read(rel)
s = replace_once(
    s,
'''	std::string read_string (std::uintptr_t address, std::size_t max_length) {
		if (!address) {
			return {};
		}

		auto str_ptr = reinterpret_cast<const char*>(address);

		auto len {0ull};
		for (; len < max_length && str_ptr [len] != '\\0'; ++len);

		if (len == 0) {
			return {};
		}

		return std::string (str_ptr, len);
	}
''',
'''	std::string read_string (std::uintptr_t address, std::size_t max_length) {
		if (!address || max_length == 0) {
			return {};
		}

		std::string result;
		result.reserve(std::min<std::size_t>(max_length, 256));
		for (std::size_t i = 0; i < max_length; ++i) {
			const auto ch = safe_read<char>(address + i);
			if (!ch) {
				return {};
			}
			if (*ch == '\\0') {
				break;
			}
			result.push_back(*ch);
		}
		return result;
	}
''',
    "safe read_string",
)
write(rel, s)

# 5) Guard the economy-schema reads that are triggered when inventory is first opened.
rel = "project/core/features/changer/impl/econ_item_system.cpp"
s = read(rel)
pairs = [
("const auto count = memory::read<int>( schema + 0x128 );", "const auto count = memory::safe_read<int>( schema + 0x128 ).value_or( 0 );"),
("const auto array = memory::read<std::uintptr_t>( schema + 0x130 );", "const auto array = memory::safe_read<std::uintptr_t>( schema + 0x130 ).value_or( 0 );"),
("const auto def_index = memory::read<int>( entry_base + 16 );", "const auto def_index = memory::safe_read<int>( entry_base + 16 ).value_or( -2 );"),
("const auto def_ptr = memory::read<std::uintptr_t>( entry_base + 24 );", "const auto def_ptr = memory::safe_read<std::uintptr_t>( entry_base + 24 ).value_or( 0 );"),
("item.def_index = memory::read<std::int16_t>( def_ptr + 0x10 );", "item.def_index = memory::safe_read<std::int16_t>( def_ptr + 0x10 ).value_or( 0 );"),
("item.loadout_slot = memory::read<int>( def_ptr + 0x338 );", "item.loadout_slot = memory::safe_read<int>( def_ptr + 0x338 ).value_or( -1 );"),
("item.used_by_classes = memory::read<std::uint32_t>( def_ptr + 0x368 );", "item.used_by_classes = memory::safe_read<std::uint32_t>( def_ptr + 0x368 ).value_or( 0 );"),
("item.rarity = memory::read<std::uint8_t>( def_ptr + 0x42 );", "item.rarity = memory::safe_read<std::uint8_t>( def_ptr + 0x42 ).value_or( 0 );"),
("if ( const auto name_ptr = memory::read<std::uintptr_t>( def_ptr + 0x260 ); name_ptr )", "if ( const auto name_ptr = memory::safe_read<std::uintptr_t>( def_ptr + 0x260 ).value_or( 0 ); name_ptr )"),
("if ( const auto model_ptr = memory::read<std::uintptr_t>( def_ptr + 0x148 ); model_ptr )", "if ( const auto model_ptr = memory::safe_read<std::uintptr_t>( def_ptr + 0x148 ).value_or( 0 ); model_ptr )"),
("if ( const auto img_ptr = memory::read<std::uintptr_t>( def_ptr + 0xA8 ); img_ptr )", "if ( const auto img_ptr = memory::safe_read<std::uintptr_t>( def_ptr + 0xA8 ).value_or( 0 ); img_ptr )"),
("if ( const auto token_ptr = memory::read<std::uintptr_t>( def_ptr + 0x70 ); token_ptr )", "if ( const auto token_ptr = memory::safe_read<std::uintptr_t>( def_ptr + 0x70 ).value_or( 0 ); token_ptr )"),
("const auto count = memory::read<int>( tree_base + 0x00 );", "const auto count = memory::safe_read<int>( tree_base + 0x00 ).value_or( 0 );"),
("const auto nodes = memory::read<std::uintptr_t>( tree_base + 0x08 );", "const auto nodes = memory::safe_read<std::uintptr_t>( tree_base + 0x08 ).value_or( 0 );"),
("const auto pk_ptr = memory::read<std::uintptr_t>( node_base + 24 );", "const auto pk_ptr = memory::safe_read<std::uintptr_t>( node_base + 24 ).value_or( 0 );"),
("pk.id = memory::read<int>( pk_ptr + 0x00 );", "pk.id = memory::safe_read<int>( pk_ptr + 0x00 ).value_or( 0 );"),
("pk.wear_min = memory::read<float>( pk_ptr + 0x6C );", "pk.wear_min = memory::safe_read<float>( pk_ptr + 0x6C ).value_or( 0.0f );"),
("pk.wear_max = memory::read<float>( pk_ptr + 0x70 );", "pk.wear_max = memory::safe_read<float>( pk_ptr + 0x70 ).value_or( 1.0f );"),
("pk.legacy_model = memory::read<bool>( pk_ptr + 0xAE );", "pk.legacy_model = memory::safe_read<bool>( pk_ptr + 0xAE ).value_or( false );"),
("pk.rarity = static_cast< std::uint8_t >( memory::read<int>( pk_ptr + 0x44 ) & 0xFF );", "pk.rarity = static_cast< std::uint8_t >( memory::safe_read<int>( pk_ptr + 0x44 ).value_or( 0 ) & 0xFF );"),
("if ( const auto name_ptr = memory::read<std::uintptr_t>( pk_ptr + 0x08 ); name_ptr )", "if ( const auto name_ptr = memory::safe_read<std::uintptr_t>( pk_ptr + 0x08 ).value_or( 0 ); name_ptr )"),
("if ( const auto desc_ptr = memory::read<std::uintptr_t>( pk_ptr + 0x10 ); desc_ptr )", "if ( const auto desc_ptr = memory::safe_read<std::uintptr_t>( pk_ptr + 0x10 ).value_or( 0 ); desc_ptr )"),
("if ( const auto name_token_ptr = memory::read<std::uintptr_t>( pk_ptr + 0x18 ); name_token_ptr )", "if ( const auto name_token_ptr = memory::safe_read<std::uintptr_t>( pk_ptr + 0x18 ).value_or( 0 ); name_token_ptr )"),
]
for old, new in pairs:
    s = replace_once(s, old, new, "econ safe read: " + old)

# Throttle texture decoding: do not enqueue every visible card in one frame.
s = replace_once(
    s,
'''		auto it = this->m_image_cache.find( image_inventory );
		if ( it == this->m_image_cache.end( ) )
		{
			auto entry = std::make_unique<image_entry>( );
			it = this->m_image_cache.emplace( image_inventory, std::move( entry ) ).first;
			this->request_decode( image_inventory );
			return nullptr;
		}

		const auto state = it->second->state.load( std::memory_order_acquire );
''',
'''		auto it = this->m_image_cache.find( image_inventory );
		if ( it == this->m_image_cache.end( ) )
		{
			auto entry = std::make_unique<image_entry>( );
			it = this->m_image_cache.emplace( image_inventory, std::move( entry ) ).first;
		}

		const auto state = it->second->state.load( std::memory_order_acquire );
		if ( state == image_state::idle )
		{
			static thread_local ULONGLONG last_decode_request{};
			const auto now = GetTickCount64( );
			if ( now - last_decode_request >= 8 )
			{
				last_decode_request = now;
				this->request_decode( image_inventory );
			}
			return nullptr;
		}
''',
    "inventory image decode throttle",
)

write(rel, s)

checks = {
    "quick_configs_removed": True,
    "legit_responsive": "const auto single_col = content_w < 760.0f;" in read("project/core/rendering/impl/menu/menu.legitbot.cpp"),
    "inventory_dynamic_columns": "fitted_columns" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_image_guards": "valid_image" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_stale_def_guard": "if ( !weapon )" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_safe_default": 'show_images{ false' in read("project/core/settings.hpp"),
    "safe_schema_reads": "memory::safe_read<int>( schema + 0x128 )" in read("project/core/features/changer/impl/econ_item_system.cpp"),
    "safe_string_reads": "const auto ch = safe_read<char>(address + i);" in read("project/utilities/memory/memory.cpp"),
    "image_decode_throttle": "last_decode_request" in read("project/core/features/changer/impl/econ_item_system.cpp"),
}
combined = "\n".join(read(p) for p in preset_blocks)
for token in ("##rage_p1","##rage_p2","##rage_p3","##legit_p1","##legit_p2","##legit_p3","##esp_full","##esp_min","##esp_off","##cham_one","##cham_two","##cham_off","##item_all","##item_key","##item_off"):
    if token in combined:
        checks["quick_configs_removed"] = False

bad = [k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("inventory UI hotfix postcondition failed: " + ", ".join(bad))

report = {
    "name": "MCB inventory crash + quick config removal hotfix 2026-09-29",
    "checks": checks,
    "runtime": "NOT_TESTED",
}
(ROOT / "MCB_INVENTORY_UI_HOTFIX_20260929.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(report, ensure_ascii=False, indent=2))
