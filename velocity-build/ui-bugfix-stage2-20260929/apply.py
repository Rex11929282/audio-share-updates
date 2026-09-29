from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()
REPO=ROOT.parents[1]
PREFIX=Path("cs2/MCB-CS2")

payload=(HERE/"patch.b64").read_text(encoding="ascii").strip()
if len(payload)!=4204:
    raise RuntimeError(f"unexpected stage2 payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="054a404b8a8539447330b8e37face9424f24f0067f336c6f1fee837f8639841c":
    raise RuntimeError("stage2 payload hash mismatch")
patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=16710:
    raise RuntimeError(f"unexpected stage2 patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="7c2e7c6426ce6d5e17045b618ee53ff97621c8429b52bda16f6796a276d9fd6e":
    raise RuntimeError("stage2 patch hash mismatch")

patch_file=ROOT/"MCB_UI_BUGFIX_STAGE2_20260929.patch"
patch_file.write_bytes(patch)
args=["git","-C",str(REPO),"apply","--ignore-space-change","--ignore-whitespace","--directory="+PREFIX.as_posix(),str(patch_file)]
subprocess.run(args[:4]+["--check"]+args[4:],check=True)
subprocess.run(args,check=True)

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")

core=read("project/core/rendering/impl/menu/menu.core.cpp")
widgets=read("project/core/rendering/impl/widgets.cpp")
skins=read("project/core/rendering/impl/menu/menu.skins.cpp")
other=read("project/core/features/esp/other/other.overlay.cpp")
impacts=read("project/core/features/misc/impl/impacts.cpp")

checks={
    "search_anti_aim_route": 'chosen.category_lower.find( "anti aim" ) != std::string::npos ? 2 : 0' in core,
    "search_profile_route": 'chosen.category_lower.find( "profile" )' in core and 'm_exact_page = 9' in core,
    "search_inventory_route": 'category_lower.find( "changer ui" )' in core,
    "search_refresh_each_open": 'if ( this->m_search_open ) this->rebuild_search_index( );' in core,
    "search_text_reserves_bind_badge": 'reserved_right' in core and 'xui::truncate( raw_name, text_w )' in core,
    "sidebar_preserves_user_width": 'const auto delta_w' not in core and "preserve the user's chosen window size" in core,
    "watermark_status_keybind_clamped": all(x in widgets for x in ("raw_x","raw_y","raw_base_y","raw_header_x","raw_row_x")),
    "bomb_spectator_clamped": all(x in other for x in ("raw_x","raw_y","raw_ry","raw_rx")),
    "stats_clamped": "raw_x" in impacts and "raw_y" in impacts,
    "agent_card_guard": "if ( card.w < 48.0f || card.h < 48.0f ) return;" in skins,
    "agent_null_guard": "if ( !a ) continue;" in skins,
    "agent_texture_guard": "img->width > 0 && img->height > 0 && img->srv.Get( ) != nullptr" in skins,
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("UI bugfix stage2 postcondition failed: "+", ".join(bad))
report={"name":"MCB UI bugfix stage2 2026-09-29","patch_sha256":hashlib.sha256(patch).hexdigest(),"checks":checks,"runtime":"NOT_TESTED"}
(ROOT/"MCB_UI_BUGFIX_STAGE2_20260929.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
