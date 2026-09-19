#include <pch/pch.hpp>

#include "nix_runtime.hpp"
#include "nix_luajit_manifest.hpp"
#include "nix_luajit_api.hpp"
#include "nix_entity_bridge.hpp"
#include "nix_engine_bridge.hpp"
#include "nix_event_bridge.hpp"
#include "nix_render_bridge.hpp"
#include "nix_render_bootstrap.hpp"
#include "nix_cvar_bridge.hpp"
#include "nix_value_bootstrap.hpp"

#include <algorithm>
#include <core/systems/systems.hpp>
#include <utilities/memory/memory.hpp>
#include <array>
#include <bcrypt.h>
#include <chrono>
#include <commdlg.h>
#include <cmath>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <mutex>
#include <string_view>
#include <unordered_map>
#include <utilities/logging/logging.hpp>
#include <windows.h>

#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "Comdlg32.lib")

namespace
{
    using scripting::nix_native::luajit_api;
    using scripting::nix_native::lua_globals_index;
    using scripting::nix_native::lua_tfunction;
    using scripting::nix_native::lua_tstring;
    constexpr std::size_t max_nix_script = 8u * 1024u * 1024u;
    constexpr std::string_view blocked_abusive_original_sha256 =
        "cb1088936c2871fa8d8332611fc7c7f409fcbbb4a69c08d77f3ca86f5e095794";

    std::string lowercase(std::string value)
    {
        std::transform(value.begin(), value.end(), value.begin(),
            [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return value;
    }

    bool is_hex_sha256(std::string_view value)
    {
        if (value.size() != 64) return false;
        for (const char c : value)
        {
            if (!std::isxdigit(static_cast<unsigned char>(c))) return false;
        }
        return true;
    }

    std::string sha256_file(const std::filesystem::path& path)
    {
        BCRYPT_ALG_HANDLE algorithm{};
        BCRYPT_HASH_HANDLE hash{};
        DWORD object_size{}, hash_size{}, cb{};
        std::vector<std::uint8_t> object;
        std::vector<std::uint8_t> digest;
        std::string result;

        if (BCryptOpenAlgorithmProvider(
                &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
            return {};

        const auto cleanup = [&]
        {
            if (hash) BCryptDestroyHash(hash);
            if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
        };

        if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&object_size), sizeof(object_size), &cb, 0) < 0 ||
            BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH,
                reinterpret_cast<PUCHAR>(&hash_size), sizeof(hash_size), &cb, 0) < 0)
        {
            cleanup();
            return {};
        }

        object.resize(object_size);
        digest.resize(hash_size);

        if (BCryptCreateHash(algorithm, &hash, object.data(),
                static_cast<ULONG>(object.size()), nullptr, 0, 0) < 0)
        {
            cleanup();
            return {};
        }

        std::ifstream input(path, std::ios::binary);
        if (!input)
        {
            cleanup();
            return {};
        }

        std::array<char, 64 * 1024> buffer{};
        while (input)
        {
            input.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
            const auto count = input.gcount();
            if (count <= 0) break;
            if (BCryptHashData(hash,
                    reinterpret_cast<PUCHAR>(buffer.data()),
                    static_cast<ULONG>(count), 0) < 0)
            {
                cleanup();
                return {};
            }
        }

        if (BCryptFinishHash(
                hash, digest.data(), static_cast<ULONG>(digest.size()), 0) < 0)
        {
            cleanup();
            return {};
        }

        cleanup();

        constexpr char hex[] = "0123456789abcdef";
        result.reserve(digest.size() * 2);
        for (const auto byte : digest)
        {
            result.push_back(hex[(byte >> 4) & 0x0f]);
            result.push_back(hex[byte & 0x0f]);
        }
        return result;
    }

    std::string lua_error(luajit_api& api, lua_State* L, std::string fallback)
    {
        if (!L) return fallback;
        if (api.lua_type(L, -1) == lua_tstring)
        {
            std::size_t size{};
            if (const auto* text = api.lua_tolstring(L, -1, &size))
                return std::string(text, size);
        }
        return fallback;
    }

    bool run_chunk(
        luajit_api& api,
        lua_State* L,
        std::string_view bytes,
        const std::string& name,
        std::string& error)
    {
        const auto top = api.lua_gettop(L);
        auto result = api.luaL_loadbuffer(L, bytes.data(), bytes.size(), name.c_str());
        if (result == 0)
            result = api.lua_pcall(L, 0, 0, 0);

        if (result != 0)
        {
            error = lua_error(api, L, "LuaJIT chunk failed with code " + std::to_string(result));
            api.lua_settop(L, top);
            return false;
        }

        api.lua_settop(L, top);
        return true;
    }

