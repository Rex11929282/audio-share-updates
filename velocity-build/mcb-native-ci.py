"""Build provenance and tests for the separately identified native source integration."""
from pathlib import Path, PurePosixPath
import base64, hashlib, io, json, os, shutil, subprocess, sys, tarfile
PARTS = Path('velocity-build/mcb-native-patch')
PATCH = Path('mcb-native-patch')
PROJECT = Path('mcb-src/cs2/MCB-CS2')
RELEASE = Path('release')
ARCHIVE_SHA = '5c9e86011284ad05db6c93d66a6a62d3223f10f82d8fbaed987e3fb0286ba6fb'
def digest(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def canonical_text(p):
    return Path(p).read_text(encoding='utf-8-sig').encode('utf-8')
def unpack():
    payload=''.join((PARTS/f'part{i}.b64').read_text(encoding='utf-8').strip() for i in range(4))
    data=base64.b64decode(payload,validate=True)
    if hashlib.sha256(data).hexdigest()!=ARCHIVE_SHA: raise ValueError('Patch archive hash mismatch')
    PATCH.mkdir(exist_ok=False)
    with tarfile.open(fileobj=io.BytesIO(data),mode='r:xz') as t:
        for member in t:
            p=PurePosixPath(member.name)
            if not member.isfile() or p.is_absolute() or '..' in p.parts or len(p.parts)!=1:
                raise ValueError('Unexpected patch member')
            if member.size>200000: raise ValueError('Unexpected member size')
            (PATCH/member.name).write_bytes(t.extractfile(member).read())
    print('Own source patch hash and safe extraction: PASS')
def tests():
    RELEASE.mkdir(exist_ok=True)
    identity={}
    for name in ('runtime.hpp','appearance.hpp','mcb_batch_guard.hpp'):
        target=PROJECT/'project/core/mcb'/name
        if digest(PATCH/name)!=digest(target): raise ValueError('Disconnected test header '+name)
        identity[name]={'mode':'raw_bytes','patch_sha256':digest(PATCH/name),'project_sha256':digest(target)}
    original=PATCH/'coord.hpp';target=PROJECT/'project/external/xdraw/xui/ui_coordinate_map.hpp'
    if canonical_text(original)!=canonical_text(target):
        raise ValueError('Disconnected coordinate header (normalized UTF-8 text differs)')
    identity['coord.hpp']={'mode':'UTF8_BOM_and_newline_normalized_only','patch_sha256':digest(original),'project_sha256':digest(target),'normalized_sha256':hashlib.sha256(canonical_text(target)).hexdigest()}
    (RELEASE/'NATIVE_TEST_SOURCE_IDENTITY.json').write_text(json.dumps(identity,indent=2),encoding='utf-8')
    subprocess.run([sys.executable,str(PATCH/'generate_label_test.py'),'mcb-src',str(PATCH/'test_labels.cpp')],check=True)
    dev=Path(os.environ['VSROOT'])/'VC/Auxiliary/Build/vcvars64.bat'
    lines=['@echo off',f'call "{dev}"','if errorlevel 1 exit /b %errorlevel%']
    names=['test_runtime','test_coord','test_batch_guard','test_source_bindings','test_labels','test_presets']
    for name in names:
        exe=(RELEASE/(name+'.exe')).resolve()
        source=(PATCH/(name+'.cpp')).resolve()
        lines += [f'cl /nologo /EHsc /std:c++20 /utf-8 /W3 /I"{PATCH.resolve()}" /I"{(PROJECT/"project").resolve()}" "{source}" /Fe:"{exe}"',
                  'if errorlevel 1 exit /b %errorlevel%', f'"{exe}"', 'if errorlevel 1 exit /b %errorlevel%']
    lines+=['exit /b 0']
    cmd=RELEASE/'native_tests.cmd';cmd.write_text('\n'.join(lines),encoding='utf-8')
    process=subprocess.run(['cmd','/d','/c',str(cmd.resolve())],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    (RELEASE/'NATIVE_TESTS.log').write_text(process.stdout,encoding='utf-8');print(process.stdout)
    if process.returncode: raise RuntimeError('Windows native contract tests failed')
    (RELEASE/'NATIVE_TESTS.json').write_text(json.dumps({'status':'PASS','platform':'Windows x64 MSVC','assertions_enabled':True,
       'test_programs':names,'headers_match_compiled_project':True,'coordinate_identity_normalizes_newlines':True,'game_runtime_tested':False},indent=2),encoding='utf-8')
def package():
    out=RELEASE/'MCB_NATIVE';(out/'runtime').mkdir(parents=True,exist_ok=True)
    products=[(Path('mcb-src/cs2/bin/MCB-CS2-v6.2-exact-panel.dll'),out/'MCB_NATIVE_INTEGRATED.dll'),
        (Path('mcb-src/cs2/bin/MCB-CS2-v6.2-exact-panel.pdb'),out/'MCB_NATIVE_INTEGRATED.pdb'),
        (Path('luajit/src/lua51.dll'),out/'runtime/lua51.dll'),(Path('luajit/COPYRIGHT'),out/'runtime/LuaJIT-COPYRIGHT.txt')]
    for src,dst in products:
        if not src.is_file(): raise ValueError('Missing actual full build product: '+str(src))
        shutil.copy2(src,dst)
    shutil.copy2(PROJECT/'MCB_INTEGRATION_MANIFEST.json',RELEASE/'MCB_INTEGRATION_MANIFEST.json')
    proof={'workflow_sha':os.environ.get('GITHUB_SHA'),'workflow_run':os.environ.get('GITHUB_RUN_ID'),
      'route':'single_native_source_rebuild','patch_archive_sha256':ARCHIVE_SHA,
      'exact_b16_binary_equivalence':False,'in_game_validation':'NOT_TESTED','full_nixware_api':'NOT_PROVEN',
      'original_attackware_cfg_format':'NOT_COMPATIBLE_USE_NATIVE_PRESETS',
      'products':{str(dst.relative_to(RELEASE)):{'bytes':dst.stat().st_size,'sha256':digest(dst)} for _,dst in products},
      'modified_source_sha256':{str(p.relative_to(PROJECT)):digest(p) for p in (PROJECT/'project').rglob('*') if p.is_file() and p.suffix in ('.cpp','.hpp','.h')}}
    (RELEASE/'NATIVE_BUILD_PROVENANCE.json').write_text(json.dumps(proof,indent=2),encoding='utf-8')
    shutil.copytree(PATCH,RELEASE/'native-patch-source',dirs_exist_ok=True)
    print('Actual native full build packaged. Game runtime and exact donor equivalence NOT claimed.')
if __name__=='__main__':
    {'unpack':unpack,'tests':tests,'package':package}[sys.argv[1]]()
