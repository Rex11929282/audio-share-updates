from pathlib import Path
import json
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "velocity-cs2"


def fail(msg: str):
    raise SystemExit("[brand-v4] " + msg)


def replace_required(path: Path, old: str, new: str, label: str):
    text = path.read_text(encoding="utf-8")
    if old not in text:
        fail(f"{label}: marker not found in {path}")
    path.write_text(text.replace(old, new), encoding="utf-8")


def replace_optional(path: Path, old: str, new: str):
    text = path.read_text(encoding="utf-8")
    if old in text:
        path.write_text(text.replace(old, new), encoding="utf-8")


# ---------------------------------------------------------------------------
# 1) Runtime-visible branding.
# ---------------------------------------------------------------------------
widgets = v / "project/core/rendering/impl/widgets.cpp"
replace_required(widgets, '"velocity.cat"', '"MCB"', "watermark brand")
replace_optional(widgets, '"developer"', '"MCB"')

config_hpp = v / "project/external/config.hpp"
replace_optional(config_hpp, '"velocity.cat core1!!11"', '"MCB core"')

diag = v / "project/utilities/diag.hpp"
for old, new in (
    ('L"velocity_init.log"', 'L"MCB_init.log"'),
    ('L"velocity_init.previous.log"', 'L"MCB_init.previous.log"'),
    ('L"velocity_crash.dmp"', 'L"MCB_crash.dmp"'),
    ('L"velocity_crash.previous.dmp"', 'L"MCB_crash.previous.dmp"'),
):
    replace_required(diag, old, new, old)

entry = v / "project/entry.cpp"
replace_optional(entry, "first-chance fault in velocity DLL", "first-chance fault in MCB DLL")
replace_optional(entry, "velocity DLL", "MCB DLL")

functions = v / "project/utilities/addresses/impl/functions.cpp"
replace_optional(functions, '"velocity_dummy_window"', '"MCB_dummy_window"')

scene = v / "project/core/features/world/impl/scene.cpp"
replace_optional(scene, '"velocity_skybox_{:08x}"', '"MCB_skybox_{:08x}"')


# ---------------------------------------------------------------------------
# 2) Lua public API: MCB is the canonical global name.
# ---------------------------------------------------------------------------
lua_cpp = v / "project/core/scripting/lua_manager.cpp"
text = lua_cpp.read_text(encoding="utf-8")
text = text.replace('"velocity.name"', '"mcb.name"')
text = text.replace('lua_setglobal( L, "velocity" );', 'lua_setglobal( L, "mcb" );')
text = text.replace('"[Lua:', '"[MCB Lua:')
text = text.replace('"[Lua]', '"[MCB Lua]')
if 'lua_setglobal( L, "mcb" );' not in text:
    fail("MCB Lua global was not applied")
if 'lua_setglobal( L, "velocity" );' in text:
    fail("legacy Velocity Lua global still exposed")
lua_cpp.write_text(text, encoding="utf-8")

for sample_name in ("hello.lua", "snow_hud.lua"):
    sample = v / "lua_examples" / sample_name
    if sample.exists():
        s = sample.read_text(encoding="utf-8").replace("velocity.", "mcb.")
        s = s.replace("Velocity", "MCB")
        sample.write_text(s, encoding="utf-8")


# ---------------------------------------------------------------------------
# 3) Build/product metadata.
# ---------------------------------------------------------------------------
project = v / "velocity-cs2.vcxproj"
p = project.read_text(encoding="utf-8")
p = p.replace('<RootNamespace>velocitycs2</RootNamespace>', '<RootNamespace>mcbcs2</RootNamespace>')
p = p.replace('<TargetName>velocity-cs2-dev</TargetName>', '<TargetName>MCB-CS2-dev</TargetName>')
p = p.replace('<TargetName>cs2</TargetName>', '<TargetName>MCB-CS2</TargetName>')
p = p.replace('VelocityImportVcpkg', 'MCBImportVcpkg')
p = p.replace('VELOCITYCS2_EXPORTS', 'MCBCS2_EXPORTS')

# Add a Windows VERSIONINFO resource so Explorer reports MCB too.
rc_include = '    <ResourceCompile Include="project\\mcb_version.rc" />\n'
if 'project\\mcb_version.rc' not in p:
    marker = '  <ItemGroup>\n    <ClCompile Include="project\\core\\features\\changer\\impl\\agents.cpp" />'
    if marker not in p:
        fail("vcxproj compile ItemGroup marker not found")
    p = p.replace(marker, '  <ItemGroup>\n' + rc_include + '    <ClCompile Include="project\\core\\features\\changer\\impl\\agents.cpp" />', 1)
project.write_text(p, encoding="utf-8")

version_rc = v / "project/mcb_version.rc"
version_rc.write_text(r'''#include <windows.h>

VS_VERSION_INFO VERSIONINFO
 FILEVERSION 1,0,0,0
 PRODUCTVERSION 1,0,0,0
 FILEFLAGSMASK 0x3fL
#ifdef _DEBUG
 FILEFLAGS 0x1L
#else
 FILEFLAGS 0x0L
#endif
 FILEOS 0x40004L
 FILETYPE 0x2L
 FILESUBTYPE 0x0L
BEGIN
    BLOCK "StringFileInfo"
    BEGIN
        BLOCK "040904B0"
        BEGIN
            VALUE "CompanyName", "MCB\0"
            VALUE "FileDescription", "MCB\0"
            VALUE "FileVersion", "1.0.0\0"
            VALUE "InternalName", "MCB-CS2\0"
            VALUE "OriginalFilename", "MCB-CS2-dev.dll\0"
            VALUE "ProductName", "MCB\0"
            VALUE "ProductVersion", "1.0.0\0"
        END
    END
    BLOCK "VarFileInfo"
    BEGIN
        VALUE "Translation", 0x409, 1200
    END
END
''', encoding="utf-8")

manifest = v / "vcpkg.json"
d = json.loads(manifest.read_text(encoding="utf-8"))
d["name"] = "mcb-cs2"
manifest.write_text(json.dumps(d, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


# ---------------------------------------------------------------------------
# 4) Final source assertions for known upstream brand markers.
# ---------------------------------------------------------------------------
known_brand_markers = {
    widgets: ["velocity.cat"],
    diag: ["velocity_init.log", "velocity_crash.dmp"],
    functions: ["velocity_dummy_window"],
    scene: ["velocity_skybox_"],
    lua_cpp: ['lua_setglobal( L, "velocity" );', '"velocity.name"'],
}
for path, markers in known_brand_markers.items():
    text = path.read_text(encoding="utf-8")
    for marker in markers:
        if marker in text:
            fail(f"old brand marker remains: {marker} in {path}")


# ---------------------------------------------------------------------------
# 5) Rename solution/project/folder so generated PDB/debug paths are MCB too.
# ---------------------------------------------------------------------------
solution = root / "cs2" / "velocity-cs2.slnx"
if solution.exists():
    s = solution.read_text(encoding="utf-8")
    s = s.replace('velocity-cs2/velocity-cs2.vcxproj', 'MCB-CS2/MCB-CS2.vcxproj')
    solution.write_text(s, encoding="utf-8")
    solution.replace(root / "cs2" / "MCB-CS2.slnx")

renamed_project = v / "MCB-CS2.vcxproj"
project.replace(renamed_project)
renamed_dir = root / "cs2" / "MCB-CS2"
if renamed_dir.exists():
    fail("target MCB project directory already exists")
v.replace(renamed_dir)

print("[brand-v4] all user-visible/product branding changed to MCB")