    constexpr std::string_view runtime_probe = R"MCB(
assert(_VERSION == "Lua 5.1", "wrong Lua language version")
assert(type(jit) == "table", "jit table missing")
assert(jit.version == "LuaJIT 2.1.1736781742", "wrong LuaJIT revision: " .. tostring(jit.version))
assert(jit.os == "Windows" and jit.arch == "x64", "wrong LuaJIT target")
local ffi = require("ffi")
assert(ffi.sizeof("void *") == 8, "wrong pointer size")
assert(ffi.abi("64bit") and ffi.abi("le"), "wrong ABI")

-- Private conformance type. This does not pretend to be a documented Nixware type.
ffi.cdef[[
typedef struct { double x; double y; double z; } mcb_nix_probe_vec3;
]]
local T = ffi.typeof("mcb_nix_probe_vec3")
local v = T(1, 2, 3)
assert(type(v) == "cdata" and ffi.istype(T, v))
assert(v.x == 1 and v.y == 2 and v.z == 3)

local backing = ffi.new("int[2]", {17, 29})
local pointer = ffi.cast("int*", backing)
assert(pointer[0] == 17 and pointer[1] == 29)

local callback = ffi.cast("int(*)(int)", function(x) return x + 1 end)
assert(callback(41) == 42, "FFI callback failed")
callback:free()
)MCB";

    constexpr std::string_view native_entity_probe = R"MCB(
local ffi = require("ffi")
local pawn = entitylist.get_local_player_pawn()
assert(pawn ~= nil, "local pawn unavailable")

local offset = engine.get_netvar_offset(
    "client.dll", "C_BaseEntity", "m_iHealth")
assert(type(offset) == "number", "m_iHealth offset unavailable")

local address = pawn[offset]
assert(address ~= nil, "pawn[offset] did not return an address")

local health = ffi.cast("int*", address)[0]
assert(type(health) == "number", "FFI health read did not return a number")
assert(health >= 0 and health <= 1000, "implausible local health")

