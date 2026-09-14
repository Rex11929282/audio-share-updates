from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
config = root / "cs2" / "MCB-CS2" / "project" / "external" / "config.hpp"


def fail(msg: str):
    raise SystemExit("[registry-v5] " + msg)


t = config.read_text(encoding="utf-8")

if "#include <mutex>" not in t:
    t = t.replace("#include <windows.h>\n", "#include <windows.h>\n#include <mutex>\n", 1)

root_line = '\t\tinline constexpr wchar_t k_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };\n'
new_root_block = (
    '\t\tinline constexpr wchar_t k_root_key[ ]{ L"Software\\\\MCB\\\\configs" };\n'
    '\t\tinline constexpr wchar_t k_legacy_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };\n'
)

if "k_legacy_root_key" not in t:
    if root_line not in t:
        fail("legacy root key anchor not found")
    t = t.replace(root_line, new_root_block, 1)

legacy_line = '\t\tinline constexpr wchar_t k_legacy_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };\n'

if "migrate_legacy_once" not in t:
    if legacy_line not in t:
        fail("legacy root marker unavailable after rename")

    migrate = '''
\t\tinline void migrate_legacy_once( )
\t\t{
\t\t\tstatic std::once_flag once{};
\t\t\tstd::call_once( once, [ ]
\t\t\t{
\t\t\t\tHKEY legacy{};
\t\t\t\tif ( RegOpenKeyExW( HKEY_CURRENT_USER, k_legacy_root_key, 0, KEY_READ, &legacy ) != ERROR_SUCCESS )
\t\t\t\t{
\t\t\t\t\treturn;
\t\t\t\t}

\t\t\t\tHKEY current{};
\t\t\t\tDWORD disposition{};
\t\t\t\tif ( RegCreateKeyExW(
\t\t\t\t\tHKEY_CURRENT_USER,
\t\t\t\t\tk_root_key,
\t\t\t\t\t0,
\t\t\t\t\tnullptr,
\t\t\t\t\t0,
\t\t\t\t\tKEY_READ | KEY_WRITE,
\t\t\t\t\tnullptr,
\t\t\t\t\t&current,
\t\t\t\t\t&disposition ) != ERROR_SUCCESS )
\t\t\t\t{
\t\t\t\t\tRegCloseKey( legacy );
\t\t\t\t\treturn;
\t\t\t\t}

\t\t\t\tDWORD index{};
\t\t\t\tfor ( ;; )
\t\t\t\t{
\t\t\t\t\twchar_t name[ 256 ]{};
\t\t\t\t\tDWORD name_len{ 256 };
\t\t\t\t\tDWORD type{};
\t\t\t\t\tDWORD size{};
\t\t\t\t\tconst auto result = RegEnumValueW(
\t\t\t\t\t\tlegacy, index++, name, &name_len, nullptr, &type, nullptr, &size );

\t\t\t\t\tif ( result == ERROR_NO_MORE_ITEMS )
\t\t\t\t\t{
\t\t\t\t\t\tbreak;
\t\t\t\t\t}
\t\t\t\t\tif ( result != ERROR_SUCCESS || type != REG_BINARY || size == 0 || size > 8u * 1024u * 1024u )
\t\t\t\t\t{
\t\t\t\t\t\tcontinue;
\t\t\t\t\t}

\t\t\t\t\tDWORD existing_type{};
\t\t\t\t\tDWORD existing_size{};
\t\t\t\t\tif ( RegQueryValueExW(
\t\t\t\t\t\tcurrent, name, nullptr, &existing_type, nullptr, &existing_size ) == ERROR_SUCCESS )
\t\t\t\t\t{
\t\t\t\t\t\tcontinue;
\t\t\t\t\t}

\t\t\t\t\tstd::vector<std::uint8_t> data( size );
\t\t\t\t\tDWORD read_size = size;
\t\t\t\t\tif ( RegQueryValueExW(
\t\t\t\t\t\tlegacy, name, nullptr, &type, data.data( ), &read_size ) == ERROR_SUCCESS &&
\t\t\t\t\t\tread_size == size )
\t\t\t\t\t{
\t\t\t\t\t\t( void )RegSetValueExW(
\t\t\t\t\t\t\tcurrent, name, 0, REG_BINARY, data.data( ), size );
\t\t\t\t\t}
\t\t\t\t}

\t\t\t\tRegCloseKey( current );
\t\t\t\tRegCloseKey( legacy );
\t\t\t} );
\t\t}
'''
    t = t.replace(legacy_line, legacy_line + migrate, 1)

for signature in (
    '\t\tinline bool save( std::wstring_view name )\n\t\t{\n',
    '\t\tinline bool load( std::wstring_view name )\n\t\t{\n',
    '\t\tinline bool remove( std::wstring_view name )\n\t\t{\n',
    '\t\tinline std::vector<std::wstring> list( )\n\t\t{\n',
):
    patched = signature + '\t\t\tmigrate_legacy_once( );\n'
    if patched in t:
        continue
    if signature not in t:
        fail("registry operation anchor missing: " + signature.splitlines()[0].strip())
    t = t.replace(signature, patched, 1)

if 'L"Software\\\\MCB\\\\configs"' not in t:
    fail("MCB root key missing after patch")
if 'L"Software\\\\velokitty\\\\configs"' not in t:
    fail("legacy root key missing after patch")
if "inline void migrate_legacy_once( )" not in t:
    fail("migration helper missing")

config.write_text(t, encoding="utf-8")
print("[registry-v5] MCB registry root + non-destructive legacy migration applied")
