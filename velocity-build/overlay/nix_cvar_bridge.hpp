#pragma once

#include "nix_luajit_api.hpp"

#include <string>

namespace scripting::nix_native
{
    // Dynamic read-only Nixware cvars table backed by the game's real cvar registry.
    bool install_cvar_api(luajit_api& api, lua_State* state, std::string& error);
    void detach_cvar_api(lua_State* state);
}
