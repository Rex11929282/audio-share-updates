"""Expand the reviewed source-only patch payload with pinned hashes; no binary execution."""
from pathlib import Path, PurePosixPath
import base64, hashlib, json, lzma
HERE = Path(__file__).resolve().parent
XZ_SHA = '0fc865026b4d493ffd09d09924089ce3d7238778b8c47d74ae84075708b3639b'
JSON_SHA = 'f61ff14bceb3177b3a43f1f0623641cfb38035a335ddb0fe72ad4bfa7b7f079e'
parts = [(HERE / ('part%d.txt' % i)).read_text(encoding='ascii').strip() for i in (1,2,3)]
assert tuple(map(len, parts)) == (8100,8100,7835), 'payload part lengths differ'
packed = base64.b85decode(''.join(parts))
assert hashlib.sha256(packed).hexdigest() == XZ_SHA, 'compressed source payload hash mismatch'
raw = lzma.decompress(packed, memlimit=256*1024*1024)
assert len(raw) == 68492 and hashlib.sha256(raw).hexdigest() == JSON_SHA, 'source manifest hash mismatch'
files = json.loads(raw)
assert isinstance(files, dict) and len(files) < 100
out = HERE.parent / 'unified'
out.mkdir(exist_ok=True)
manifest = {}
for name, text in files.items():
    p = PurePosixPath(name)
    assert not p.is_absolute() and '..' not in p.parts and '\\' not in name and ':' not in name
    assert isinstance(text, str) and p.suffix in ('.py','.hpp','.txt','.json','.cpp')
    dest = out.joinpath(*p.parts)
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(text, encoding='utf-8', newline='\n')
    manifest[name] = hashlib.sha256(text.encode('utf-8')).hexdigest()
# Pin this build to the reviewed upstream follow-up, not a moving branch.
pin_script = HERE.parent / 'cs2_update_20260925.py'
s = pin_script.read_text(encoding='utf-8')
old = 'NEW_SOURCE = "a6f200d09e17b144584cdd80c5429d7189bf2259"'
new = 'NEW_SOURCE = "c077db5d04b6f8746303179d2ed78f50e61975bf"'
assert s.count(old) == 1 and new not in s, 'unexpected source pin input'
pin_script.write_text(s.replace(old,new), encoding='utf-8', newline='\n')
Path('release').mkdir(exist_ok=True)
Path('release/UNIFIED_PAYLOAD.json').write_text(json.dumps({'source_xz_sha256':XZ_SHA,'source_json_sha256':JSON_SHA,'files':manifest,'upstream':'c077db5d04b6f8746303179d2ed78f50e61975bf','route':'source_rebuild_NOT_Attackware_binary_merge','game_runtime_tested':False},ensure_ascii=False,indent=2),encoding='utf-8')
print('Verified and expanded',len(files),'source files')
