from pathlib import Path
import sys, json, hashlib

root=Path(sys.argv[1]).resolve()

p=root/"project/core/rendering/impl/menu/menu.misc.cpp"
s=p.read_text(encoding="utf-8-sig")
for old,new in (
    ("方向 X##env_lrx","光源俯仰##env_lrx"),
    ("方向 Y##env_lry","光源偏航##env_lry"),
    ("方向 Z##env_lrz","光源翻滾##env_lrz"),
):
    s=s.replace(old,new)
p.write_text(s,encoding="utf-8",newline="\n")

skins=root/"project/core/rendering/impl/menu/menu.skins.cpp"
ss=skins.read_text(encoding="utf-8-sig")
old="const auto aa = skin_map( ).contains( a->def_index ); const auto bb = skin_map( ).contains( b->def_index );"
new="const auto aa = detail::skin_map( ).contains( a->def_index ); const auto bb = detail::skin_map( ).contains( b->def_index );"
if old in ss:
    ss=ss.replace(old,new,1)
elif new not in ss:
    raise RuntimeError("inventory applied-first sort anchor missing")
skins.write_text(ss,encoding="utf-8",newline="\n")

checks={
    "no_xyz_labels": all(x not in s for x in ("方向 X##env_lrx","方向 Y##env_lry","方向 Z##env_lrz")),
    "chinese_light_rotation": all(x in s for x in ("光源俯仰##env_lrx","光源偏航##env_lry","光源翻滾##env_lrz")),\n    "inventory_sort_scope": "detail::skin_map( ).contains" in ss,
}
if not all(checks.values()):
    raise RuntimeError("feature expansion localization fix failed")
report={"fix":"feature expansion visible-label cleanup","checks":checks,"sha256":hashlib.sha256(p.read_bytes()).hexdigest()}
(root/"MCB_ALL_FEATURE_EXPANSION_FIX_20260928.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False))
