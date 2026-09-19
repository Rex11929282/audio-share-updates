#include <pch/pch.hpp>

#include "nix_cvar_bridge.hpp"

#include <utilities/addresses/addresses.hpp>
#include <utilities/fnv1a.hpp>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>

namespace
{
    using scripting::nix_native::luajit_api;
    using scripting::nix_native::lua_globals_index;
    using scripting::nix_native::lua_registry_index;
    using scripting::nix_native::lua_tstring;
    using scripting::nix_native::lua_tuserdata;
    using ::nix_lua_cfunction;

    constexpr char convar_metatable[] = "MCB_NIX_convar_t";
    constexpr char cvars_metatable[] = "MCB_NIX_cvars_table";

    enum class source2_convar_type : std::int16_t
    {
        invalid = -1,
        boolean = 0,
        int16 = 1,
        uint16 = 2,
        int32 = 3,
        uint32 = 4,
        int64 = 5,
        uint64 = 6,
        float32 = 7,
        float64 = 8,
        string = 9,
        color = 10,
        vector2 = 11,
        vector3 = 12,
        vector4 = 13,
        qangle = 14,
        vector_ws = 15
    };

    struct convar_ref
    {
        c_convar* value{};
    };

    struct state_binding
    {
        luajit_api* api{};
    };

    std::mutex g_mutex{};
    std::unordered_map<lua_State*, state_binding> g_states{};

    luajit_api* api_for(lua_State* state)
    {
        std::scoped_lock lock(g_mutex);
        const auto it = g_states.find(state);
        return it == g_states.end() ? nullptr : it->second.api;
    }

    convar_ref* get_ref(luajit_api& api, lua_State* state, int index)
    {
        if (api.lua_type(state, index) != lua_tuserdata)
            return nullptr;
        return static_cast<convar_ref*>(
            api.lua_touserdata(state, index));
    }

    bool valid(const convar_ref* ref)
    {
        return ref && ref->value && ref->value->m_name;
    }

    int push_nil(luajit_api& api, lua_State* state)
    {
        api.lua_pushnil(state);
        return 1;
    }

    long double numeric_value(const c_convar& cvar, bool& ok)
    {
        ok = true;
        switch (static_cast<source2_convar_type>(cvar.m_type))
        {
        case source2_convar_type::boolean:
            return cvar.m_value.i1 ? 1.0L : 0.0L;
        case source2_convar_type::int16:
            return static_cast<long double>(cvar.m_value.i16);
        case source2_convar_type::uint16:
            return static_cast<long double>(
                static_cast<std::uint16_t>(cvar.m_value.i16));
        case source2_convar_type::int32:
            return static_cast<long double>(cvar.m_value.i32);
        case source2_convar_type::uint32:
            return static_cast<long double>(
                static_cast<std::uint32_t>(cvar.m_value.i32));
        case source2_convar_type::int64:
            return static_cast<long double>(cvar.m_value.i64);
        case source2_convar_type::uint64:
            return static_cast<long double>(
                static_cast<std::uint64_t>(cvar.m_value.i64));
        case source2_convar_type::float32:
            return static_cast<long double>(cvar.m_value.fl);
        case source2_convar_type::float64:
            return static_cast<long double>(cvar.m_value.db);
        default:
            ok = false;
            return 0.0L;
        }
    }

    int __cdecl convar_get_name(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        api->lua_pushstring(state, ref->value->m_name);
        return 1;
    }

    int __cdecl convar_get_desc(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        api->lua_pushstring(
            state,
            ref->value->m_description
                ? ref->value->m_description
                : "");
        return 1;
    }

    int __cdecl convar_get_bool(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        if (static_cast<source2_convar_type>(
                ref->value->m_type) ==
            source2_convar_type::string)
        {
            const auto* text = ref->value->m_value.sz;
            const bool truth =
                text && text[0] != '\0' &&
                _stricmp(text, "0") != 0 &&
                _stricmp(text, "false") != 0;
            api->lua_pushboolean(state, truth ? 1 : 0);
            return 1;
        }

        bool ok{};
        const auto number = numeric_value(*ref->value, ok);
        if (!ok) return push_nil(*api, state);

        api->lua_pushboolean(state, number != 0.0L ? 1 : 0);
        return 1;
    }

    int __cdecl convar_get_int(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        bool ok{};
        const auto number = numeric_value(*ref->value, ok);
        if (ok)
        {
            api->lua_pushnumber(
                state,
                static_cast<double>(
                    static_cast<std::int64_t>(number)));
            return 1;
        }

        if (static_cast<source2_convar_type>(
                ref->value->m_type) !=
            source2_convar_type::string)
            return push_nil(*api, state);

        const auto* text = ref->value->m_value.sz;
        if (!text) return push_nil(*api, state);

        char* end{};
        const auto parsed = std::strtoll(text, &end, 10);
        if (end == text) return push_nil(*api, state);

        api->lua_pushnumber(
            state, static_cast<double>(parsed));
        return 1;
    }

