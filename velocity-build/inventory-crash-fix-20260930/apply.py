from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()
REPO=ROOT.parents[1]
PREFIX=Path("cs2/MCB-CS2")

payload=(HERE/"part1.txt").read_text(encoding="ascii").strip()
if len(payload)!=5040:
    raise RuntimeError(f"unexpected inventory-fix payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="cd3171f78d4a134991c64f5bb8667e0d0ff85340ef491fd13e5fa41aa012b2a4":
    raise RuntimeError("inventory-fix payload hash mismatch")
patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=16258:
    raise RuntimeError(f"unexpected inventory-fix patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="49b46e8f99b467987388b8ec7ac0cbfaa9763dd6bd9f1799647138cba7962f61":
    raise RuntimeError("inventory-fix patch hash mismatch")

patch_file=ROOT/"MCB_INVENTORY_CRASH_FIX.patch"
patch_file.write_bytes(patch)
args=["git","-C",str(REPO),"apply","--directory="+PREFIX.as_posix(),str(patch_file)]
subprocess.run(args[:4]+["--check"]+args[4:],check=True)
subprocess.run(args,check=True)

# Quick-config UI is removed, but the underlying MCB preset namespace is still
# required by ui_views_v62.inl. Restore the support compilation unit/include so
# unrelated custom pages keep linking correctly.
exact_path=ROOT/"project/core/rendering/impl/menu/menu.exact.cpp"
exact_text=exact_path.read_text(encoding="utf-8-sig")
inc="#include <core/mcb/mcb_presets.hpp>\n"
if inc not in exact_text:
    exact_text=inc+exact_text
exact_path.write_text(exact_text,encoding="utf-8",newline="\n")

vcx_path=ROOT/"MCB-CS2.vcxproj"
vcx=vcx_path.read_text(encoding="utf-8-sig")
compile_line='    <ClCompile Include="project\\core\\mcb\\mcb_presets.cpp" />\n'
header_line='    <ClInclude Include="project\\core\\mcb\\mcb_presets.hpp" />\n'
if compile_line not in vcx:
    marker='    <ClCompile Include="project\\core\\mcb\\mcb_shot_evidence.cpp" />\n'
    if marker not in vcx: raise RuntimeError("vcx preset compile restore anchor missing")
    vcx=vcx.replace(marker,marker+compile_line,1)
if header_line not in vcx:
    marker='    <ClInclude Include="project\\core\\mcb\\mcb_shot_evidence.hpp" />\n'
    if marker not in vcx: raise RuntimeError("vcx preset header restore anchor missing")
    vcx=vcx.replace(marker,marker+header_line,1)
vcx_path.write_text(vcx,encoding="utf-8",newline="\n")

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")
checks={
    "quick_presets_removed": all(x not in read("project/core/rendering/impl/menu/menu.ragebot.cpp")+read("project/core/rendering/impl/menu/menu.legitbot.cpp")+read("project/core/rendering/impl/menu/menu.player.cpp")+read("project/core/rendering/impl/menu/menu.world.cpp") for x in ("##rage_p1","##legit_p1","##esp_full","##item_all")),
    "view_quick_buttons_removed": all(x not in read("project/core/rendering/impl/menu/menu.misc.cpp") for x in ("4:3##ar","默认##vm","宽视野##vm")),
    "preset_support_retained": "mcb_presets.cpp" in read("MCB-CS2.vcxproj") and "#include <core/mcb/mcb_presets.hpp>" in read("project/core/rendering/impl/menu/menu.exact.cpp"),
    "inventory_tools_state": "this->m_inventory_tools_open" in read("project/core/rendering/impl/menu/menu.skins.cpp") and "mutable bool m_inventory_tools_open" in read("project/core/rendering/rendering.hpp"),
    "adaptive_inventory_columns": "max_fit_columns" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "inventory_null_guards": "if ( !w ) continue;" in read("project/core/rendering/impl/menu/menu.skins.cpp") and "if ( !def || card.w < 40.0f" in read("project/core/rendering/impl/menu/menu.skins.cpp"),
    "decode_throttle": "decode_requests >= 4" in read("project/core/features/changer/impl/econ_item_system.cpp"),
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("inventory fix postcondition failed: "+", ".join(bad))
report={"name":"MCB inventory crash + quick preset removal 2026-09-30","patch_sha256":hashlib.sha256(patch).hexdigest(),"checks":checks,"runtime":"NOT_TESTED"}
(ROOT/"MCB_INVENTORY_CRASH_FIX_20260930.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