local class_name = pawn:get_class_name()
assert(type(class_name) == "string" and #class_name > 0, "entity class unavailable")

local sensitivity = cvars.sensitivity
assert(sensitivity ~= nil, "cvars.sensitivity unavailable")
local sens_value = sensitivity:get_float()
assert(type(sens_value) == "number" and sens_value > 0, "invalid sensitivity cvar")

local origin = pawn:get_abs_origin()
assert(origin ~= nil and type(origin) == "cdata", "absolute origin is not cdata")

return true
)MCB";


    std::string bootstrap_for(const std::string& filename)
    {
        std::string quoted;
        quoted.reserve(filename.size() + 8);
        for (const unsigned char c : filename)
        {
            if (c == '\\' || c == '"')
            {
                quoted.push_back('\\');
                quoted.push_back(static_cast<char>(c));
            }
            else if (c >= 32 && c != 127)
            {
                quoted.push_back(static_cast<char>(c));
            }
        }

        return std::string(R"MCB(
local callbacks = {}
local supported = { paint = true, unload = true, override_view = true, player_death = true, player_hurt = true, bullet_impact = true, round_start = true }

function register_callback(name, fn)
    assert(type(name) == "string", "callback name must be a string")
    assert(type(fn) == "function", "callback must be a function")
    if not supported[name] then
        error("MCB Nix Slice A does not implement callback '" .. name .. "'", 2)
    end
    local list = callbacks[name]
    if not list then
        list = {}
        callbacks[name] = list
    end
    list[#list + 1] = fn
end

function __mcb_nix_dispatch(name)
    local list = callbacks[name]
    if not list then return end
    for i = 1, #list do
        list[i]()
    end
end

function __mcb_nix_dispatch_event(name, event)
    local list = callbacks[name]
    if not list then return end
    for i = 1, #list do
        list[i](event)
    end
end

function __mcb_nix_override_view(fov, fov_viewmodel, ox, oy, oz, pitch, yaw, roll)
    local view = {
        fov = fov,
        fov_viewmodel = fov_viewmodel,
        origin = vec3_t(ox, oy, oz),
        angles = angle_t(pitch, yaw, roll)
    }

    local list = callbacks.override_view
    if list then
        for i = 1, #list do
            list[i](view)
        end
    end

    assert(type(view.fov) == "number", "view.fov must remain numeric")
    assert(type(view.fov_viewmodel) == "number", "view.fov_viewmodel must remain numeric")
    assert(type(view.origin) == "cdata", "view.origin must remain vec3_t cdata")
    assert(type(view.angles) == "cdata", "view.angles must remain angle_t cdata")

    return view.fov, view.fov_viewmodel,
           view.origin.x, view.origin.y, view.origin.z,
           view.angles.pitch, view.angles.yaw, view.angles.roll
end

function get_script_name()
    return ")MCB") + quoted + R"MCB("
end
)MCB";
    }

    struct runtime_image
    {
        HMODULE module{};
        bool owns_module{};
        luajit_api api{};
        std::filesystem::path path{};

        ~runtime_image()
        {
            if (module && owns_module) FreeLibrary(module);
        }

        bool open(const std::filesystem::path& runtime_path, std::string& error)
        {
            const std::string expected = lowercase(MCB_NIX_LUAJIT_SHA256);
            if (!is_hex_sha256(expected))
            {
                error = "LuaJIT manifest SHA-256 is missing or invalid";
                return false;
            }

            std::error_code ec;
            const auto canonical = std::filesystem::weakly_canonical(runtime_path, ec);
            if (ec || !std::filesystem::is_regular_file(canonical, ec))
            {
                error = "runtime/lua51.dll is missing";
                return false;
            }

            if (_wcsicmp(canonical.filename().c_str(), L"lua51.dll") != 0)
            {
                error = "LuaJIT runtime must remain named lua51.dll";
                return false;
            }

            const auto actual_hash = lowercase(sha256_file(canonical));
            if (actual_hash != expected)
            {
                error = "LuaJIT SHA-256 mismatch";
                return false;
            }

            if (auto existing = GetModuleHandleW(L"lua51.dll"))
            {
                std::array<wchar_t, 32768> loaded{};
                const auto len = GetModuleFileNameW(
                    existing, loaded.data(), static_cast<DWORD>(loaded.size()));
                if (!len || len >= loaded.size())
                {
                    error = "cannot identify already-loaded lua51.dll";
                    return false;
                }

                const auto existing_path =
                    std::filesystem::weakly_canonical(std::filesystem::path(loaded.data()), ec);
                if (ec || _wcsicmp(existing_path.c_str(), canonical.c_str()) != 0)
                {
                    error = "a different lua51.dll is already loaded";
                    return false;
                }
            }

            // Always acquire our own loader reference. GetModuleHandleW() does not
            // increment the module refcount, so using it directly would make
            // shutdown capable of unloading a runtime owned by another component.
            module = LoadLibraryExW(
                canonical.c_str(), nullptr,
                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (!module)
            {
                error = "LoadLibraryExW(lua51.dll) failed: " +
                    std::to_string(GetLastError());
                return false;
            }
            owns_module = true;

            path = canonical;
            if (!api.bind(module))
            {
                error = "required LuaJIT C API export is missing";
                return false;
            }

            lua_State* probe = api.luaL_newstate();
            if (!probe)
            {
                error = "luaL_newstate failed";
                return false;
            }

            api.luaL_openlibs(probe);
            bool ok = run_chunk(
                api, probe, runtime_probe, "=MCB_NIX_RUNTIME_PROBE", error);
            if (ok)
            {
                ok = run_chunk(
                    api, probe, scripting::nix_bootstrap::value_types,
                    "=MCB_NIX_VALUE_TYPES", error);
            }
            api.lua_close(probe);
            return ok;
        }
    };

    bool read_file(const std::filesystem::path& path, std::string& output)
    {
        std::error_code ec;
        const auto size = std::filesystem::file_size(path, ec);
        if (ec || size == 0 || size > max_nix_script) return false;

        std::ifstream file(path, std::ios::binary);
        if (!file) return false;
        output.assign(std::istreambuf_iterator<char>(file), {});
        return output.size() == size;
    }

    bool is_nix_script(const std::filesystem::path& path)
    {
        const auto ext = path.extension().wstring();
        return _wcsicmp(ext.c_str(), L".lua") == 0 ||
               _wcsicmp(ext.c_str(), L".luac") == 0;
    }

    bool is_approved_script(const std::filesystem::path& path)
    {
        auto sidecar = path;
        sidecar += L".approved.sha256";

        std::ifstream input(sidecar, std::ios::binary);
        if (!input) return false;

        std::string expected(
            std::istreambuf_iterator<char>(input), {});
        expected.erase(
            std::remove_if(
                expected.begin(), expected.end(),
                [](unsigned char ch)
                {
                    return std::isspace(ch) != 0;
                }),
            expected.end());

        if (!is_hex_sha256(expected))
            return false;

        const auto actual = lowercase(sha256_file(path));
        if (actual == blocked_abusive_original_sha256)
            return false;

        return lowercase(expected) == actual;
    }
}

