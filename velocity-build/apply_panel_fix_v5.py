from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
menu = root / "cs2" / "MCB-CS2" / "project" / "core" / "rendering" / "impl" / "menu" / "menu.core.cpp"
t = menu.read_text(encoding="utf-8")
old = 'xdraw::color{ 19, 191, 245, active ? 255 : static_cast<std::uint8_t>( 180 + hover_t * 60.0f ) }'
new = 'xdraw::color{ 19, 191, 245, static_cast<std::uint8_t>( active ? 255 : std::clamp( 180.0f + hover_t * 60.0f, 0.0f, 255.0f ) ) }'
if old not in t:
    raise SystemExit('[panel-fix-v5] alpha expression not found')
t = t.replace(old, new, 1)
menu.write_text(t, encoding="utf-8")

# The user's uploaded HTML/CSS/JS is the canonical v5 panel specification.
# Apply the exact 10-page native renderer after the older visual panel patch.
exact = Path(__file__).resolve().parent / "apply_exact_panel_v5.py"
subprocess.check_call([sys.executable, str(exact), str(root)])
print('[panel-fix-v5] explicit alpha narrowing + exact uploaded panel applied')
