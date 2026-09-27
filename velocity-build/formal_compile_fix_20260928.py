from pathlib import Path
import sys,json,hashlib
root=Path(sys.argv[1])
p=root/"project/core/features/misc/impl/other.cpp"
s=p.read_text(encoding="utf-8-sig")
candidates=[
    "constexpr auto phase_count",
    "constexpr const auto phase_count",
]
t=s
for c in candidates:
    if c in t:
        t=t.replace(c,c.replace("constexpr ",""),1)
        break
if t==s:
    raise RuntimeError("phase_count constexpr anchor not found")
p.write_text(t,encoding="utf-8",newline="\n")
report={"path":str(p.relative_to(root)),"fix":"phase_count runtime const","sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
(root/"MCB_FORMAL_COMPILE_FIX.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
