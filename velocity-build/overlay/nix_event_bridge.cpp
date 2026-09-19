#include <pch/pch.hpp>

#include "nix_event_bridge.hpp"
#include "nix_entity_bridge.hpp"

#include <protection/game_addresses.hpp>
#include <utilities/cstypes.hpp>
#include <utilities/memory/memory.hpp>

#include <cstdint>
#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>

namespace
{
    using scripting::nix_native::luajit_api;
    using scripting::nix_native::lua_registry_index;
    using scripting::nix_native::lua_tstring;
    using scripting::nix_native::lua_tuserdata;
    using ::nix_lua_cfunction;

    constexpr char event_metatable[] = "MCB_NIX_game_event_t";

    struct event_ref
    {
        std::uint64_t generation{};
    };

    struct state_binding
    {
        luajit_api* api{};
        std::uintptr_t active_event{};
        std::string active_name{};
        std::uint64_t generation{};
    };

    struct active_event
    {
        luajit_api* api{};
        std::uintptr_t event{};
        std::string name{};
        std::uint64_t generation{};
    };

    std::mutex g_mutex{};
    std::unordered_map<lua_State*, state_binding> g_states{};

    state_binding* binding_for(lua_State* state)
    {
        const auto it = g_states.find(state);
        return it == g_states.end() ? nullptr : &it->second;
    }

    bool get_string_arg(
        luajit_api& api,
        lua_State* state,
        int index,
        std::string& out)
    {
        if (api.lua_type(state, index) != lua_tstring)
            return false;

        std::size_t length{};
        const auto* raw = api.lua_tolstring(state, index, &length);
        if (!raw || length == 0 || length > 255)
            return false;

        const std::string_view view(raw, length);
        if (view.find('\0') != std::string_view::npos)
            return false;

        out.assign(view);
        return true;
    }

    bool resolve_active(
        lua_State* state,
        event_ref*& ref,
        active_event& out)
    {
        std::scoped_lock lock(g_mutex);
        auto* binding = binding_for(state);
        if (!binding || !binding->api || !binding->active_event)
            return false;

        auto* api = binding->api;
        if (api->lua_type(state, 1) != lua_tuserdata)
            return false;

        ref = static_cast<event_ref*>(
            api->lua_touserdata(state, 1));
        if (!ref || ref->generation != binding->generation)
            return false;

        out.api = api;
        out.event = binding->active_event;
        out.name = binding->active_name;
        out.generation = binding->generation;
        return true;
    }

    int push_nil(luajit_api& api, lua_State* state)
    {
        api.lua_pushnil(state);
        return 1;
    }

    int __cdecl event_get_name(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        current.api->lua_pushlstring(
            state,
            current.name.data(),
            current.name.size());
        return 1;
    }

    int __cdecl event_get_int(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        auto* api = current.api;
        std::string key;
        if (!get_string_arg(*api, state, 2, key))
            return push_nil(*api, state);

        const auto fn = PATTERN(patterns::game_event_get_int);
        if (!fn) return push_nil(*api, state);

        const auto value = memory::call<int>(
            fn,
            current.event,
            key.c_str(),
            false);
        api->lua_pushinteger(state, value);
        return 1;
    }

    int __cdecl event_get_float(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        auto* api = current.api;
        std::string key;
        if (!get_string_arg(*api, state, 2, key))
            return push_nil(*api, state);

        const auto fn = PATTERN(patterns::game_event_get_float);
        if (!fn) return push_nil(*api, state);

        const auto value = memory::call<float>(
            fn,
            current.event,
            key.c_str(),
            0.0f);
        api->lua_pushnumber(state, value);
        return 1;
    }

    int __cdecl event_get_pawn(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        auto* api = current.api;
        std::string key;
        if (!get_string_arg(*api, state, 2, key))
            return push_nil(*api, state);

        const auto fn = PATTERN(patterns::game_event_get_pawn);
        if (!fn) return push_nil(*api, state);

        const cstypes::event_hash event_key{
            0, key.c_str()
        };
        const auto entity = memory::call<std::uintptr_t>(
            fn,
            current.event,
            &event_key);

        scripting::nix_native::push_entity_value(
            *api, state, entity);
        return 1;
    }