namespace scripting
{
    struct nix_runtime::impl
    {
        struct script
        {
            std::filesystem::path path{};
            std::filesystem::file_time_type write_time{};
            lua_State* state{};
            bool suspended{};
            std::string last_error{};
        };

        mutable std::mutex mutex{};
        std::filesystem::path enabled_directory{};
        std::unique_ptr<runtime_image> runtime{};
        std::vector<std::unique_ptr<script>> scripts{};
        std::chrono::steady_clock::time_point last_scan{};
        bool initialized{};
        bool native_probe_completed{};
        bool native_probe_failed{};

        void run_native_probe_once()
        {
            if (native_probe_completed || native_probe_failed ||
                !runtime || !systems::g_local.get().pawn)
                return;

            auto& api = runtime->api;
            auto* state = api.luaL_newstate();
            if (!state)
            {
                native_probe_failed = true;
                logging::console::print(
                    "[NixLua] 游戏内实体 FFI 自检失败：luaL_newstate");
                return;
            }

            api.luaL_openlibs(state);
            std::string error;
            bool attached = false;

            bool ok = run_chunk(
                api, state, scripting::nix_bootstrap::value_types,
                "=MCB_NIX_VALUE_TYPES", error);

            if (ok)
            {
                attached = scripting::nix_native::install_entity_api(
                    api, state, error);
                ok = attached;
            }

            bool cvar_attached = false;
            if (ok)
            {
                cvar_attached = scripting::nix_native::install_cvar_api(
                    api, state, error);
                ok = cvar_attached;
            }

            if (ok)
            {
                ok = run_chunk(
                    api, state, native_entity_probe,
                    "=MCB_NIX_NATIVE_ENTITY_PROBE", error);
            }

            if (cvar_attached)
                scripting::nix_native::detach_cvar_api(state);
            if (attached)
                scripting::nix_native::detach_entity_api(state);

            api.lua_close(state);

            if (ok)
            {
                native_probe_completed = true;
                logging::console::print(
                    "[NixLua] 游戏内实体 FFI 自检通过："
                    "local pawn -> schema -> lightuserdata -> ffi.cast");
            }
            else
            {
                native_probe_failed = true;
                logging::console::print(
                    "[NixLua] 游戏内实体 FFI 自检失败：{}", error);
            }
        }

        bool dispatch_override_view(
            script& value,
            float& fov,
            float& fov_viewmodel,
            math::vector3& origin,
            math::vector3& angles)
        {
            if (!value.state || value.suspended) return false;

            auto& api = runtime->api;
            const auto top = api.lua_gettop(value.state);
            api.lua_getfield(
                value.state, lua_globals_index,
                "__mcb_nix_override_view");

            if (api.lua_type(value.state, -1) != lua_tfunction)
            {
                api.lua_settop(value.state, top);
                value.last_error = "internal override_view dispatcher missing";
                value.suspended = true;
                return false;
            }

            api.lua_pushnumber(value.state, fov);
            api.lua_pushnumber(value.state, fov_viewmodel);
            api.lua_pushnumber(value.state, origin.x);
            api.lua_pushnumber(value.state, origin.y);
            api.lua_pushnumber(value.state, origin.z);
            api.lua_pushnumber(value.state, angles.x);
            api.lua_pushnumber(value.state, angles.y);
            api.lua_pushnumber(value.state, angles.z);

            const auto result = api.lua_pcall(value.state, 8, 8, 0);
            if (result != 0)
            {
                value.last_error = lua_error(
                    api, value.state,
                    "callback 'override_view' failed");
                api.lua_settop(value.state, top);
                value.suspended = true;
                logging::console::print(
                    "[NixLua] 已暫停 {}：{}",
                    value.path.filename().string(), value.last_error);
                return false;
            }

            for (int i = -8; i <= -1; ++i)
            {
                if (api.lua_type(value.state, i) !=
                    scripting::nix_native::lua_tnumber)
                {
                    value.last_error =
                        "override_view returned non-numeric native fields";
                    api.lua_settop(value.state, top);
                    value.suspended = true;
                    return false;
                }
            }

            const auto next_fov =
                static_cast<float>(api.lua_tonumber(value.state, -8));
            const auto next_fov_viewmodel =
                static_cast<float>(api.lua_tonumber(value.state, -7));
            const math::vector3 next_origin{
                static_cast<float>(api.lua_tonumber(value.state, -6)),
                static_cast<float>(api.lua_tonumber(value.state, -5)),
                static_cast<float>(api.lua_tonumber(value.state, -4))
            };
            const math::vector3 next_angles{
                static_cast<float>(api.lua_tonumber(value.state, -3)),
                static_cast<float>(api.lua_tonumber(value.state, -2)),
                static_cast<float>(api.lua_tonumber(value.state, -1))
            };

            api.lua_settop(value.state, top);

            const bool finite =
                std::isfinite(next_fov) &&
                std::isfinite(next_fov_viewmodel) &&
                std::isfinite(next_origin.x) &&
                std::isfinite(next_origin.y) &&
                std::isfinite(next_origin.z) &&
                std::isfinite(next_angles.x) &&
                std::isfinite(next_angles.y) &&
                std::isfinite(next_angles.z);

            if (!finite)
            {
                value.last_error =
                    "override_view produced a non-finite field";
                value.suspended = true;
                return false;
            }

            fov = next_fov;
            fov_viewmodel = next_fov_viewmodel;
            origin = next_origin;
            angles = next_angles;
            return true;
        }

