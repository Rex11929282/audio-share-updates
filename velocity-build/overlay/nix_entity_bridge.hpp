#pragma once

#include "nix_luajit_api.hpp"

#include <cstdint>
#include <string>

namespace scripting::nix_native
{
    // Registers the first real native vertical slice:
    //   entitylist.get_local_player_pawn/controller
    //   entitylist.get_entity_from_handle
    //   base_entity_t numeric pointer indexing for real FFI casts
    //   base_entity_t origin/rotation/class/handle access
    //   engine.get_netvar_offset for client.dll schema
    //
    // No placeholder object is installed. If native data cannot be resolved,
    // the API returns nil instead of fabricating a value.
    bool install_entity_api(luajit_api& api, lua_State* state, std::string& error);
    void detach_entity_api(lua_State* state);

    // Push a live host entity as the same base_entity_t userdata used by entitylist.
    // Returns false and pushes nil when the pointer is null.
    bool push_entity_value(
        luajit_api& api,
        lua_State* state,
        std::uintptr_t entity);
}