    int __cdecl convar_get_float(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        bool ok{};
        const auto number = numeric_value(*ref->value, ok);
        if (ok)
        {
            const auto value = static_cast<double>(number);
            if (!std::isfinite(value))
                return push_nil(*api, state);
            api->lua_pushnumber(state, value);
            return 1;
        }

        if (static_cast<source2_convar_type>(
                ref->value->m_type) !=
            source2_convar_type::string)
            return push_nil(*api, state);

        const auto* text = ref->value->m_value.sz;
        if (!text) return push_nil(*api, state);

        char* end{};
        const auto value = std::strtod(text, &end);
        if (end == text || !std::isfinite(value))
            return push_nil(*api, state);

        api->lua_pushnumber(state, value);
        return 1;
    }

    int __cdecl convar_get_string(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;
        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref)) return push_nil(*api, state);

        const auto type =
            static_cast<source2_convar_type>(
                ref->value->m_type);

        if (type == source2_convar_type::string)
        {
            api->lua_pushstring(
                state,
                ref->value->m_value.sz
                    ? ref->value->m_value.sz
                    : "");
            return 1;
        }

        bool ok{};
        const auto number = numeric_value(*ref->value, ok);
        if (!ok) return push_nil(*api, state);

        std::array<char, 96> buffer{};
        int count{};
        if (type == source2_convar_type::boolean)
        {
            count = std::snprintf(
                buffer.data(), buffer.size(),
                "%s",
                ref->value->m_value.i1 ? "true" : "false");
        }
        else
        {
            count = std::snprintf(
                buffer.data(), buffer.size(),
                "%.17Lg", number);
        }

        if (count < 0) return push_nil(*api, state);

        const auto length = static_cast<std::size_t>(
            std::min<int>(
                count,
                static_cast<int>(buffer.size() - 1)));
        api->lua_pushlstring(
            state, buffer.data(), length);
        return 1;
    }

    int __cdecl convar_index(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_ref(*api, state, 1);
        if (!valid(ref) ||
            api->lua_type(state, 2) != lua_tstring)
            return push_nil(*api, state);

        std::size_t length{};
        const auto* key =
            api->lua_tolstring(state, 2, &length);
        if (!key) return push_nil(*api, state);

        const std::string_view name(key, length);
        if (name.find('\0') != std::string_view::npos)
            return push_nil(*api, state);

        nix_lua_cfunction fn{};
        if (name == "get_name")
            fn = &convar_get_name;
        else if (name == "get_desc")
            fn = &convar_get_desc;
        else if (name == "get_bool")
            fn = &convar_get_bool;
        else if (name == "get_int")
            fn = &convar_get_int;
        else if (name == "get_float")
            fn = &convar_get_float;
        else if (name == "get_string")
            fn = &convar_get_string;
        else
            return push_nil(*api, state);

        api->lua_pushcclosure(state, fn, 0);
        return 1;
    }

    int __cdecl cvars_index(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        if (api->lua_type(state, 2) != lua_tstring ||
            !addresses::globals::cvar)
            return push_nil(*api, state);

        std::size_t length{};
        const auto* key =
            api->lua_tolstring(state, 2, &length);
        if (!key || length == 0 || length > 255)
            return push_nil(*api, state);

        const std::string_view name(key, length);
        if (name.find('\0') != std::string_view::npos)
            return push_nil(*api, state);

        const std::string owned(name);
        auto* cvar = addresses::globals::cvar->find(
            fnv1a::runtime_hash(owned.c_str()));
        if (!cvar) return push_nil(*api, state);

        auto* ref = static_cast<convar_ref*>(
            api->lua_newuserdata(
                state, sizeof(convar_ref)));
        if (!ref) return push_nil(*api, state);

        *ref = convar_ref{cvar};

        api->lua_getfield(
            state, lua_registry_index, convar_metatable);
        if (api->lua_type(state, -1) != 5)
        {
            scripting::nix_native::lua_pop(
                *api, state, 2);
            return push_nil(*api, state);
        }

        api->lua_setmetatable(state, -2);
        return 1;
    }

    void set_c_function(
        luajit_api& api,
        lua_State* state,
        int table_index,
        const char* name,
        nix_lua_cfunction fn)
    {
        api.lua_pushcclosure(state, fn, 0);
        api.lua_setfield(state, table_index, name);
    }
}

namespace scripting::nix_native
{
    bool install_cvar_api(
        luajit_api& api,
        lua_State* state,
        std::string& error)
    {
        if (!state)
        {
            error = "cvar bridge received null LuaJIT state";
            return false;
        }

        {
            std::scoped_lock lock(g_mutex);
            if (!g_states.emplace(
                    state, state_binding{&api}).second)
            {
                error = "cvar bridge already attached to this state";
                return false;
            }
        }

        const auto top = api.lua_gettop(state);

        api.luaL_newmetatable(
            state, convar_metatable);
        set_c_function(
            api, state, -2,
            "__index", &convar_index);
        api.lua_settop(state, top);

        api.lua_createtable(state, 0, 0);
        api.luaL_newmetatable(
            state, cvars_metatable);
        set_c_function(
            api, state, -2,
            "__index", &cvars_index);
        api.lua_setmetatable(state, -2);
        api.lua_setfield(
            state, lua_globals_index, "cvars");

        api.lua_settop(state, top);
        return true;
    }

    void detach_cvar_api(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_mutex);
        g_states.erase(state);
    }
}
