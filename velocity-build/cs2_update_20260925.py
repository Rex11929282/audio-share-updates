"""Port the existing source-rebuild branch to the reviewed September update.

This is NOT a binary patch for the historical b15 DLL. No original user Lua,
local settings, anti-cheat settings or historical binary is modified here.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import unittest
import zipfile

OLD_SOURCE = "4392221937f12490dcf14efcba4e85115ab580c9"
NEW_SOURCE = "a6f200d09e17b144584cdd80c5429d7189bf2259"
TEMPLATE_BLOB = "ec79994a2381e0163421c811f97fc72c12409a7a"
WORKFLOW = Path('.github/workflows/mcb-nix-luajit-build.yml')
PATCH_DIR = Path(__file__).resolve().parent
MARKER = 'MCB_UPDATE_WEAPON_FIRE_LISTENER'
ALLOWED_STEPS = (
    'Prepare report', 'Locate Visual Studio and vcpkg',
    'Fetch pinned MCB source', 'Fetch exact LuaJIT revision',
    'Build exact LuaJIT x64 with MSVC',
    'Validate LuaJIT version, FFI and bytecode',
    'Validate Nix LuaJIT cdata value bootstrap',
    'Validate Nix render Lua bootstrap',
    'Compile and run independent DLL loader smoke',
    'Apply MCB and Nix runtime patch chain',
    'Validate responsive UI input mapping',
    'Validate CFG preset and shot evidence integration',
    'Pin built LuaJIT hash into runtime manifest',
    'Validate dual runtime source integration', 'Build MCB Ship x64',
)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if text.count(old) != 1:
        raise ValueError(f'{label}: expected one anchor, got {text.count(old)}')
    return text.replace(old, new, 1)


def add_weapon_listener(text: str) -> str:
    """Add an actual checked listener, never a fictitious m_events member."""
    newline = '\r\n' if '\r\n' in text else '\n'
    src = text.replace('\r\n', '\n')
    weapon_pattern = r'register_listener\s*\(\s*xs\s*\(\s*"weapon_fire"\s*\)'
    existing = list(re.finditer(weapon_pattern, src))
    if MARKER in src:
        if len(existing) != 1 or src.count('g_impacts.on_weapon_fire(') != 1:
            raise ValueError('inconsistent existing weapon-fire integration')
        if src.count('g_nix.on_game_event("weapon_fire"') != 1:
            raise ValueError('existing weapon-fire Lua dispatch missing/duplicated')
        return text
    if existing:
        raise ValueError('unrecognized weapon-fire listener; manual reconciliation required')
    anchor = re.compile(
        r'(?m)^(?P<i>[ \t]*)if\s*\(\s*!\s*register_listener\s*\('
        r'\s*xs\s*\(\s*"bullet_impact"\s*\)')
    matches = list(anchor.finditer(src))
    if len(matches) != 1:
        raise ValueError(f'expected one checked bullet-impact listener, got {len(matches)}')
    match = matches[0]
    i = match.group('i')
    block = '\n'.join((
        i + '// ' + MARKER,
        i + 'if ( !register_listener( xs( "weapon_fire" ), [ ]( void* event ) {',
        i + '    scripting::g_nix.on_game_event("weapon_fire", reinterpret_cast<std::uintptr_t>(event));',
        i + '    features::misc::g_impacts.on_weapon_fire(reinterpret_cast<std::uintptr_t>(event));',
        i + '} ) )',
        i + '{',
        i + '    this->shutdown( );',
        i + '    return false;',
        i + '}', '',
    ))
    out = src[:match.start()] + block + src[match.start():]
    if len(re.findall(weapon_pattern, out)) != 1:
        raise ValueError('weapon-fire listener postcondition failed')
    return out.replace('\n', newline)


def extract_step(workflow: str, name: str) -> str:
    """Parse only the reviewed workflow's literal, indented PowerShell blocks.

    Not a general YAML parser: unfamiliar constructs fail rather than silently
    acquiring new execution semantics. No dependency installation is required.
    """
    lines = workflow.splitlines()
    target = '      - name: ' + name
    indexes = [n for n, line in enumerate(lines) if line == target]
    if len(indexes) != 1:
        raise ValueError('workflow step absent or duplicated: ' + name)
    first = indexes[0] + 1
    last = next((n for n in range(first, len(lines))
                 if lines[n].startswith('      - ')), len(lines))
    header = lines[first:last]
    if '        shell: pwsh' not in header:
        raise ValueError('unexpected shell: ' + name)
    pos = next((n for n in range(first, last)
                if lines[n] == '        run: |'), None)
    if pos is None:
        raise ValueError('literal run block missing: ' + name)
    result = []
    for line in lines[pos + 1:last]:
        if line and not line.startswith('          '):
            raise ValueError('unexpected run indentation: ' + name)
        result.append(line[10:] if line else '')
    return '\n'.join(result).rstrip() + '\n'


def run_step(name: str) -> None:
    if name not in ALLOWED_STEPS:
        raise ValueError('step is not allowlisted: ' + name)
    actual = subprocess.check_output(['git', 'hash-object', str(WORKFLOW)], text=True).strip()
    if actual != TEMPLATE_BLOB:
        raise ValueError('reviewed workflow changed; re-review before running it')
    block = extract_step(WORKFLOW.read_text(encoding='utf-8'), name)
    if name == 'Fetch pinned MCB source':
        block = replace_once(block, OLD_SOURCE, NEW_SOURCE, 'source revision')
    if name == 'Validate CFG preset and shot evidence integration':
        block = replace_once(block, 'm_events[ "weapon_fire" ]',
                             'register_listener( xs( "weapon_fire" )', 'listener check')
        block = block.replace('native_cfg_presets_compiled_source=PASS',
                              'native_cfg_presets_source_markers=PASS')
    if name == 'Validate responsive UI input mapping':
        for key in ('ui_larger_hit_targets', 'ui_focus_loss_cleanup'):
            block = block.replace(key + '=PASS', key + '_source_markers=PASS')
    release = Path('release')
    release.mkdir(exist_ok=True)
    slug = re.sub('[^A-Za-z0-9]+', '_', name)
    script = release / (slug + '.ps1')
    script.write_text("$ErrorActionPreference = 'Stop'\n" + block, encoding='utf-8')
    with (release / (slug + '.log')).open('w', encoding='utf-8') as log:
        process = subprocess.Popen(['pwsh', '-NoProfile', '-File', str(script.resolve())],
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   text=True, encoding='utf-8', errors='replace')
        assert process.stdout is not None
        for line in process.stdout:
            sys.stdout.write(line)
            log.write(line)
        status = process.wait()
    with (release / 'UPDATE_STEPS.jsonl').open('a', encoding='utf-8') as log:
        log.write(json.dumps({'step': name, 'exit_code': status}) + '\n')
    if status:
        raise SystemExit(status)


def prepare() -> None:
    root = Path('mcb-src')
    actual = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != NEW_SOURCE:
        raise ValueError('unexpected upstream revision: ' + actual)
    project = root / 'cs2/velocity-cs2'
    entry = project / 'project/entry.cpp'
    text = entry.read_text(encoding='utf-8')
    text, includes = re.subn(r'(?m)^[ \t]*#include\s*[<"]utilities/discord_webhook.hpp[>"][^\n]*\n', '', text)
    text, calls = re.subn(r'(?m)^[ \t]*discord_webhook::send_test_message\s*\(\s*\)\s*;[^\n]*\n', '', text)
    if includes != 1 or calls != 1 or 'discord_webhook' in text:
        raise ValueError(f'webhook removal anchors changed: includes={includes}, calls={calls}')
    entry.write_text(text, encoding='utf-8')
    proj = project / 'velocity-cs2.vcxproj'
    text = proj.read_text(encoding='utf-8')
    text, removed = re.subn(r'(?m)^.*<Cl(?:Compile|Include)\s+Include="[^"\n]*discord_webhook\.(?:cpp|hpp)"\s*/>[^\n]*\n', '', text)
    if removed != 2:
        raise ValueError(f'expected two webhook project items, got {removed}')
    proj.write_text(text, encoding='utf-8')
    for ext in ('cpp', 'hpp'):
        (project / ('project/utilities/discord_webhook.' + ext)).unlink()

    # Correct the historical patch script while retaining its other operations.
    patch = PATCH_DIR / 'apply_cfg_shot.py'
    text = patch.read_text(encoding='utf-8')
    start = text.index('# Register local weapon_fire')
    end = text.index('report=root/', start)
    replacement = '''# Checked Source2 listener integration; validated separately.
from cs2_update_20260925 import add_weapon_listener
events=v/"project"/"core"/"systems"/"impl"/"events.cpp"
events.write_text(add_weapon_listener(events.read_text(encoding="utf-8")), encoding="utf-8")

'''
    text = text[:start] + replacement + text[end:]
    # The old report used literal backslash-n, not actual line endings.
    start = text.index('report=root/')
    text = text[:start] + text[start:].replace('\\\\n', '\\n')
    compile(text, str(patch), 'exec')
    patch.write_text(text, encoding='utf-8')
    Path('release/UPDATE_PORT.json').write_text(json.dumps({
        'upstream_revision': actual,
        'upstream_declared_build_target': '25515854',
        'user_installed_build': 'UNKNOWN',
        'route': 'source_rebuild_NOT_b15_binary_patch',
        'removed_upstream_webhook': True,
        'historical_b15_binaries_modified': False,
        'user_original_lua_modified': False,
        'full_nixware_compatibility': 'NOT_COMPLETED',
        'in_game_validation': 'NOT_TESTED',
    }, indent=2), encoding='utf-8')


def audit() -> None:
    root = Path('mcb-src/cs2/MCB-CS2/project')
    events = (root / 'core/systems/impl/events.cpp').read_text(encoding='utf-8')
    if add_weapon_listener(events) != events:
        raise ValueError('checked weapon listener not installed')
    for file in root.rglob('*'):
        if file.is_file() and file.suffix in ('.cpp', '.hpp', '.h'):
            if 'discord_webhook::send_test_message' in file.read_text(encoding='utf-8', errors='replace'):
                raise ValueError('upstream webhook call remains: ' + str(file))
    Path('release/UPDATE_SOURCE_AUDIT.txt').write_text(
        'checked_listener_source=PASS\nwebhook_call_source_absent=PASS\n'
        'source_inspection_only=YES\nin_game_runtime=NOT_TESTED\n', encoding='utf-8')


def package() -> None:
    """Package a separate source-rebuild candidate; never mix b15 helper files."""
    release = Path('release')
    source = Path('mcb-src')
    binary = source / 'cs2/bin/MCB-CS2-v6.2-exact-panel.dll'
    pdb = binary.with_suffix('.pdb')
    runtime = Path('luajit/src/lua51.dll')
    for file in (binary, pdb, runtime):
        if not file.is_file():
            raise ValueError('required build product absent: ' + str(file))
    dest = release / 'candidate-source-rebuild'
    (dest / 'runtime').mkdir(parents=True)
    shutil.copy2(binary, dest / 'MCB-CS2-update-25515854-dev.dll')
    shutil.copy2(pdb, dest / 'MCB-CS2-update-25515854-dev.pdb')
    shutil.copy2(runtime, dest / 'runtime/lua51.dll')
    shutil.copy2('luajit/COPYRIGHT', dest / 'runtime/LuaJIT-COPYRIGHT.txt')
    (dest / 'NOT_A_B15_REPLACEMENT.txt').write_text(
        'Development source rebuild. NOT a replacement helper for b15.\n'
        'Do not mix this DLL with MCB_storage.dll from an older release.\n'
        'Original user scripts and the historical rollback archive are not in this cloud workspace.\n'
        'No originals were changed or run. This is not the requested full b15 integration release.\n'
        'Upstream targets 25515854 but calls its own update partial. User build unknown.\n'
        'In-game tests: NOT_TESTED. Full Nixware API: NOT_COMPLETED.\n', encoding='utf-8')
    excluded = {'.ttf', '.otf', '.woff', '.woff2', '.ttc', '.obj', '.pdb', '.dll', '.exe', '.lib', '.pch'}
    with zipfile.ZipFile(release / 'SOURCE_SNAPSHOT.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for base, prefix in ((source / 'cs2/MCB-CS2', 'native-source'), (PATCH_DIR, 'build-tools')):
            for file in sorted(base.rglob('*')):
                if not file.is_file() or file.suffix.lower() in excluded:
                    continue
                if any(p in ('vcpkg_installed', '.git', '__pycache__', 'bin', 'obj', 'x64') for p in file.parts):
                    continue
                if file.name in ('hello.lua', 'snow_hud.lua'):
                    continue
                z.write(file, str(Path(prefix) / file.relative_to(base)))
    manifest = {}
    for file in sorted(release.rglob('*')):
        if file.is_file():
            manifest[str(file.relative_to(release))] = {
                'bytes': file.stat().st_size,
                'sha256': hashlib.sha256(file.read_bytes()).hexdigest(),
            }
    (release / 'SHA256_MANIFEST.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('source_rebuild_candidate_packaged=YES; full_b15_release=NO; in_game=NOT_TESTED')


class PatchTests(unittest.TestCase):
    BASE = 'bool events::initialize( )\n{\n\tif ( !register_listener( xs( "bullet_impact" ), [ ]( void* event ) { old_handler(event); } ) )\n\t{\n\t\treturn false;\n\t}\n\treturn true;\n}\n'

    def test_preserves_existing_listener(self):
        out = add_weapon_listener(self.BASE)
        self.assertIn('old_handler(event);', out)
        self.assertEqual(out.count('"weapon_fire"'), 2)
        self.assertIn('this->shutdown( );', out)
        self.assertNotIn('m_events[', out)

    def test_idempotent(self):
        out = add_weapon_listener(self.BASE)
        self.assertEqual(out, add_weapon_listener(out))

    def test_crlf(self):
        out = add_weapon_listener(self.BASE.replace('\n', '\r\n'))
        self.assertNotIn('\n', out.replace('\r\n', ''))
        self.assertEqual(out, add_weapon_listener(out))

    def test_whitespace(self):
        for pad in ('', ' ', '    ', '\t', '\t\t'):
            with self.subTest(pad=repr(pad)):
                self.assertIn(MARKER, add_weapon_listener(self.BASE.replace('\t', pad)))

    def test_unknown_layout_rejected(self):
        with self.assertRaises(ValueError):
            add_weapon_listener('m_events["bullet_impact"] = callback;')

    def test_duplicate_anchor_rejected(self):
        with self.assertRaises(ValueError):
            add_weapon_listener(self.BASE + self.BASE)

    def test_foreign_existing_listener_rejected(self):
        with self.assertRaises(ValueError):
            add_weapon_listener(self.BASE + 'register_listener(xs("weapon_fire"), other);')

    def test_corrupt_existing_rejected(self):
        out = add_weapon_listener(self.BASE).replace('g_nix.on_game_event', 'broken')
        with self.assertRaises(ValueError):
            add_weapon_listener(out)

    def test_exact_replacement(self):
        self.assertEqual(replace_once('abc', 'b', 'd', 'test'), 'adc')
        for source in ('ac', 'abbc'):
            with self.assertRaises(ValueError):
                replace_once(source, 'b', 'd', 'test')

    def test_workflow_extraction(self):
        fixture = '      - name: Probe\n        shell: pwsh\n        run: |\n          Write-Output ok\n\n      - name: Next\n'
        self.assertEqual(extract_step(fixture, 'Probe'), 'Write-Output ok\n')
        with self.assertRaises(ValueError):
            extract_step(fixture.replace('shell: pwsh', 'shell: bash'), 'Probe')
        with self.assertRaises(ValueError):
            extract_step(fixture, 'Unknown')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('run-step', 'prepare', 'test', 'audit', 'package'))
    parser.add_argument('name', nargs='?')
    args = parser.parse_args()
    if args.action == 'run-step':
        run_step(args.name or '')
    elif args.action == 'test':
        result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(PatchTests))
        raise SystemExit(0 if result.wasSuccessful() else 1)
    else:
        globals()[args.action]()