        bool dispatch_game_event(
            script& value,
            const char* event_name,
            std::uintptr_t event)
        {
            if (!value.state || value.suspended ||
                !event_name || !*event_name || !event)
                return false;

            auto& api = runtime->api;
            const auto top = api.lua_gettop(value.state);

            api.lua_getfield(
                value.state,
                lua_globals_index,
                "__mcb_nix_dispatch_event");

            if (api.lua_type(value.state, -1) != lua_tfunction)
            {
                api.lua_settop(value.state, top);
                value.last_error =
                    "internal game event dispatcher missing";
                value.suspended = true;
                return false;
            }

            api.lua_pushstring(value.state, event_name);
            if (!scripting::nix_native::begin_event_scope(
                    api, value.state, event_name, event))
            {
                api.lua_settop(value.state, top);
                value.last_error =
                    "failed to create scoped game_event_t";
                value.suspended = true;
                return false;
            }

            const auto result =
                api.lua_pcall(value.state, 2, 0, 0);

            scripting::nix_native::end_event_scope(
                value.state);

            if (result != 0)
            {
                value.last_error = lua_error(
                    api, value.state,
                    std::string("callback '") +
                        event_name + "' failed");
                api.lua_settop(value.state, top);
                value.suspended = true;
                logging::console::print(
                    "[NixLua] 已暫停 {}：{}",
                    value.path.filename().string(),
                    value.last_error);
                return false;
            }

            api.lua_settop(value.state, top);
            return true;
        }

        bool dispatch(script& value, const char* event)
        {
            if (!value.state || value.suspended) return false;

            auto& api = runtime->api;
            const auto top = api.lua_gettop(value.state);
            api.lua_getfield(value.state, lua_globals_index, "__mcb_nix_dispatch");
            if (api.lua_type(value.state, -1) != lua_tfunction)
            {
                api.lua_settop(value.state, top);
                value.last_error = "internal dispatch function missing";
                value.suspended = true;
                return false;
            }

            api.lua_pushstring(value.state, event);
            const auto result = api.lua_pcall(value.state, 1, 0, 0);
            if (result != 0)
            {
                value.last_error = lua_error(
                    api, value.state,
                    std::string("callback '") + event + "' failed");
                api.lua_settop(value.state, top);
                value.suspended = true;
                logging::console::print(
                    "[NixLua] 已暫停 {}：{}",
                    value.path.filename().string(), value.last_error);
                return false;
            }

            api.lua_settop(value.state, top);
            return true;
        }

        void unload(script& value)
        {
            if (!value.state) return;
            if (!value.suspended) dispatch(value, "unload");
            scripting::nix_native::detach_render_api(value.state);
            scripting::nix_native::detach_event_api(value.state);
            scripting::nix_native::detach_engine_api(value.state);
            scripting::nix_native::detach_cvar_api(value.state);
            scripting::nix_native::detach_entity_api(value.state);
            runtime->api.lua_close(value.state);
            value.state = nullptr;
        }

