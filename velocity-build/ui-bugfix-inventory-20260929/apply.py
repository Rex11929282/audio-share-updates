from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()
REPO=ROOT.parents[1]
PREFIX=Path("cs2/MCB-CS2")

payload=(HERE/"patch.b64").read_text(encoding="ascii").strip()
if len(payload)!=5800:
    raise RuntimeError(f"unexpected UI bugfix payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="405afa6f6bb51d68642130fb8e47d82edfd77f72ae8d7b0b91a27b1be0773364":
    raise RuntimeError("UI bugfix payload hash mismatch")
patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=18365:
    raise RuntimeError(f"unexpected UI bugfix patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="b04a4fa537e9fefd2b09c40cf80e3f82b794c30649e294383dda1dd833b42d34":
    raise RuntimeError("UI bugfix patch hash mismatch")

patch_file=ROOT/"MCB_UI_INVENTORY_BUGFIX_20260929.patch"
patch_file.write_bytes(patch)
args=["git","-C",str(REPO),"apply","--ignore-space-change","--ignore-whitespace","--directory="+PREFIX.as_posix(),str(patch_file)]
subprocess.run(args[:4]+["--check"]+args[4:],check=True)
subprocess.run(args,check=True)

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")

rage=read("project/core/rendering/impl/menu/menu.ragebot.cpp")
legit=read("project/core/rendering/impl/menu/menu.legitbot.cpp")
player=read("project/core/rendering/impl/menu/menu.player.cpp")
world=read("project/core/rendering/impl/menu/menu.world.cpp")
skins=read("project/core/rendering/impl/menu/menu.skins.cpp")
exact=read("project/core/rendering/impl/menu/menu.exact.cpp")
rendering=read("project/core/rendering/rendering.hpp")

removed_tokens=("##rage_p1","##rage_p2","##rage_p3","##legit_p1","##legit_p2","##legit_p3","##esp_full","##esp_min","##esp_off","##cham_one","##cham_two","##cham_off","##item_all","##item_key","##item_off")
merged="\n".join((rage,legit,player,world))
checks={
    "quick_presets_removed": all(t not in merged for t in removed_tokens),
    "legit_responsive": "const auto single_col = content_w < 760.0f;" in legit and "if ( !single_col ) xui::layout::set_cursor" in legit,
    "misc_all_sections_visible": "m_misc_section_open" not in exact and "xui::text( sections[i].title" in exact,
    "inventory_dynamic_columns": "effective_columns(" in skins,
    "inventory_null_guards": all(x in skins for x in ("if ( !w ) continue;","if ( !a ) continue;","if ( !def || card.w < 48.0f","if ( !pk || !weapon")),
    "inventory_texture_guards": "img->width > 0 && img->height > 0 && img->srv.Get( ) != nullptr" in skins,
    "inventory_tools_member_state": "this->m_inventory_tools_open" in skins and "static bool tools_open" not in skins and "mutable bool m_inventory_tools_open" in rendering,
    "inventory_invalid_browser_guard": "detail::request_page( detail::skins_page::grid );" in skins and "if ( !weapon )" in skins,
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("UI inventory bugfix postcondition failed: "+", ".join(bad))
report={"name":"MCB UI + inventory crash bugfix 2026-09-29","patch_sha256":hashlib.sha256(patch).hexdigest(),"checks":checks,"runtime":"NOT_TESTED"}
(ROOT/"MCB_UI_INVENTORY_BUGFIX_20260929.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
