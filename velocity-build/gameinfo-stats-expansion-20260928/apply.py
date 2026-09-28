from pathlib import Path
import base64, hashlib, zlib

HERE=Path(__file__).resolve().parent
payload=(HERE/"part1.txt").read_text(encoding="ascii").strip()+(HERE/"part2.txt").read_text(encoding="ascii").strip()
if len(payload)!=6360:
    raise RuntimeError(f"unexpected payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="97cda434acc13f79bd5f84ef089984d57570d944d5201b97282918a6c41eb68a":
    raise RuntimeError("payload hash mismatch")
source=zlib.decompress(base64.b64decode(payload))
if len(source)!=21698:
    raise RuntimeError(f"unexpected source length: {len(source)}")
if hashlib.sha256(source).hexdigest()!="9a8508c8bf9c40e1529367324d8d2c3c7f597a185991df2a5e0a14747ebf5077":
    raise RuntimeError("source hash mismatch")
exec(compile(source,"gameinfo-stats-expansion","exec"))
