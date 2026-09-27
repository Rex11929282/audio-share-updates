from pathlib import Path
import sys, json, hashlib

root = Path(sys.argv[1])
p = root / "project/core/rendering/impl/menu/menu.exact.cpp"
s = p.read_text(encoding="utf-8-sig")
include = "#include <core/settings.hpp>\n"
if include not in s:
    anchor = "#include <pch/pch.hpp>\n"
    if anchor not in s:
        raise RuntimeError("menu.exact.cpp PCH include anchor missing")
    s = s.replace(anchor, anchor + include, 1)
    p.write_text(s, encoding="utf-8", newline="\n")

report = {
    "menu_exact_settings_include": True,
    "sha256": hashlib.sha256(p.read_bytes()).hexdigest()
}
(root / "MCB_WEAPON_UI_COMPILE_FIX.json").write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
)
print(json.dumps(report, ensure_ascii=False))
