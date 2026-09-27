from pathlib import Path
import sys,json,hashlib
root=Path(sys.argv[1])
changed=[]
repls={
    '"Lua 腳本"':'"腳本"',
    '"目前沒有 Lua 腳本"':'"目前沒有腳本"',
}
for p in (root/'project/core/rendering/impl/menu').glob('*.cpp'):
    s=p.read_text(encoding='utf-8-sig')
    t=s
    for a,b in repls.items():
        t=t.replace(a,b)
    if t!=s:
        p.write_text(t,encoding='utf-8',newline='\n')
        changed.append({'path':str(p.relative_to(root)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
if not changed:
    raise RuntimeError('expected Lua fixed-label replacements were not found')
remaining=[]
for p in (root/'project/core/rendering/impl/menu').glob('*.cpp'):
    s=p.read_text(encoding='utf-8-sig')
    for token in ('Lua 腳本','目前沒有 Lua 腳本'):
        if token in s:
            remaining.append({'path':str(p.relative_to(root)),'token':token})
if remaining:
    raise RuntimeError('fixed-label replacement incomplete: '+repr(remaining))
report={'changes':changed,'fixed_labels':['腳本','目前沒有腳本'],'runtime':'NOT_TESTED'}
(root/'MCB_UI_TRANSLATION_FIX.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
