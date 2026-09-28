from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
PREFIX = Path("cs2/MCB-CS2")

names = ["part1.txt","part2.txt","part3.txt","part4_1.txt","part4_2.txt","part4_3.txt","part4_4.txt"]
payload = "".join((HERE / name).read_text(encoding="ascii").strip() for name in names)

if len(payload) != 70736:
    raise RuntimeError(f"unexpected responsive payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest() != "9be01b18bc19c9942c9a5cc68c221bec2f06abd4301db2748c2ddf5761aeacc2":
    raise RuntimeError("responsive payload hash mismatch")

patch = zlib.decompress(base64.b64decode(payload))
if len(patch) != 247912:
    raise RuntimeError(f"unexpected responsive patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest() != "9abb485c7cb5de7446d0b7f00fda4636baf5d097fbdcf6cd8e72c2101a517b0e":
    raise RuntimeError("responsive patch hash mismatch")

patch_file = ROOT / "MCB_RESPONSIVE_SIMPLIFIED_UI.patch"
patch_file.write_bytes(patch)

args = [
    "git","-C",str(REPO),"apply",
    "--ignore-space-change","--ignore-whitespace",
    "--directory="+PREFIX.as_posix(),
    str(patch_file),
]
subprocess.run(args[:4] + ["--check"] + args[4:], check=True)
subprocess.run(args, check=True)

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")

core = read("project/core/rendering/impl/menu/menu.core.cpp")
exact = read("project/core/rendering/impl/menu/menu.exact.cpp")
misc = read("project/core/rendering/impl/menu/menu.misc.cpp")
skins = read("project/core/rendering/impl/menu/menu.skins.cpp")
widgets = read("project/core/rendering/impl/widgets.cpp")
rendering = read("project/core/rendering/rendering.hpp")
xui = read("project/external/xdraw/xui/xui.cpp")

checks = {
    "simplified_main_nav": all(x in core for x in ("战斗","玩家","库存","其他","脚本","配置")),
    "global_search": "搜索功能…" in core and "m_search_open" in core,
    "layout_editor": "draw_layout_editor" in exact and "界面布局编辑器" in exact,
    "hotkey_overview": "draw_hotkey_overview" in exact and "全局快捷键总览" in exact,
    "misc_single_page": "draw_misc_hub" in exact and all(x in exact for x in ("反馈、移动与系统","环境与天气","HUD 与屏幕组件","画面移除")),
    "inventory_disclosure": "m_inventory_categories_open" in rendering and "m_inventory_tools_open" in rendering,
    "responsive_geometry": "980.0f" in core and "620.0f" in core and "m_user_layout_initialized" in core,
    "drag_alpha_fix": "Keep the same blur while moving/resizing" in xui,
    "top_right_tools": all(x in core for x in ('"⌕"','"▦"','"⌨"','"⚙"')),
    "collapsed_controls": '"›"' in exact and '"▾"' in exact,
    "layout_free_position": all(x in exact for x in ("水平位置","垂直位置","界面大小")),
    "inventory_compact_toolbar": "m_inventory_categories_open" in skins and "m_inventory_tools_open" in skins,
}
bad = [k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("responsive simplified UI postcondition failed: " + ", ".join(bad))

report = {
    "name": "MCB responsive simplified UI rework 2026-09-28",
    "payload_sha256": hashlib.sha256(payload.encode("ascii")).hexdigest(),
    "patch_sha256": hashlib.sha256(patch).hexdigest(),
    "checks": checks,
    "runtime": "NOT_TESTED",
}
(ROOT / "MCB_RESPONSIVE_SIMPLIFIED_UI_20260928.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(report, ensure_ascii=False, indent=2))
