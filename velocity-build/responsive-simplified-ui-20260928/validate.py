from pathlib import Path
import hashlib
import json
import re
import sys

ROOT = Path(sys.argv[1]).resolve()
HERE = Path(__file__).resolve().parent

names = ["part1.txt","part2.txt","part3.txt","part4_1.txt","part4_2.txt","part4_3.txt","part4_4.txt"]
payload = "".join((HERE / n).read_text(encoding="ascii").strip() for n in names)
if len(payload) != 70736:
    raise RuntimeError(f"payload length mismatch: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest() != "9be01b18bc19c9942c9a5cc68c221bec2f06abd4301db2748c2ddf5761aeacc2":
    raise RuntimeError("payload hash mismatch")

patch_file = ROOT / "MCB_RESPONSIVE_SIMPLIFIED_UI.patch"
if len(patch_file.read_bytes()) != 247912:
    raise RuntimeError("patch length mismatch")
if hashlib.sha256(patch_file.read_bytes()).hexdigest() != "9abb485c7cb5de7446d0b7f00fda4636baf5d097fbdcf6cd8e72c2101a517b0e":
    raise RuntimeError("patch hash mismatch")

def read(rel):
    return (ROOT / rel).read_text(encoding="utf-8-sig")

core = read("project/core/rendering/impl/menu/menu.core.cpp")
exact = read("project/core/rendering/impl/menu/menu.exact.cpp")
rage = read("project/core/rendering/impl/menu/menu.ragebot.cpp")
player = read("project/core/rendering/impl/menu/menu.player.cpp")
misc = read("project/core/rendering/impl/menu/menu.misc.cpp")
skins = read("project/core/rendering/impl/menu/menu.skins.cpp")
settings = read("project/core/settings.hpp")
xui = read("project/external/xdraw/xui/xui.cpp")

checks = {
    "simplified_navigation": '"战斗", "玩家", "库存", "其他", "脚本", "配置"' in core,
    "global_search": "搜索功能…" in core,
    "hotkey_overview": "全局快捷键总览" in exact,
    "layout_editor": "界面布局编辑器" in exact,
    "inventory_collapsed": "显示选项  ▾##inventory_tools" in skins and "##inventory_tools_panel" in skins,
    "inventory_search": '"搜索…"' in skins,
    "misc_one_page": '"分类"' not in misc,
    "responsive_combat": "content_w < 760.0f" in rage,
    "responsive_player": "single_col = content_w < 760.0f" in player,
    "responsive_misc": "single_col = content_w < 760.0f" in misc,
    "medium_small_default": "preferred_w = this->m_sidebar_compact ? 840.0f : 980.0f" in core,
    "drag_visual_fix": "Keep the same blur while moving/resizing" in xui,
    "decorations": all(x in settings for x in ("decor_top_line","decor_dot_matrix","decor_vignette","decor_sidebar_glow","decor_section_lines")),
    "layout_offsets": all(x in settings for x in ("bomb_offset_x","spectator_offset_x","stats_offset_x")),
}
bad = [k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("postcondition failed: " + ", ".join(bad))

files = list((ROOT / "project/core/rendering/impl/menu").glob("*.cpp"))
files += [ROOT / "project/core/rendering/impl/widgets.cpp", ROOT / "project/core/localization/zh_tw.hpp"]
traditional = set("戰鬥庫腳設檔載儲刪顯開關選擇類數傷準鏡視覺環風濕潤圓邊動畫裝飾強稱當與擊後餘體隱尋預點內資訊彈藥槍敵隊讀寫會應項細網廣顏總覽調啟閉遠層塗標籤計時")
pat = re.compile(r'"([^"\\]*(?:\\.[^"\\]*)*)"')
hits = []
for path in files:
    for lineno,line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(),1):
        for value in pat.findall(line):
            chars = sorted(set(value) & traditional)
            if chars:
                hits.append({"file":str(path.relative_to(ROOT)),"line":lineno,"chars":"".join(chars),"text":value[:160]})
if hits:
    raise RuntimeError("Traditional Chinese visible strings remain: " + json.dumps(hits[:20], ensure_ascii=False))

report = {
    "name":"MCB responsive Simplified Chinese UI rework 2026-09-28",
    "checks":checks,
    "traditional_visible_hits":hits,
    "patch_sha256":"9abb485c7cb5de7446d0b7f00fda4636baf5d097fbdcf6cd8e72c2101a517b0e",
    "runtime":"NOT_TESTED",
}
(ROOT / "MCB_RESPONSIVE_SIMPLIFIED_UI_20260928.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
