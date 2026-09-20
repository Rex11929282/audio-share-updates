#include <pch/pch.hpp>

#include "nix_engine_bridge.hpp"

#include <protection/game_addresses.hpp>
#include <core/systems/systems.hpp>
#include <utilities/addresses/addresses.hpp>
#include <utilities/logging/logging.hpp>
#include <utilities/memory/memory.hpp>

#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>

namespace
{
    using scripting::nix_native::luajit_api;
    using scripting::nix_native::lua_globals_index;
    using scripting::nix_native::lua_tstring;
    using scripting::nix_native::lua_ttable;
    using ::nix_lua_cfunction;

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

    int __cdecl execute_client_cmd(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        if (api->lua_type(state, 1) != lua_tstring)
            return 0;

        std::size_t length{};
        const auto* raw = api->lua_tolstring(state, 1, &length);
        if (!raw || length == 0 || length > 4096)
            return 0;

        const std::string_view view(raw, length);
        if (view.find('\0') != std::string_view::npos)
            return 0;

        const auto command = std::string(view);
        const auto fn = PATTERN(patterns::engine_client_cmd);
        if (!fn || !addresses::globals::source2engine_to_client)
            return 0;

        memory::call<void>(
            fn,
            addresses::globals::source2engine_to_client,
            0,
            command.c_str(),
            0x7ffef001);

        return 0;
    }

    bool string_arg(
        luajit_api& api,
        lua_State* state,
        int index,
        std::string& out,
        std::size_t max_length)
    {
        if (api.lua_type(state, index) != lua_tstring)
            return false;

        std::size_t length{};
        const auto* raw =
            api.lua_tolstring(state, index, &length);
        if (!raw || length == 0 || length > max_length)
            return false;

        out.assign(raw, length);
        return true;
    }

    int __cdecl chat_print(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        std::string message;
        if (!string_arg(*api, state, 1, message, 900))
            return 0;
        if (message.find('\0') != std::string::npos)
            return 0;

        const auto local = systems::g_local.get();
        if (!local.is_valid() ||
            systems::g_local.is_in_cinematic())
            return 0;

        const auto hud_element =
            memory::call<std::uintptr_t>(
                PATTERN(patterns::find_hud_element),
                xs("CCSGO_HudVoiceStatus"));
        if (!hud_element)
            return 0;

        const auto voice = hud_element - 32;
        std::uint8_t flags[2]{1, 0};
        memory::call<void>(
            PATTERN(patterns::set_voice_data),
            voice,
            message.c_str(),
            0xFFFFFFFF,
            flags);
        return 0;
    }

    int __cdecl color_print(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        std::string message;
        if (!string_arg(*api, state, 1, message, 1 << 20))
            return 0;

        // Nixware color_print targets its own colored script console.
        // MCB has no equivalent RGB console backend. Preserve the exact
        // text bytes (up to the first embedded NUL used by Nixware as a
        // no-newline marker) in MCB diagnostics instead of pretending
        // the color argument is honored.
        if (const auto nul = message.find('\0');
            nul != std::string::npos)
            message.resize(nul);

        if (!message.empty())
            logging::console::print_raw(message.c_str());
        return 0;
    }

    void set_global_function(
        luajit_api& api,
        lua_State* state,
        const char* name,
        nix_lua_cfunction function)
    {
        api.lua_pushcclosure(state, function, 0);
        api.lua_setfield(
            state, lua_globals_index, name);
    }

    void set_c_function(
        luajit_api& api,
        lua_State* state,
        int table_index,
        const char* name,
        nix_lua_cfunction function)
    {
        api.lua_pushcclosure(state, function, 0);
        api.lua_setfield(state, table_index, name);
    }
}

namespace scripting::nix_native
{
    bool install_engine_api(
        luajit_api& api,
        lua_State* state,
        std::string& error)
    {
        if (!state)
        {
            error = "engine bridge received null LuaJIT state";
            return false;
        }

        {
            std::scoped_lock lock(g_mutex);
            if (!g_states.emplace(
                    state, state_binding{&api}).second)
            {
                error = "engine bridge already attached to this state";
                return false;
            }
        }

        const auto top = api.lua_gettop(state);

        api.lua_getfield(state, lua_globals_index, "engine");
        if (api.lua_type(state, -1) != lua_ttable)
        {
            api.lua_settop(state, top);
            api.lua_createtable(state, 0, 8);
        }

        set_c_function(
            api, state, -2,
            "execute_client_cmd", &execute_client_cmd);
        set_c_function(
            api, state, -2,
            "chat_print", &chat_print);

        api.lua_setfield(state, lua_globals_index, "engine");
        set_global_function(
            api, state, "color_print", &color_print);
        api.lua_settop(state, top);
        return true;
    }

    void detach_engine_api(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_mutex);
        g_states.erase(state);
    }
}
