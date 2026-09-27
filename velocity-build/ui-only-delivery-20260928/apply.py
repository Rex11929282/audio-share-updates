from pathlib import Path
import base64, hashlib, subprocess, sys, zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
parts = [(HERE / f"part{i}.txt").read_text(encoding="ascii").strip() for i in (1,2,3,4)]
payload = "".join(parts)
if len(payload) != 30938:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest() != "1598ac4eb344a3a852e13813673d0fe6bc794ff485478d988ee814d71adb34d3":
    raise RuntimeError("compressed payload hash mismatch")
patch = zlib.decompress(base64.b85decode(payload))
if len(patch) != 105196:
    raise RuntimeError(f"unexpected patch length: {len(patch)}")
if hashlib.sha256(patch).hexdigest() != "a9e773a35919bfdd8ec42c1df850be37163d9095ceca5bfec624f9859a7fb13d":
    raise RuntimeError("patch hash mismatch")
patch_file = ROOT / "MCB_UI_ONLY_DELIVERY.patch"
patch_file.write_bytes(patch)
subprocess.run(["git","apply","--check",str(patch_file)], cwd=ROOT, check=True)
subprocess.run(["git","apply",str(patch_file)], cwd=ROOT, check=True)
print("MCB_UI_ONLY_DELIVERY=APPLIED")
print("patch_sha256=a9e773a35919bfdd8ec42c1df850be37163d9095ceca5bfec624f9859a7fb13d")