        void load(script& value)
        {
            value.suspended = false;
            value.last_error.clear();

            std::string bytes;
            if (!read_file(value.path, bytes))
            {
                value.last_error = "cannot read script or file exceeds 8 MiB";
                return;
            }

            auto& api = runtime->api;
            value.state = api.luaL_newstate();
            if (!value.state)
            {
                value.last_error = "luaL_newstate failed";
                return;
            }

            api.luaL_openlibs(value.state);

            std::string error;
            const auto bootstrap = bootstrap_for(value.path.filename().string());
            if (!run_chunk(
                    api, value.state, scripting::nix_bootstrap::value_types,
                    "=MCB_NIX_VALUE_TYPES", error) ||
                !scripting::nix_native::install_entity_api(
                    api, value.state, error) ||
                !scripting::nix_native::install_cvar_api(
                    api, value.state, error) ||
                !scripting::nix_native::install_engine_api(
                    api, value.state, error) ||
                !scripting::nix_native::install_event_api(
                    api, value.state, error) ||
                !scripting::nix_native::install_render_api(
                    api, value.state, error) ||
                !run_chunk(
                    api, value.state, scripting::nix_bootstrap::render_api,
                    "=MCB_NIX_RENDER_API", error) ||
                !run_chunk(
                    api, value.state, bootstrap,
                    "=MCB_NIX_INTERNAL_BOOTSTRAP", error) ||
                !run_chunk(
                    api, value.state, bytes,
                    "@" + value.path.filename().string(), error))
            {
                value.last_error = error;
                scripting::nix_native::detach_render_api(value.state);
            scripting::nix_native::detach_event_api(value.state);
                scripting::nix_native::detach_engine_api(value.state);
                scripting::nix_native::detach_cvar_api(value.state);
                scripting::nix_native::detach_entity_api(value.state);
                api.lua_close(value.state);
                value.state = nullptr;
                logging::console::print(
                    "[NixLua] 載入失敗 {}：{}",
                    value.path.filename().string(), value.last_error);
                return;
            }

            logging::console::print(
                "[NixLua] LuaJIT 已載入：{}",
                value.path.filename().string());
        }

        void scan(bool force)
        {
            std::error_code ec;
            std::vector<std::filesystem::path> files;
            for (const auto& entry :
                 std::filesystem::directory_iterator(enabled_directory, ec))
            {
                if (ec) break;
                if (entry.is_regular_file(ec) &&
                    is_nix_script(entry.path()) &&
                    is_approved_script(entry.path()))
                    files.push_back(entry.path());
                ec.clear();
            }
            std::sort(files.begin(), files.end());

            for (auto it = scripts.begin(); it != scripts.end();)
            {
                if (std::find(files.begin(), files.end(), (*it)->path) == files.end())
                {
                    unload(**it);
                    it = scripts.erase(it);
                }
                else
                {
                    ++it;
                }
            }

            for (const auto& path : files)
            {
                const auto write_time = std::filesystem::last_write_time(path, ec);
                if (ec)
                {
                    ec.clear();
                    continue;
                }

                const auto found = std::find_if(
                    scripts.begin(), scripts.end(),
                    [&](const auto& value) { return value->path == path; });

                if (found == scripts.end())
                {
                    auto value = std::make_unique<script>();
                    value->path = path;
                    value->write_time = write_time;
                    load(*value);
                    scripts.push_back(std::move(value));
                }
                else if (force || (*found)->write_time != write_time)
                {
                    unload(**found);
                    (*found)->write_time = write_time;
                    load(**found);
                }
            }
        }
    };

    nix_runtime::nix_runtime()
        : m_impl(std::make_unique<impl>())
    {
    }

    nix_runtime::~nix_runtime()
    {
        shutdown();
    }

    bool nix_runtime::initialize(void* module_handle)
    {
        std::scoped_lock lock(m_impl->mutex);
        if (m_impl->initialized) return true;

        std::array<wchar_t, 32768> module_path{};
        const auto len = GetModuleFileNameW(
            static_cast<HMODULE>(module_handle),
            module_path.data(), static_cast<DWORD>(module_path.size()));
        if (!len || len >= module_path.size()) return false;

        const auto root =
            std::filesystem::path(module_path.data()).parent_path();

        m_impl->enabled_directory =
            root / L"scripts" / L"nixware" / L"enabled";

        std::error_code ec;
        std::filesystem::create_directories(m_impl->enabled_directory, ec);
        if (ec)
        {
            logging::console::print(
                "[NixLua] 無法建立啟用資料夾：{}", ec.message());
            return false;
        }

        auto runtime = std::make_unique<runtime_image>();
        std::string error;
        if (!runtime->open(root / L"runtime" / L"lua51.dll", error))
        {
            logging::console::print("[NixLua] Runtime 停用：{}", error);
            return false;
        }

        m_impl->runtime = std::move(runtime);
        m_impl->last_scan = std::chrono::steady_clock::time_point{};
        m_impl->initialized = true;

        logging::console::print(
            "[NixLua] 真 LuaJIT Runtime 已啟用；revision={}；僅載入具匹配 .approved.sha256 的腳本",
            MCB_NIX_LUAJIT_REVISION);
        return true;
    }

