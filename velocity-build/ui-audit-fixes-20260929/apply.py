from pathlib import Path
import base64, hashlib, json, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()

payload=(HERE/"part1.txt").read_text(encoding="ascii").strip()+(HERE/"part2.txt").read_text(encoding="ascii").strip()
if len(payload)!=10788:
    raise RuntimeError(f"unexpected UI audit payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="8a6385e80adcdd557ad7d9e86e87827298e2e54aa1a7d9f193522503cdc5e733":
    raise RuntimeError("UI audit payload hash mismatch")

source=zlib.decompress(base64.b64decode(payload))
if len(source)!=35693:
    raise RuntimeError(f"unexpected UI audit source length: {len(source)}")
if hashlib.sha256(source).hexdigest()!="ee80c83a80dfc082ff2ed98aad88b1269b08b089b309e9d47538fa3cf3f23c7a":
    raise RuntimeError("UI audit source hash mismatch")

ns={"__name__":"__main__"}
exec(compile(source,"ui-audit-fixes-20260929","exec"),ns,ns)
checks=ns.get("checks",{})
report={
  "name":"MCB UI audit fixes 2026-09-29",
  "checks":checks,
  "payload_sha256":hashlib.sha256(payload.encode("ascii")).hexdigest(),
  "source_sha256":hashlib.sha256(source).hexdigest(),
  "runtime":"NOT_TESTED"
}
(ROOT/"MCB_UI_AUDIT_FIXES_20260929.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(report,ensure_ascii=False,indent=2))
