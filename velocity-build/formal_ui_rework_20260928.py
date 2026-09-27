from pathlib import Path
import base64,zlib,hashlib
HERE=Path(__file__).resolve().parent
parts=[(HERE/'formal-ui-20260928'/f'part{i}.txt').read_text(encoding='ascii').strip() for i in (1,2,3)]
assert tuple(map(len,parts))==(6000,6000,4880), 'formal UI payload part lengths differ'
raw=zlib.decompress(base64.b85decode(''.join(parts)))
assert len(raw)==45814 and hashlib.sha256(raw).hexdigest()=='ca810f9329119798ddd2afbf3eb68e7649859000a77b913ae42eea9931b7378f', 'formal UI payload hash mismatch'
exec(compile(raw.decode('utf-8'),'formal_ui_rework_20260928_payload.py','exec'))
