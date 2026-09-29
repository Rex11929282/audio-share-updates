from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()
REPO=ROOT.parents[1]
PREFIX=Path("cs2/MCB-CS2")

payload=(HERE/"patch.b64").read_text(encoding="ascii").strip()
if len(payload)!=6248:
    raise RuntimeError(f"unexpected polish payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="afa9a8534e671bc649d8a2a30b75a87cb70486a1911107632aa1ed57f649ca58":
    raise RuntimeError("polish payload hash mismatch")
patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=17890:
    raise RuntimeError(f"unexpected polish patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="66c3f639a2008870d1e42b55306d07656a0716dc159915430b42bf5f911c8eb5":
    raise RuntimeError("polish patch hash mismatch")

patch_file=ROOT/"MCB_UI_AUDIT_POLISH2_20260929.patch"
patch_file.write_bytes(patch)
args=["git","-C",str(REPO),"apply","--directory="+PREFIX.as_posix(),str(patch_file)]
subprocess.run(args[:4]+["--check"]+args[4:],check=True)
subprocess.run(args,check=True)

def read(rel): return (ROOT/rel).read_text(encoding="utf-8-sig")
exact=read("project/core/rendering/impl/menu/menu.exact.cpp")
core=read("project/core/rendering/impl/menu/menu.core.cpp")
checks={
    "drag_layout_editor":"拖动组件移动" in exact and "active_node" in exact and "layout_reset_all" in exact,
    "layout_resize_handle":"resizing = handle_hovered" in exact,
    "misc_auto_height":"begin_child( child, aw, 0.0f, false )" in exact,
    "same_key_neutral":"同键绑定 ×" in exact and "按键冲突" not in exact,
    "toolbar_tooltips":all(x in core for x in ("全局搜索","界面布局编辑","全局快捷键","个人与外观")),
    "search_breadcrumb":"display_category.replace" in core and '" > "' in core,
}
bad=[k for k,v in checks.items() if not v]
if bad: raise RuntimeError("UI audit polish2 postcondition failed: "+", ".join(bad))
report={"name":"MCB UI audit polish 2 2026-09-29","patch_sha256":hashlib.sha256(patch).hexdigest(),"checks":checks,"runtime":"NOT_TESTED"}
(ROOT/"MCB_UI_AUDIT_POLISH2_20260929.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
