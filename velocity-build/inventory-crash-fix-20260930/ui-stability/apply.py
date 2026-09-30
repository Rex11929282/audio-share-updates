"""Apply the reviewed Run92 UI-only follow-up and extend real native regressions."""
from pathlib import Path
import hashlib, json, subprocess, sys
HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
patch = HERE / 'ui-stability.patch'
args = ['git', '-C', str(REPO), 'apply', '--ignore-space-change', '--ignore-whitespace', '--directory=cs2/MCB-CS2', str(patch)]
subprocess.run(args[:4] + ['--check'] + args[4:], check=True)
subprocess.run(args, check=True)
# Extend the earlier translation audit to characters that its legacy set omitted.
visible_files = [ROOT / 'project/external/xdraw/xui/xui.cpp', ROOT / 'project/core/scripting/lua_manager.cpp']
for file in visible_files:
    text = file.read_text(encoding='utf-8-sig')
    for leaked in ('切換','未綁定','上一頁','下一頁','清除綁定','導入','未知錯誤'):
        assert leaked not in text, f'unsimplified visible label {leaked} in {file}'
font_source = (ROOT / 'project/external/xdraw/xdraw.cpp').read_text(encoding='utf-8-sig')
assert font_source.index('L"msyh.ttc"') < font_source.index('L"simsun.ttc"') < font_source.index('L"msjh.ttc"') < font_source.index('L"mingliu.ttc"'), 'font fallback must prefer Simplified Chinese'
test_path = ROOT.parents[2] / 'velocity-build/unified/tests/native_ui.cpp'
s = test_path.read_text(encoding='utf-8-sig')
assert s.count('int main(){') == 1
assert s.count('  host h;') == 1
fragments = ['native_regression.cpp', 'native_hotkey_regression.cpp', 'native_search_regression.cpp', 'native_hud_editor_fragment.hpp']
s = s.replace('int main(){', '\n'.join((HERE / name).read_text(encoding='utf-8') for name in fragments) + '\nint main(){', 1)
s = s.replace('  host h;', '  host h;\n  h.resize(1440,1080,1440,1080);ui_stability_regression();ui_autoheight_regression();ui_viewport_regression();ui_model_regression();ui_hotkey_scope_regression();ui_search_display_regression();ui_hud_editor_regression(h);', 1)
test_path.write_text(s, encoding='utf-8', newline='\n')
report = {'name':'UI stability follow-up 2026-09-30','baseline_commit':'f1ab0e76098287f8c3460ea673ec02c07a6d7008','patch_sha256':hashlib.sha256(patch.read_bytes()).hexdigest(),'scope':'UI layout, search navigation, window lifetime; no gameplay algorithms changed','native_regression':'scheduled in existing native-ui CI step','in_game_runtime':'NOT_TESTED'}
(ROOT / 'MCB_UI_STABILITY_20260930.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