    void nix_runtime::shutdown()
    {
        if (!m_impl) return;
        std::scoped_lock lock(m_impl->mutex);
        for (auto& value : m_impl->scripts)
            m_impl->unload(*value);
        m_impl->scripts.clear();
        m_impl->runtime.reset();
        m_impl->initialized = false;
    }

    void nix_runtime::on_frame()
    {
        std::scoped_lock lock(m_impl->mutex);
        if (!m_impl->initialized || !m_impl->runtime) return;

        scripting::nix_native::advance_render_frame();
        m_impl->run_native_probe_once();

        const auto now = std::chrono::steady_clock::now();
        if (m_impl->last_scan.time_since_epoch().count() == 0 ||
            now - m_impl->last_scan >= std::chrono::milliseconds(750))
        {
            m_impl->scan(false);
            m_impl->last_scan = now;
        }

        for (auto& value : m_impl->scripts)
        {
            if (!value->state || value->suspended)
                continue;

            scripting::nix_native::begin_render_scope(
                value->state);
            m_impl->dispatch(*value, "paint");
            scripting::nix_native::end_render_scope(
                value->state);
        }
    }

    void nix_runtime::on_override_view(std::uintptr_t view_setup)
    {
        if (!view_setup) return;

        std::scoped_lock lock(m_impl->mutex);
        if (!m_impl->initialized || !m_impl->runtime) return;

        constexpr std::ptrdiff_t fov_offset = 0x498;
        constexpr std::ptrdiff_t viewmodel_fov_offset = 0x49c;
        constexpr std::ptrdiff_t origin_offset = 0x4a0;
        constexpr std::ptrdiff_t angles_offset = 0x4b8;

        auto fov = memory::safe_read<float>(
            view_setup + fov_offset);
        auto fov_viewmodel = memory::safe_read<float>(
            view_setup + viewmodel_fov_offset);
        auto origin = memory::safe_read<math::vector3>(
            view_setup + origin_offset);
        auto angles = memory::safe_read<math::vector3>(
            view_setup + angles_offset);

        if (!fov || !fov_viewmodel || !origin || !angles)
            return;

        for (auto& value : m_impl->scripts)
        {
            if (!value->state || value->suspended)
                continue;

            m_impl->dispatch_override_view(
                *value, *fov, *fov_viewmodel, *origin, *angles);
        }

        if (!std::isfinite(*fov) ||
            !std::isfinite(*fov_viewmodel) ||
            !std::isfinite(origin->x) ||
            !std::isfinite(origin->y) ||
            !std::isfinite(origin->z) ||
            !std::isfinite(angles->x) ||
            !std::isfinite(angles->y) ||
            !std::isfinite(angles->z))
            return;

        memory::write<float>(
            view_setup + fov_offset, *fov);
        memory::write<float>(
            view_setup + viewmodel_fov_offset, *fov_viewmodel);
        memory::write<math::vector3>(
            view_setup + origin_offset, *origin);
        memory::write<math::vector3>(
            view_setup + angles_offset, *angles);
    }

    void nix_runtime::on_game_event(
        const char* event_name,
        std::uintptr_t event)
    {
        if (!event_name || !*event_name || !event)
            return;

        std::scoped_lock lock(m_impl->mutex);
        if (!m_impl->initialized || !m_impl->runtime)
            return;

        for (auto& value : m_impl->scripts)
        {
            if (!value->state || value->suspended)
                continue;

            m_impl->dispatch_game_event(
                *value, event_name, event);
        }
    }

