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
test_path = ROOT.parents[2] / 'velocity-build/unified/tests/native_ui.cpp'
s = test_path.read_text(encoding='utf-8-sig')
assert s.count('int main(){') == 1
assert s.count('  host h;') == 1
s = s.replace('int main(){', (HERE / 'native_regression.cpp').read_text(encoding='utf-8') + '\nint main(){', 1)
s = s.replace('  host h;', '  host h;\n  h.resize(1440,1080,1440,1080);ui_stability_regression();ui_autoheight_regression();ui_viewport_regression();ui_model_regression();', 1)
test_path.write_text(s, encoding='utf-8', newline='\n')
report = {'name':'UI stability follow-up 2026-09-30','baseline_commit':'f1ab0e76098287f8c3460ea673ec02c07a6d7008','patch_sha256':hashlib.sha256(patch.read_bytes()).hexdigest(),'scope':'UI layout, search navigation, window lifetime; no gameplay algorithms changed','native_regression':'scheduled in existing native-ui CI step','in_game_runtime':'NOT_TESTED'}
(ROOT / 'MCB_UI_STABILITY_20260930.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
