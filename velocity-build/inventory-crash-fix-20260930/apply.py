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

# Remove the remaining native one-click preset block from the config view.
# The user asked for quick configuration to be deleted completely, so no
# Legit/Rage/HVH preset buttons remain visible or compiled into this page.
views_path=ROOT/"project/core/rendering/ui_views_v62.inl"
views=views_path.read_text(encoding="utf-8-sig")
start='    xui::text("Native gameplay presets",tokens::col_text);'
end='    xui::layout::separator();\n'
pos=views.find(start)
if pos >= 0:
    end_pos=views.find(end,pos)
    if end_pos < 0:
        raise RuntimeError("native preset block end anchor missing")
    views=views[:pos]+views[end_pos+len(end):]
elif any(x in views for x in ("Legit CFG","Rage CFG","HVH CFG","::mcb::presets")):
    raise RuntimeError("native preset block shape changed")
views_path.write_text(views,encoding="utf-8",newline="\n")

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")
checks={
    "quick_presets_removed": all(x not in read("project/core/rendering/impl/menu/menu.ragebot.cpp")+read("project/core/rendering/impl/menu/menu.legitbot.cpp")+read("project/core/rendering/impl/menu/menu.player.cpp")+read("project/core/rendering/impl/menu/menu.world.cpp") for x in ("##rage_p1","##legit_p1","##esp_full","##item_all")),
    "view_quick_buttons_removed": all(x not in read("project/core/rendering/impl/menu/menu.misc.cpp") for x in ("4:3##ar","默认##vm","宽视野##vm")),
    "native_quick_configs_removed": all(x not in read("project/core/rendering/ui_views_v62.inl") for x in ("Legit CFG","Rage CFG","HVH CFG","::mcb::presets")) and "mcb_presets.cpp" not in read("MCB-CS2.vcxproj"),
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
