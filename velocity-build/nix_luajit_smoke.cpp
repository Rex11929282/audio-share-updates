#define NOMINMAX
#include <windows.h>

#include <array>
#include <cstddef>
#include <filesystem>
#include <iostream>
#include <string>

struct lua_State;

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::wcerr << L"usage: nix_luajit_smoke.exe <absolute lua51.dll>\n";
        return 2;
    }

    const auto dll = std::filesystem::weakly_canonical(argv[1]);
    if (!dll.is_absolute() || _wcsicmp(dll.filename().c_str(), L"lua51.dll") != 0)
    {
        std::wcerr << L"invalid runtime path\n";
        return 3;
    }

    const auto module = LoadLibraryExW(
        dll.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!module)
    {
        std::wcerr << L"LoadLibraryExW failed: " << GetLastError() << L"\n";
        return 4;
    }

    const auto get = [&](const char* name) -> FARPROC
    {
        const auto proc = GetProcAddress(module, name);
        if (!proc) std::cerr << "missing export: " << name << "\n";
        return proc;
    };

    using newstate_t = lua_State* (__cdecl*)();
    using close_t = void (__cdecl*)(lua_State*);
    using openlibs_t = void (__cdecl*)(lua_State*);
    using loadbuffer_t = int (__cdecl*)(lua_State*, const char*, std::size_t, const char*);
    using pcall_t = int (__cdecl*)(lua_State*, int, int, int);
    using type_t = int (__cdecl*)(lua_State*, int);
    using tolstring_t = const char* (__cdecl*)(lua_State*, int, std::size_t*);

    const auto newstate = reinterpret_cast<newstate_t>(get("luaL_newstate"));
    const auto close = reinterpret_cast<close_t>(get("lua_close"));
    const auto openlibs = reinterpret_cast<openlibs_t>(get("luaL_openlibs"));
    const auto loadbuffer = reinterpret_cast<loadbuffer_t>(get("luaL_loadbuffer"));
    const auto pcall = reinterpret_cast<pcall_t>(get("lua_pcall"));
    const auto type = reinterpret_cast<type_t>(get("lua_type"));
    const auto tolstring = reinterpret_cast<tolstring_t>(get("lua_tolstring"));

    if (!newstate || !close || !openlibs || !loadbuffer || !pcall || !type || !tolstring)
    {
        FreeLibrary(module);
        return 5;
    }

    const std::string probe = R"LUA(
assert(_VERSION == "Lua 5.1")
assert(jit.version == "LuaJIT 2.1.1736781742", jit.version)
assert(jit.os == "Windows" and jit.arch == "x64")

local ffi = require("ffi")
assert(ffi.sizeof("void *") == 8)
assert(ffi.abi("64bit") and ffi.abi("le"))

ffi.cdef[[
typedef struct { double x; double y; double z; } mcb_smoke_vec3;
]]
local T = ffi.typeof("mcb_smoke_vec3")
local v = T(1, 2, 3)
assert(type(v) == "cdata")
assert(ffi.istype(T, v))
assert(v.x == 1 and v.y == 2 and v.z == 3)

local backing = ffi.new("int[2]", {17, 29})
local p = ffi.cast("int*", backing)
assert(p[0] == 17 and p[1] == 29)

local cb = ffi.cast("int(*)(int)", function(x) return x + 1 end)
assert(cb(41) == 42)
cb:free()

print("MCB_NIX_LUAJIT_SMOKE=PASS")
)LUA";

    auto* L = newstate();
    if (!L)
    {
        FreeLibrary(module);
        return 6;
    }

    openlibs(L);
    int rc = loadbuffer(L, probe.data(), probe.size(), "=MCB_NIX_SMOKE");
    if (rc == 0) rc = pcall(L, 0, 0, 0);

    if (rc != 0)
    {
        constexpr int LUA_TSTRING = 4;
        if (type(L, -1) == LUA_TSTRING)
        {
            std::size_t n{};
            if (const auto* msg = tolstring(L, -1, &n))
                std::cerr << std::string(msg, n) << "\n";
        }
    }

    close(L);
    FreeLibrary(module);
    return rc == 0 ? 0 : 7;
}
