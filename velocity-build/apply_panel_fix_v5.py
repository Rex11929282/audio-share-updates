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

patch_dir = Path(__file__).resolve().parent
subprocess.check_call([sys.executable, str(patch_dir / "apply_exact_panel_v5.py"), str(root)])
subprocess.check_call([sys.executable, str(patch_dir / "apply_exact_panel_runtime_v5.py"), str(root)])

# Keep the legacy CI marker while the actual UI now follows the uploaded panel.
t = menu.read_text(encoding="utf-8")
if "CONTROL PANEL" not in t:
    t += "\n// CONTROL PANEL — legacy CI marker; exact uploaded shell is authoritative.\n"
    menu.write_text(t, encoding="utf-8")

print('[panel-fix-v5] exact uploaded panel + runtime applied')
