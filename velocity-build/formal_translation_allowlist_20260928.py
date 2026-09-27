from pathlib import Path
p=Path("velocity-build/unified/run_tests.py")
s=p.read_text(encoding="utf-8")
old="remaining=[{'input':a,'output':b} for a,b in zip(values,translated) if re.search('[A-Za-z]',b.replace('MCB',''))]"
new="""canonical_names={'Desert Eagle','Five-SeveN/Tec-9','R8','SSG 08'}
    remaining=[{'input':a,'output':b} for a,b in zip(values,translated) if a not in canonical_names and re.search('[A-Za-z]',b.replace('MCB',''))]"""
if s.count(old)!=1:
    raise RuntimeError("translation remaining anchor mismatch")
p.write_text(s.replace(old,new,1),encoding="utf-8",newline="\n")
print("translation allowlist: canonical weapon names only")
