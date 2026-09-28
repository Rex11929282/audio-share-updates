from pathlib import Path
import sys, json, hashlib

root=Path(sys.argv[1]).resolve()
p=root/"project/core/rendering/impl/widgets.cpp"
s=p.read_text(encoding="utf-8-sig")
inc="#include <core/localization/zh_tw.hpp>"
if inc not in s:
    anchor="#include <core/settings.hpp>\n"
    if anchor not in s:
        raise RuntimeError("widgets include anchor missing")
    s=s.replace(anchor,anchor+inc+"\n",1)
    p.write_text(s,encoding="utf-8",newline="\n")

if "localization::tr" not in p.read_text(encoding="utf-8-sig"):
    raise RuntimeError("localization call missing after fix")

report={"fix":"widgets localization include","sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
(root/"MCB_BUGFIX_ROUND2_COMPILE_FIX.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