    bool nix_runtime::import_script_dialog(void* owner_window)
    {
        std::array<wchar_t, 32768> selected{};
        constexpr wchar_t filter[] =
            L"Nixware Lua (*.lua;*.luac)\0*.lua;*.luac\0"
            L"All files (*.*)\0*.*\0\0";

        OPENFILENAMEW dialog{};
        dialog.lStructSize = sizeof(dialog);
        dialog.hwndOwner = static_cast<HWND>(owner_window);
        dialog.lpstrFilter = filter;
        dialog.lpstrFile = selected.data();
        dialog.nMaxFile = static_cast<DWORD>(selected.size());
        dialog.lpstrTitle = L"Import and explicitly approve Nixware Lua";
        dialog.Flags =
            OFN_FILEMUSTEXIST |
            OFN_PATHMUSTEXIST |
            OFN_EXPLORER |
            OFN_NOCHANGEDIR;

        if (!GetOpenFileNameW(&dialog))
            return false;

        const std::filesystem::path source{selected.data()};
        if (!is_nix_script(source))
        {
            logging::console::print(
                "[NixLua] 导入拒绝：只允许 .lua / .luac");
            return false;
        }

        std::error_code ec;
        const auto size = std::filesystem::file_size(source, ec);
        if (ec || size == 0 || size > max_nix_script)
        {
            logging::console::print(
                "[NixLua] 导入拒绝：脚本为空、不可读或超过 8 MiB");
            return false;
        }

        const auto source_hash = lowercase(sha256_file(source));
        if (!is_hex_sha256(source_hash))
        {
            logging::console::print(
                "[NixLua] 导入失败：无法计算脚本 SHA-256");
            return false;
        }

        if (source_hash == blocked_abusive_original_sha256)
        {
            logging::console::print(
                "[NixLua] 已保持封存：该原稿禁止进入可执行目录");
            return false;
        }

        std::filesystem::path directory;
        {
            std::scoped_lock lock(m_impl->mutex);
            if (!m_impl->initialized)
                return false;
            directory = m_impl->enabled_directory;
        }

        const auto destination =
            directory / source.filename();
        auto temporary = destination;
        temporary += L".mcb-import.tmp";

        std::filesystem::remove(temporary, ec);
        ec.clear();
        std::filesystem::copy_file(
            source, temporary,
            std::filesystem::copy_options::overwrite_existing,
            ec);

        if (ec ||
            lowercase(sha256_file(temporary)) != source_hash)
        {
            std::filesystem::remove(temporary, ec);
            logging::console::print(
                "[NixLua] 导入失败：复制后 SHA-256 核验不一致");
            return false;
        }

        if (!MoveFileExW(
                temporary.c_str(),
                destination.c_str(),
                MOVEFILE_REPLACE_EXISTING |
                MOVEFILE_WRITE_THROUGH))
        {
            std::filesystem::remove(temporary, ec);
            logging::console::print(
                "[NixLua] 导入失败：替换目标文件失败，Win32={}",
                GetLastError());
            return false;
        }

        auto sidecar = destination;
        sidecar += L".approved.sha256";
        auto sidecar_tmp = sidecar;
        sidecar_tmp += L".tmp";

        {
            std::ofstream output(
                sidecar_tmp,
                std::ios::binary | std::ios::trunc);
            if (!output)
            {
                logging::console::print(
                    "[NixLua] 导入失败：无法写入批准侧档");
                return false;
            }

            output.write(
                source_hash.data(),
                static_cast<std::streamsize>(source_hash.size()));
            output.put('\n');
            output.flush();
            if (!output)
            {
                logging::console::print(
                    "[NixLua] 导入失败：批准侧档写入不完整");
                return false;
            }
        }

        if (!MoveFileExW(
                sidecar_tmp.c_str(),
                sidecar.c_str(),
                MOVEFILE_REPLACE_EXISTING |
                MOVEFILE_WRITE_THROUGH))
        {
            std::filesystem::remove(sidecar_tmp, ec);
            logging::console::print(
                "[NixLua] 导入失败：批准侧档替换失败，Win32={}",
                GetLastError());
            return false;
        }

        {
            std::scoped_lock lock(m_impl->mutex);
            m_impl->last_scan =
                std::chrono::steady_clock::time_point{};
        }

        logging::console::print(
            "[NixLua] 已明确批准：{} sha256={}",
            destination.filename().string(), source_hash);
        return true;
    }

    void nix_runtime::reload_all()
    {
        std::scoped_lock lock(m_impl->mutex);
        if (!m_impl->initialized || !m_impl->runtime) return;
        m_impl->scan(true);
    }

    bool nix_runtime::ready() const
    {
        std::scoped_lock lock(m_impl->mutex);
        return m_impl->initialized && m_impl->runtime != nullptr;
    }

    const std::filesystem::path& nix_runtime::script_directory() const
    {
        return m_impl->enabled_directory;
    }

    std::vector<nix_script_status> nix_runtime::script_statuses() const
    {
        std::scoped_lock lock(m_impl->mutex);
        std::vector<nix_script_status> result;
        result.reserve(m_impl->scripts.size());
        for (const auto& value : m_impl->scripts)
        {
            result.push_back({
                value->path.filename().string(),
                value->state != nullptr,
                value->suspended,
                value->last_error
            });
        }
        return result;
    }
}
