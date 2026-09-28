from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
PREFIX = Path("cs2/MCB-CS2")

parts=[(HERE/f"part{i}.txt").read_text(encoding="ascii").strip() for i in (1,2,3)]
payload="".join(parts)
if len(payload)!=8275:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="4a8157311130300b1686392f9ff96007547eed67c900db62958ff2a29b09b021":
    raise RuntimeError("advanced payload hash mismatch")

patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=29827:
    raise RuntimeError(f"unexpected patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="159b697a5f78c5fca0931bb51e1fcc9932e19c474dc15a0e281424d77366a93d":
    raise RuntimeError("advanced patch hash mismatch")

patch_file=ROOT/"MCB_ADVANCED_EXPANSION.patch"
patch_file.write_bytes(patch)

args=["git","-C",str(REPO),"apply","--ignore-space-change","--ignore-whitespace","--directory="+PREFIX.as_posix(),str(patch_file)]
check=args[:4]+["--check"]+args[4:]
subprocess.run(check,check=True)
subprocess.run(args,check=True)

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")

checks={
    "esp_filters": all(x in read("project/core/settings.hpp") for x in ("visible_only","max_distance")) and "cfg.visible_only.value" in read("project/core/features/esp/player/player.overlay.cpp"),
    "crosshair_advanced": all(x in read("project/core/settings.hpp") for x in ("dynamic_gap","dynamic_gap_scale","t_style")) and "cfg.dynamic_gap.value" in read("project/core/features/misc/impl/hud.cpp"),
    "velocity_stats": all(x in read("project/core/settings.hpp") for x in ("show_peak","show_delta","reset_peak_on_land")) and "session_peak" in read("project/core/features/misc/impl/hud.cpp"),
    "watermark_positioning": all(x in read("project/core/settings.hpp") for x in ("offset_x","offset_y")) and "wm.offset_x.value" in read("project/core/rendering/impl/widgets.cpp"),
    "keybind_widget_controls": "m_keybinds" in read("project/core/settings.hpp") and "kb_cfg.placement.value" in read("project/core/rendering/impl/widgets.cpp"),
    "menu_controls": all(x in read("project/core/rendering/impl/menu/menu.exact.cpp") for x in ("快捷鍵位置","水印透明度","水印水平偏移")),
    "player_filter_ui": "只顯示可見目標" in read("project/core/rendering/impl/menu/menu.player.cpp"),
    "hud_advanced_ui": all(x in read("project/core/rendering/impl/menu/menu.misc.cpp") for x in ("動態間距","丁字準星","顯示峰值","顯示加速度")),
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("advanced expansion postcondition failed: "+", ".join(bad))

report={"name":"MCB advanced expansion 2026-09-28","patch_sha256":hashlib.sha256(patch).hexdigest(),"checks":checks,"runtime":"NOT_TESTED"}
(ROOT/"MCB_ADVANCED_EXPANSION_20260928.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
