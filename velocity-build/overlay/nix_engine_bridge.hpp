#pragma once

#include "nix_luajit_api.hpp"

#include <string>

namespace scripting::nix_native
{
    bool install_engine_api(luajit_api& api, lua_State* state, std::string& error);
    void detach_engine_api(lua_State* state);
}
