#pragma once

#include "nix_luajit_api.hpp"

#include <string>

namespace scripting::nix_native
{
    bool install_render_api(luajit_api& api, lua_State* state, std::string& error);
    void detach_render_api(lua_State* state);
    void advance_render_frame();
}
