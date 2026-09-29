from pathlib import Path
import base64, hashlib, json, sys, zlib

HERE=Path(__file__).resolve().parent
ROOT=Path(sys.argv[1]).resolve()

payload=(HERE/"part1.txt").read_text(encoding="ascii").strip()+(HERE/"part2.txt").read_text(encoding="ascii").strip()
if len(payload)!=10716:
    raise RuntimeError(f"unexpected UI audit payload length: {len(payload)}")
if hashlib.sha256(payload.encode("ascii")).hexdigest()!="0b863dc3ea87cbaea87e3139c2f73e1d879adfb152488d1f0812649f361159bd":
    raise RuntimeError("UI audit payload hash mismatch")

source=zlib.decompress(base64.b64decode(payload))
if len(source)!=35504:
    raise RuntimeError(f"unexpected UI audit source length: {len(source)}")
if hashlib.sha256(source).hexdigest()!="5914ea39ecfa85093ad55ae4caaad02bf5f45efd532ffa1af1f2ab8f62703dd6":
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
