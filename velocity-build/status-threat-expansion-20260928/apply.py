from pathlib import Path
import base64, hashlib, json, subprocess, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()
REPO=ROOT.parents[1]
PREFIX=Path("cs2/MCB-CS2")

payload=(HERE/"part1.txt").read_text(encoding="ascii").strip()+(HERE/"part2.txt").read_text(encoding="ascii").strip()
if len(payload)!=5224:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="330f44065c9431539128eaeeea1038089024c67b8be704da34c7a6c803c2a1e1":
    raise RuntimeError("status-threat payload hash mismatch")

patch=zlib.decompress(base64.b64decode(payload))
if len(patch)!=13219:
    raise RuntimeError(f"unexpected patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest()!="774fab514d7790781cd36e8185447eae1e6ff503dbf2472beb60ef63e0c44f2c":
    raise RuntimeError("status-threat patch hash mismatch")

patch_file=ROOT/"MCB_STATUS_THREAT_EXPANSION.patch"
patch_file.write_bytes(patch)
args=["git","-C",str(REPO),"apply","--directory="+PREFIX.as_posix(),str(patch_file)]
check=args[:4]+["--check"]+args[4:]
subprocess.run(check,check=True)
subprocess.run(args,check=True)

def read(rel):
    return (ROOT/rel).read_text(encoding="utf-8-sig")

checks={
    "status_settings":"struct status_cfg" in read("project/core/settings.hpp"),
    "status_render":"void widgets::status_monitor" in read("project/core/rendering/impl/widgets.cpp"),
    "status_ui":"狀態監控面板" in read("project/core/rendering/impl/menu/menu.exact.cpp"),
    "threat_settings":"struct threat_warning" in read("project/core/settings.hpp"),
    "threat_ui":"投擲物威脅警告" in read("project/core/rendering/impl/menu/menu.world.cpp"),
    "threat_render":"高爆威脅" in read("project/core/features/esp/projectile/projectile.overlay.cpp"),
    "draw_hook":"this->status_monitor( dl );" in read("project/core/rendering/impl/widgets.cpp"),
}
bad=[k for k,v in checks.items() if not v]
if bad:
    raise RuntimeError("status-threat expansion postcondition failed: "+", ".join(bad))
report={
    "name":"MCB status monitor + grenade threat expansion 2026-09-28",
    "patch_sha256":hashlib.sha256(patch).hexdigest(),
    "checks":checks,
    "runtime":"NOT_TESTED",
}
(ROOT/"MCB_STATUS_THREAT_EXPANSION_20260928.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
