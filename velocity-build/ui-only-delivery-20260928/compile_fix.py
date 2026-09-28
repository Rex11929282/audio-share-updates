from pathlib import Path
import sys, json, hashlib

root=Path(sys.argv[1]).resolve()
p=root/"project/external/xdraw/xui/xui.cpp"
s=p.read_text(encoding="utf-8-sig")
old="const auto hover_anim_val = hovered ? 1.0f : 0.0f;"
new="const auto hover_anim_val = ( can_interact && input.in_rect( slider_interaction ) ) ? 1.0f : 0.0f;"
if s.count(old)!=1:
    raise RuntimeError(f"slider hover compile-fix anchor count={s.count(old)}")
s=s.replace(old,new,1)
p.write_text(s,encoding="utf-8",newline="\n")
report={
    "fix":"slider hover scope",
    "semantics":"exact visible slider interaction rect; no sticky hover",
    "sha256":hashlib.sha256(p.read_bytes()).hexdigest(),
}
(root/"MCB_UI_ONLY_COMPILE_FIX.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
