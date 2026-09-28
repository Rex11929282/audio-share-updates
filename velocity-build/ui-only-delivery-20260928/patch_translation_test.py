from pathlib import Path

p = Path("velocity-build/unified/run_tests.py")
s = p.read_text(encoding="utf-8")
old = "    remaining=[{'input':a,'output':b} for a,b in zip(values,translated) if re.search('[A-Za-z]',b.replace('MCB',''))]\n"
new = """    canonical_weapon_names={'Desert Eagle','Five-SeveN/Tec-9','R8','SSG 08'}
    remaining=[{'input':a,'output':b} for a,b in zip(values,translated)
               if b not in canonical_weapon_names and re.search('[A-Za-z]',b.replace('MCB',''))]
"""
if old not in s:
    raise RuntimeError("translation policy anchor missing")
p.write_text(s.replace(old,new,1), encoding="utf-8", newline="\n")
print("TRANSLATION_TEST_POLICY_PATCHED=YES")
