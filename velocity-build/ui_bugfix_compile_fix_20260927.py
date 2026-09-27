from pathlib import Path
import sys, json, hashlib

root = Path(sys.argv[1])
p = root / 'project/core/rendering/impl/menu/menu.skins.cpp'
s = p.read_text(encoding='utf-8-sig')

weapon_marker = '\t\tstatic inline void draw_weapon_card('
next_marker = '\t\tstatic inline void draw_agent_tile('
ws = s.find(weapon_marker)
we = s.find(next_marker, ws)
if ws < 0 or we < 0:
    raise RuntimeError('weapon-card function markers missing')

bad = 'const auto target_h = std::max( 1.0f, image_button.h - 8.0f );'
normal = 'const auto target_h = image_h - 12.0f;'

prefix = s[:ws]
body = s[ws:we]
suffix = s[we:]

if prefix.count(bad) != 1:
    raise RuntimeError(f'expected one out-of-scope image_button use, got {prefix.count(bad)}')
prefix = prefix.replace(bad, normal, 1)

if body.count(normal) < 1:
    raise RuntimeError('weapon image target height anchor missing')
body = body.replace(normal, bad, 1)

out = prefix + body + suffix
p.write_text(out, encoding='utf-8', newline='\n')

final = p.read_text(encoding='utf-8')
first_ref = final.find('image_button')
if first_ref < ws:
    raise RuntimeError('image_button still referenced outside weapon-card scope')

report = {
    'fixed_out_of_scope_image_button': True,
    'weapon_photo_uses_button_bounds': True,
    'sha256': hashlib.sha256(p.read_bytes()).hexdigest(),
}
(root / 'MCB_UI_BUGFIX_COMPILE_FIX.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
