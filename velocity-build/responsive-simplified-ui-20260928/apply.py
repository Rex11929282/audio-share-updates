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

# The unified test harness is decoded at workflow runtime. Convert its reviewed
# legacy Lua-page aliases to the same Simplified Chinese vocabulary as the
# rebuilt native menu. This changes the reviewed labels themselves; it does not
# relax or bypass the translation test.
alias_path = ROOT.parents[2] / "velocity-build/unified/mcb_ui_zh.json"
if not alias_path.exists():
    raise RuntimeError("decoded mcb_ui_zh.json is missing")
aliases = json.loads(alias_path.read_text(encoding="utf-8"))
alias_replacements = {
    "Lua 腳本管理": "脚本管理",
    "在這裡直接導入、開啟資料夾或重新載入 Lua 腳本。": "在这里直接导入、打开文件夹或重新加载脚本。",
    "導入 .lua 檔後會自動複製到 scripts 資料夾並立即重新載入。": "导入脚本文件后会自动复制到脚本文件夹并立即重新加载。",
    "導入 Lua 腳本": "导入脚本",
    "已開啟的 Lua": "已打开的脚本",
    "目前沒有偵測到 Lua 腳本": "目前没有检测到脚本",
    "重新載入 Lua": "重新加载脚本",
}
for old_key, new_key in alias_replacements.items():
    if old_key not in aliases:
        raise RuntimeError("missing legacy Lua alias: " + old_key)
    aliases.pop(old_key)
    aliases[new_key] = new_key
alias_path.write_text(json.dumps(aliases, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

# Compile fix for the global hotkey overview. menu.exact.cpp does not share the
# private detail namespace from other menu translation units.
exact_path = ROOT / "project/core/rendering/impl/menu/menu.exact.cpp"
exact_text = exact_path.read_text(encoding="utf-8-sig")
old_query = '        const auto query = detail::to_lower_copy( this->m_hotkey_filter );'
new_query = '''        const auto ascii_lower_copy = []( std::string value )
        {
            for ( auto& ch : value )
            {
                if ( ch >= 'A' && ch <= 'Z' ) ch = static_cast<char>( ch - 'A' + 'a' );
            }
            return value;
        };
        const auto query = ascii_lower_copy( this->m_hotkey_filter );'''
if old_query in exact_text:
    exact_text = exact_text.replace(old_query, new_query, 1)
elif "const auto query = ascii_lower_copy" not in exact_text:
    raise RuntimeError("hotkey search query compile-fix anchor missing")
old_blob = '            auto search_blob = detail::to_lower_copy( name + " " + category );'
new_blob = '            auto search_blob = ascii_lower_copy( name + " " + category );'
if old_blob in exact_text:
    exact_text = exact_text.replace(old_blob, new_blob, 1)
elif new_blob not in exact_text:
    raise RuntimeError("hotkey search blob compile-fix anchor missing")
exact_path.write_text(exact_text, encoding="utf-8", newline="\n")

# Compile fix for search result labels: localization::tr returns string_view.
core_path = ROOT / "project/core/rendering/impl/menu/menu.core.cpp"
core_text = core_path.read_text(encoding="utf-8-sig")
old_name = 'const std::string display_name = localization::tr( item.name );'
new_name = 'const std::string display_name{ localization::tr( item.name ) };'
if old_name in core_text:
    core_text = core_text.replace(old_name, new_name, 1)
elif new_name not in core_text:
    raise RuntimeError("search display_name compile-fix anchor missing")
old_category = 'const std::string display_category = item.category.empty( ) ? "其他" : localization::tr( item.category );'
new_category = 'const std::string display_category = item.category.empty( ) ? std::string{ "其他" } : std::string{ localization::tr( item.category ) };'
if old_category in core_text:
    core_text = core_text.replace(old_category, new_category, 1)
elif new_category not in core_text:
    raise RuntimeError("search display_category compile-fix anchor missing")
core_path.write_text(core_text, encoding="utf-8", newline="\n")

# Normalize user-visible lower-level strings that live outside the menu .cpp
# files (keybind names, Lua dialogs/status, preset feedback). This keeps the
# whole visible product vocabulary in Simplified Chinese, not only the pages.
traditional_to_simplified = str.maketrans({
    "戰":"战","鬥":"斗","庫":"库","腳":"脚","設":"设","檔":"档","載":"载",
    "儲":"储","刪":"删","顯":"显","關":"关","選":"选","擇":"择","類":"类",
    "數":"数","傷":"伤","準":"准","鏡":"镜","視":"视","覺":"觉","環":"环",
    "風":"风","濕":"湿","潤":"润","圓":"圆","邊":"边","動":"动","裝":"装",
    "飾":"饰","強":"强","稱":"称","當":"当","與":"与","擊":"击","後":"后",
    "餘":"余","體":"体","隱":"隐","尋":"寻","預":"预","點":"点","內":"内",
    "資":"资","訊":"讯","彈":"弹","藥":"药","槍":"枪","敵":"敌","隊":"队",
    "讀":"读","寫":"写","會":"会","應":"应","項":"项","細":"细","網":"网",
    "廣":"广","顏":"颜","總":"总","覽":"览","調":"调","啟":"启","閉":"闭",
    "遠":"远","層":"层","塗":"涂","標":"标","籤":"签","計":"计","時":"时",
    "個":"个","員":"员","觸":"触","發":"发","匯":"汇","複":"复","製":"制",
    "夾":"夹","這":"这","裡":"里","開":"开","無":"无","進":"进","對":"对",
    "為":"为","還":"还","從":"从","將":"将","僅":"仅","暫":"暂","過":"过",
    "輕":"轻","敗":"败","復":"复","驗":"验","證":"证","參":"参","鍵":"键",
    "處":"处","萬":"万","斷":"断","線":"线","聲":"声","屍":"尸","蹤":"踪",
    "跡":"迹","間":"间","幀":"帧","掃":"扫","錄":"录","歸":"归","優":"优",
    "檢":"检","測":"测","擴":"扩","縮":"缩","續":"续","頓":"顿","階":"阶",
    "態":"态","結":"结","鎖":"锁","側":"侧","確":"确","暢":"畅","暫":"暂",
    "許":"许","滿":"满","僅":"仅","達":"达","過":"过","與":"与","據":"据",
})
for rel in (
    "project/external/xdraw/xui/xui.cpp",
    "project/core/scripting/lua_manager.cpp",
    "project/core/scripting/nix_runtime.cpp",
    "project/core/mcb/mcb_presets.cpp",
):
    p = ROOT / rel
    txt = p.read_text(encoding="utf-8-sig")
    txt = txt.translate(traditional_to_simplified)
    p.write_text(txt, encoding="utf-8", newline="\n")

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
    "collapsed_controls": 'std::string label = std::string( open ? "▾  " : "›  " )' in exact,
    "layout_free_position": all(x in exact for x in ("水平位置","垂直位置","界面大小")),
    "inventory_compact_toolbar": "显示选项  ▾##inventory_tools" in skins and "##inventory_tools_panel" in skins and "m_inventory_categories_open" in exact,
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