    int __cdecl event_get_controller(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        auto* api = current.api;
        std::string key;
        if (!get_string_arg(*api, state, 2, key))
            return push_nil(*api, state);

        const auto fn = PATTERN(patterns::game_event_get_controller);
        if (!fn) return push_nil(*api, state);

        const cstypes::event_hash event_key{
            0, key.c_str()
        };
        const auto entity = memory::call<std::uintptr_t>(
            fn,
            current.event,
            &event_key);

        scripting::nix_native::push_entity_value(
            *api, state, entity);
        return 1;
    }

    int __cdecl event_index(lua_State* state)
    {
        event_ref* ref{};
        active_event current{};
        if (!resolve_active(state, ref, current))
            return 0;

        auto* api = current.api;
        if (api->lua_type(state, 2) != lua_tstring)
            return push_nil(*api, state);

        std::size_t length{};
        const auto* raw =
            api->lua_tolstring(state, 2, &length);
        if (!raw) return push_nil(*api, state);

        const std::string_view key(raw, length);
        nix_lua_cfunction fn{};

        if (key == "get_name")
            fn = &event_get_name;
        else if (key == "get_int")
            fn = &event_get_int;
        else if (key == "get_float")
            fn = &event_get_float;
        else if (key == "get_pawn")
            fn = &event_get_pawn;
        else if (key == "get_controller")
            fn = &event_get_controller;
        else
            return push_nil(*api, state);

        api->lua_pushcclosure(state, fn, 0);
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
    bool install_event_api(
        luajit_api& api,
        lua_State* state,
        std::string& error)
    {
        if (!state)
        {
            error = "event bridge received null LuaJIT state";
            return false;
        }

        {
            std::scoped_lock lock(g_mutex);
            if (!g_states.emplace(
                    state, state_binding{&api}).second)
            {
                error = "event bridge already attached to this state";
                return false;
            }
        }

        const auto top = api.lua_gettop(state);
        api.luaL_newmetatable(state, event_metatable);
        set_c_function(
            api, state, -2, "__index", &event_index);
        api.lua_settop(state, top);
        return true;
    }

    void detach_event_api(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_mutex);
        g_states.erase(state);
    }

    bool begin_event_scope(
        luajit_api& api,
        lua_State* state,
        const char* event_name,
        std::uintptr_t event)
    {
        if (!state || !event_name || !*event_name || !event)
        {
            api.lua_pushnil(state);
            return false;
        }

        std::scoped_lock lock(g_mutex);
        auto* binding = binding_for(state);
        if (!binding || binding->api != &api)
        {
            api.lua_pushnil(state);
            return false;
        }

        binding->active_event = event;
        binding->active_name = event_name;
        ++binding->generation;
        if (binding->generation == 0)
            ++binding->generation;

        auto* ref = static_cast<event_ref*>(
            api.lua_newuserdata(
                state, sizeof(event_ref)));
        if (!ref)
        {
            binding->active_event = 0;
            binding->active_name.clear();
            api.lua_pushnil(state);
            return false;
        }

        *ref = event_ref{binding->generation};

        api.lua_getfield(
            state, lua_registry_index, event_metatable);
        if (api.lua_type(state, -1) != 5)
        {
            scripting::nix_native::lua_pop(
                api, state, 2);
            binding->active_event = 0;
            binding->active_name.clear();
            api.lua_pushnil(state);
            return false;
        }

        api.lua_setmetatable(state, -2);
        return true;
    }

    void end_event_scope(lua_State* state)
    {
        if (!state) return;

        std::scoped_lock lock(g_mutex);
        auto* binding = binding_for(state);
        if (!binding) return;

        binding->active_event = 0;
        binding->active_name.clear();
    }
}
