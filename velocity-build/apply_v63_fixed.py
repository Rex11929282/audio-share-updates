"""Apply reviewed v6.3 source deltas only to their exact v6.2 baseline."""
from pathlib import Path
import base64,hashlib,json,sys,zlib
root=Path(sys.argv[1]).resolve()/"cs2"/"MCB-CS2"
parts=sorted(Path(__file__).parent.glob('v63_delta.part*'))
if len(parts)!=5: raise SystemExit('Missing reviewed delta parts')
manifest=json.loads(zlib.decompress(base64.b85decode(b''.join(p.read_bytes().strip() for p in parts))))
pending=[]
for item in manifest['files']:
    rel=Path(item['path'])
    if rel.is_absolute() or '..' in rel.parts: raise SystemExit('Unsafe patch path')
    p=root/rel
    old=p.read_text(encoding='utf-8-sig') if p.exists() else ''
    before=hashlib.sha256(old.encode()).hexdigest() if p.exists() else None
    if before!=item['before']: raise SystemExit('Baseline mismatch: '+str(rel))
    lines=old.splitlines(keepends=True)
    for a,b,text in reversed(item['ops']): lines[a:b]=[text]
    text=''.join(lines)
    if hashlib.sha256(text.encode()).hexdigest()!=item['after']: raise SystemExit('Patch checksum mismatch: '+str(rel))
    pending.append((p,text))
for p,text in pending:
    p.parent.mkdir(parents=True,exist_ok=True)
    p.write_text(text,encoding='utf-8')
report={'version':'6.3','target':'MCB-CS2-v6.3-fixed-panel.dll','source_changes':len(pending),'fixed_size':[846,682],'sidebar':203,'native_settings_bindings':'restored from existing implementation','new_gameplay_algorithms':False,'anti_cheat_bypass_added':False,'game_runtime':'NOT_TESTED','steam_account_live':'NOT_TESTED','pixel_exact_reference_comparison':'NOT_TESTED','files':[{'path':i['path'],'sha256':i['after']} for i in manifest['files']]}
(Path(sys.argv[1])/'MCB_V63_APPLIED.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('v6.3 exact-baseline source patch applied:',len(pending),'files')
