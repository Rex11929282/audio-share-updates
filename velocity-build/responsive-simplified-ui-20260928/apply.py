from pathlib import Path
import base64
import subprocess
import sys
import zlib

HERE = Path(__file__).resolve().parent
ROOT = Path(sys.argv[1]).resolve()
REPO = ROOT.parents[1]
PREFIX = Path("cs2/MCB-CS2")

names = ["part1.txt","part2.txt","part3.txt","part4_1.txt","part4_2.txt","part4_3.txt","part4_4.txt"]
payload = "".join((HERE / name).read_text(encoding="ascii").strip() for name in names)
patch = zlib.decompress(base64.b64decode(payload))
patch_file = ROOT / "MCB_RESPONSIVE_SIMPLIFIED_UI.patch"
patch_file.write_bytes(patch)

args = ["git","-C",str(REPO),"apply","--directory="+PREFIX.as_posix(),str(patch_file)]
subprocess.run(args[:4] + ["--check"] + args[4:], check=True)
subprocess.run(args, check=True)
print("responsive simplified UI patch applied")
