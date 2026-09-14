from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
config = root / "cs2" / "MCB-CS2" / "project" / "external" / "config.hpp"


def fail(msg: str):
    raise SystemExit("[registry-v5] " + msg)


t = config.read_text(encoding="utf-8")

if "#include <mutex>" not in t:
    t = t.replace("#include <windows.h>\n", "#include <windows.h>\n#include <mutex>\n", 1)

if "k_legacy_root_key" not in t:
    pattern = r'(?m)^(\s*)inline constexpr wchar_t k_root_key\s*\[\s*\]\s*\{\s*L"Software\\\\velokitty\\\\configs"\s*\};\s*$'
    replacement = (
        r'\1inline constexpr wchar_t k_root_key[ ]{ L"Software\\\\MCB\\\\configs" };\n'
        r'\1inline constexpr wchar_t k_legacy_root_key[ ]{ L"Software\\\\velokitty\\\\configs" };'
    )
    t, count = re.subn(pattern, replacement, t, count=1)
    if count != 1:
        fail("legacy root key anchor not found")
else:
    t = t.replace('L"Software\\\\velokitty\\\\configs"', 'L"Software\\\\MCB\\\\configs"', 1)
    # Restore the legacy key if the simple replacement accidentally touched it.
    legacy_pattern = r'(k_legacy_root_key\s*\[\s*\]\s*\{\s*)L"Software\\\\MCB\\\\configs"'
    t = re.sub(legacy_pattern, r'\1L"Software\\\\velokitty\\\\configs"', t, count=1)

if "migrate_legacy_once" not in t:
    marker_match = re.search(
        r'(?m)^\s*inline constexpr wchar_t k_legacy_root_key\s*\[\s*\]\s*\{\s*L"Software\\\\velokitty\\\\configs"\s*\};\s*$',
        t,
    )
    if not marker_match:
        fail("legacy root marker unavailable after rename")

    migrate = r'''

		inline void migrate_legacy_once( )
		{
			static std::once_flag once{};
			std::call_once( once, [ ]
			{
				HKEY legacy{};
				if ( RegOpenKeyExW( HKEY_CURRENT_USER, k_legacy_root_key, 0, KEY_READ, &legacy ) != ERROR_SUCCESS )
				{
					return;
				}

				HKEY current{};
				DWORD disposition{};
				if ( RegCreateKeyExW(
					HKEY_CURRENT_USER,
					k_root_key,
					0,
					nullptr,
					0,
					KEY_READ | KEY_WRITE,
					nullptr,
					&current,
					&disposition ) != ERROR_SUCCESS )
				{
					RegCloseKey( legacy );
					return;
				}

				DWORD index{};
				for ( ;; )
				{
					wchar_t name[ 256 ]{};
					DWORD name_len = static_cast<DWORD>( std::size( name ) );
					DWORD type{};
					DWORD size{};
					const auto result = RegEnumValueW(
						legacy, index++, name, &name_len, nullptr, &type, nullptr, &size );

					if ( result == ERROR_NO_MORE_ITEMS )
					{
						break;
					}
					if ( result != ERROR_SUCCESS || type != REG_BINARY || size == 0 || size > 8u * 1024u * 1024u )
					{
						continue;
					}

					DWORD existing_type{};
					DWORD existing_size{};
					if ( RegQueryValueExW(
						current, name, nullptr, &existing_type, nullptr, &existing_size ) == ERROR_SUCCESS )
					{
						continue;
					}

					std::vector<std::uint8_t> data( size );
					DWORD read_size = size;
					if ( RegQueryValueExW(
						legacy, name, nullptr, &type, data.data( ), &read_size ) == ERROR_SUCCESS &&
						read_size == size )
					{
						( void )RegSetValueExW(
							current, name, 0, REG_BINARY, data.data( ), size );
					}
				}

				RegCloseKey( current );
				RegCloseKey( legacy );
			} );
		}
'''
    insert_at = marker_match.end()
    t = t[:insert_at] + migrate + t[insert_at:]

# Persistent registry operations call migration before accessing the new key.
patterns = [
    r'(\t\tinline bool save\( std::wstring_view name \)\n\t\t\{\n)(?!\t\t\tmigrate_legacy_once)',
    r'(\t\tinline bool load\( std::wstring_view name \)\n\t\t\{\n)(?!\t\t\tmigrate_legacy_once)',
    r'(\t\tinline bool remove\( std::wstring_view name \)\n\t\t\{\n)(?!\t\t\tmigrate_legacy_once)',
    r'(\t\tinline std::vector<std::wstring> list\( \)\n\t\t\{\n)(?!\t\t\tmigrate_legacy_once)',
]
for pattern in patterns:
    t, _ = re.subn(pattern, r'\1\t\t\tmigrate_legacy_once( );\n', t, count=1)

if 'L"Software\\\\MCB\\\\configs"' not in t:
    fail("MCB root key missing after patch")
if 'L"Software\\\\velokitty\\\\configs"' not in t:
    fail("legacy root key missing after patch")
if "migrate_legacy_once" not in t:
    fail("migration helper missing")

config.write_text(t, encoding="utf-8")
print("[registry-v5] MCB registry root + non-destructive legacy migration applied")
