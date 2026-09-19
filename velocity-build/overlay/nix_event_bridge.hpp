#pragma once

#include "nix_luajit_api.hpp"

#include <cstdint>
#include <string>

namespace scripting::nix_native
{
    bool install_event_api(luajit_api& api, lua_State* state, std::string& error);
    void detach_event_api(lua_State* state);

    // Starts a synchronous event scope and pushes one game_event_t userdata.
    // The userdata becomes invalid immediately after end_event_scope().
    bool begin_event_scope(
        luajit_api& api,
        lua_State* state,
        const char* event_name,
        std::uintptr_t event);
    void end_event_scope(lua_State* state);
}
