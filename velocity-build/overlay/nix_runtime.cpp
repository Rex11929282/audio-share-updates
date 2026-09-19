#include <pch/pch.hpp>

#include "nix_runtime.hpp"
#include "nix_luajit_manifest.hpp"

#include <algorithm>
#include <array>
#include <bcrypt.h>
#include <chrono>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <mutex>
#include <string_view>
#include <unordered_map>
#include <utilities/logging/logging.hpp>
#include <windows.h>

#pragma comment(lib, "bcrypt.lib")

namespace
{
    struct lua_State;

    constexpr int lua_globals_index = -10002;
    constexpr int lua_tfunction = 6;
    constexpr int lua_tstring = 4;
    constexpr std::size_t max_nix_script = 8u * 1024u * 1024u;

    struct luajit_api
    {
        lua_State* (__cdecl* luaL_newstate)(){};
        void (__cdecl* lua_close)(lua_State*){};
        void (__cdecl* luaL_openlibs)(lua_State*){};
        int (__cdecl* luaL_loadbuffer)(lua_State*, const char*, std::size_t, const char*){};
        int (__cdecl* lua_pcall)(lua_State*, int, int, int){};
        void (__cdecl* lua_getfield)(lua_State*, int, const char*){};
        void (__cdecl* lua_pushstring)(lua_State*, const char*){};
        void (__cdecl* lua_pushnumber)(lua_State*, double){};
        int (__cdecl* lua_type)(lua_State*, int){};
        const char* (__cdecl* lua_tolstring)(lua_State*, int, std::size_t*){};
        int (__cdecl* lua_gettop)(lua_State*){};
        void (__cdecl* lua_settop)(lua_State*, int){};

        template <typename T>
        static bool bind_one(HMODULE module, T& out, const char* name)
        {
            out = reinterpret_cast<T>(GetProcAddress(module, name));
            return out != nullptr;
        }

        bool bind(HMODULE module)
        {
            return
                bind_one(module, luaL_newstate, "luaL_newstate") &&
                bind_one(module, lua_close, "lua_close") &&
                bind_one(module, luaL_openlibs, "luaL_openlibs") &&
                bind_one(module, luaL_loadbuffer, "luaL_loadbuffer") &&
                bind_one(module, lua_pcall, "lua_pcall") &&
                bind_one(module, lua_getfield, "lua_getfield") &&
                bind_one(module, lua_pushstring, "lua_pushstring") &&
                bind_one(module, lua_pushnumber, "lua_pushnumber") &&
                bind_one(module, lua_type, "lua_type") &&
                bind_one(module, lua_tolstring, "lua_tolstring") &&
                bind_one(module, lua_gettop, "lua_gettop") &&
                bind_one(module, lua_settop, "lua_settop");
        }
    };

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
local supported = { paint = true, unload = true }

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

function get_script_name()
    return ")MCB") + quoted + R"MCB("
end
)MCB";
    }

    struct runtime_image
    {
        HMODULE module{};
        luajit_api api{};
        std::filesystem::path path{};

        ~runtime_image()
        {
            if (module) FreeLibrary(module);
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
                module = existing;
            }
            else
            {
                module = LoadLibraryExW(
                    canonical.c_str(), nullptr,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
                if (!module)
                {
                    error = "LoadLibraryExW(lua51.dll) failed: " +
                        std::to_string(GetLastError());
                    return false;
                }
            }

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
            const bool ok = run_chunk(
                api, probe, runtime_probe, "=MCB_NIX_RUNTIME_PROBE", error);
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
                    api, value.state, bootstrap,
                    "=MCB_NIX_INTERNAL_BOOTSTRAP", error) ||
                !run_chunk(
                    api, value.state, bytes,
                    "@" + value.path.filename().string(), error))
            {
                value.last_error = error;
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
                if (entry.is_regular_file(ec) && is_nix_script(entry.path()))
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
            "[NixLua] 真 LuaJIT Runtime 已啟用；revision={}；只自動載入 scripts/nixware/enabled",
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

        const auto now = std::chrono::steady_clock::now();
        if (m_impl->last_scan.time_since_epoch().count() == 0 ||
            now - m_impl->last_scan >= std::chrono::milliseconds(750))
        {
            m_impl->scan(false);
            m_impl->last_scan = now;
        }

        for (auto& value : m_impl->scripts)
            if (value->state && !value->suspended)
                m_impl->dispatch(*value, "paint");
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
