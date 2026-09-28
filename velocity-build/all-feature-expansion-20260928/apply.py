from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
PREFIX = Path("cs2/MCB-CS2")

parts = [(HERE / f"part{i}.txt").read_text(encoding="ascii").strip() for i in range(1, 5)]
payload = "".join(parts)
if len(payload) != 12710:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest() != "94a8bc26a8dec18e10611caae682ea89faac5ef6560c0a575cecbec94ac090aa":
    raise RuntimeError("feature expansion payload hash mismatch")

patch = zlib.decompress(base64.b85decode(payload))
if len(patch) != 41498:
    raise RuntimeError(f"unexpected patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest() != "d7cd7ea2d1dd2bbbe4fd9879269e0eb9af2c32b4ca397803293ae660e5e35e87":
    raise RuntimeError("feature expansion patch hash mismatch")

patch_file = ROOT / "MCB_ALL_FEATURE_EXPANSION.patch"
patch_file.write_bytes(patch)

apply_args = [
    "git", "-C", str(REPO), "apply",
    "--directory=" + PREFIX.as_posix(),
    str(patch_file),
]
check_args = apply_args[:4] + ["--check"] + apply_args[4:]
subprocess.run(check_args, check=True)
subprocess.run(apply_args, check=True)

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")

settings = read("project/core/settings.hpp")
rage = read("project/core/rendering/impl/menu/menu.ragebot.cpp")
legit = read("project/core/rendering/impl/menu/menu.legitbot.cpp")
misc = read("project/core/rendering/impl/menu/menu.misc.cpp")
skins = read("project/core/rendering/impl/menu/menu.skins.cpp")
player = read("project/core/rendering/impl/menu/menu.player.cpp")
world = read("project/core/rendering/impl/menu/menu.world.cpp")
exact = read("project/core/rendering/impl/menu/menu.exact.cpp")
config = read("project/core/rendering/impl/menu/menu.config.cpp")
hud = read("project/core/features/misc/impl/hud.cpp")
impacts = read("project/core/features/misc/impl/impacts.cpp")
combat_misc = read("project/core/features/combat/impl/misc.cpp")
widgets = read("project/core/rendering/impl/widgets.cpp")

checks = {
    "rage_expanded": "自動開鏡##rage_autoscope" in rage and "最大回溯##rage_bt" in rage and "穩健##rage_p1" in rage,
    "legit_presets": "自然##legit_p1" in legit and "快速##legit_p3" in legit,
    "aa_advanced": all(k in settings for k in ("direction_indicator_radius", "direction_indicator_sweep", "direction_indicator_thickness", "direction_indicator_height")) and "direction_indicator_radius" in combat_misc,
    "player_presets": "完整##esp_full" in player and "單層##cham_one" in player,
    "feedback_layout": "log_position" in settings and "資訊列位置##feedback_pos" in misc and "cfg.log_width" in impacts,
    "hit_marker_geometry": "hit_marker_size" in settings and "cfg.hit_marker_arm" in impacts,
    "environment_expanded": all(k in misc for k in ("環境補光", "方向 X##env_lrx", "色差後處理", "速度除錯")),
    "crosshair_expanded": "center_dot" in settings and "中心點##xhair" in misc and "cfg.gap.value" in hud,
    "inventory_expanded": "struct inventory_ui" in settings and "排序##inventory_sort" in skins and "compact_cards" in skins,
    "watermark_expanded": "show_logo" in settings and "水印位置##profile_wm_pos" in exact and "wm.placement.value" in widgets,
    "identity_expanded": "clantag_frame" in settings and "外框##identity_tag_frame" in misc,
    "script_filter": "##script_filter" in config,
    "config_utilities": "重設目前設定" in config and "備份選取設定" in config,
    "world_presets": "重點##item_key" in world,
}
bad = [k for k, v in checks.items() if not v]
if bad:
    raise RuntimeError("feature expansion postcondition failed: " + ", ".join(bad))

report = {
    "name": "MCB all-feature expansion 2026-09-28",
    "patch_sha256": hashlib.sha256(patch).hexdigest(),
    "checks": checks,
    "runtime": "NOT_TESTED",
}
(ROOT / "MCB_ALL_FEATURE_EXPANSION_20260928.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2),
    encoding="utf-8",
)

print(json.dumps(report, ensure_ascii=False, indent=2))
