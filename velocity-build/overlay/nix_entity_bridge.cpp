#include <pch/pch.hpp>

#include "nix_entity_bridge.hpp"

#include <core/systems/systems.hpp>
#include <utilities/fnv1a.hpp>
#include <utilities/memory/memory.hpp>

#include <array>
#include <cmath>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <limits>
#include <mutex>
#include <string_view>
#include <unordered_map>

namespace
{
    using scripting::nix_native::luajit_api;
    using ::nix_lua_cfunction;
    using scripting::nix_native::lua_globals_index;
    using scripting::nix_native::lua_registry_index;
    using scripting::nix_native::lua_tnumber;
    using scripting::nix_native::lua_tstring;
    using scripting::nix_native::lua_tuserdata;

    constexpr char entity_metatable[] = "MCB_NIX_base_entity_t";

    enum class entity_source : std::uint8_t
    {
        local_pawn,
        local_controller,
        handle
    };

    struct entity_ref
    {
        std::uintptr_t ptr{};
        std::uint32_t handle{};
        entity_source source{};
    };

    struct state_binding
    {
        luajit_api* api{};
    };

    std::mutex g_binding_mutex{};
    std::unordered_map<lua_State*, state_binding> g_bindings{};

    luajit_api* api_for(lua_State* state)
    {
        std::scoped_lock lock(g_binding_mutex);
        const auto it = g_bindings.find(state);
        return it == g_bindings.end() ? nullptr : it->second.api;
    }

    bool current_entity(const entity_ref& ref)
    {
        if (!ref.ptr) return false;

        switch (ref.source)
        {
        case entity_source::local_pawn:
            return systems::g_local.get().pawn == ref.ptr;

        case entity_source::local_controller:
            return systems::g_local.get().controller == ref.ptr;

        case entity_source::handle:
            return ref.handle != 0 &&
                   systems::g_entities.lookup(ref.handle) == ref.ptr;
        }

        return false;
    }

    entity_ref* get_entity(luajit_api& api, lua_State* state, int index)
    {
        if (api.lua_type(state, index) != lua_tuserdata)
            return nullptr;

        return static_cast<entity_ref*>(api.lua_touserdata(state, index));
    }

    int push_nil(luajit_api& api, lua_State* state)
    {
        api.lua_pushnil(state);
        return 1;
    }

    bool push_entity(
        luajit_api& api,
        lua_State* state,
        std::uintptr_t ptr,
        entity_source source,
        std::uint32_t handle = 0)
    {
        if (!ptr)
        {
            api.lua_pushnil(state);
            return false;
        }

        auto* ref = static_cast<entity_ref*>(
            api.lua_newuserdata(state, sizeof(entity_ref)));
        if (!ref)
        {
            api.lua_pushnil(state);
            return false;
        }

        *ref = entity_ref{ptr, handle, source};

        api.lua_getfield(state, lua_registry_index, entity_metatable);
        if (api.lua_type(state, -1) != 5)
        {
            scripting::nix_native::lua_pop(api, state, 2);
            api.lua_pushnil(state);
            return false;
        }

        api.lua_setmetatable(state, -2);
        return true;
    }

    bool checked_address(
        std::uintptr_t base,
        std::ptrdiff_t offset,
        std::uintptr_t& address)
    {
        if (offset >= 0)
        {
            const auto add = static_cast<std::uintptr_t>(offset);
            if (base > std::numeric_limits<std::uintptr_t>::max() - add)
                return false;
            address = base + add;
            return true;
        }

        const auto magnitude =
            static_cast<std::uintptr_t>(-(offset + 1)) + 1u;
        if (base < magnitude) return false;
        address = base - magnitude;
        return true;
    }

    bool get_lua_string(
        luajit_api& api,
        lua_State* state,
        int index,
        std::string_view& result)
    {
        if (api.lua_type(state, index) != lua_tstring)
            return false;

        std::size_t length{};
        const auto* text = api.lua_tolstring(state, index, &length);
        if (!text) return false;

        result = std::string_view(text, length);
        return result.find('\0') == std::string_view::npos;
    }

    bool push_constructor3(
        luajit_api& api,
        lua_State* state,
        const char* constructor,
        float x,
        float y,
        float z)
    {
        const auto top = api.lua_gettop(state);
        api.lua_getfield(state, lua_globals_index, constructor);
        if (api.lua_type(state, -1) != 6)
        {
            api.lua_settop(state, top);
            api.lua_pushnil(state);
            return false;
        }

        api.lua_pushnumber(state, x);
        api.lua_pushnumber(state, y);
        api.lua_pushnumber(state, z);
        if (api.lua_pcall(state, 3, 1, 0) != 0)
        {
            api.lua_settop(state, top);
            api.lua_pushnil(state);
            return false;
        }
        return true;
    }

    bool resolve_scene_vector(
        std::uintptr_t entity,
        const char* field,
        math::vector3& result)
    {
        const auto node_offset = systems::schemas::try_lookup(
            "C_BaseEntity", fnv1a::runtime_hash("m_pGameSceneNode"));
        const auto vector_offset = systems::schemas::try_lookup(
            "CGameSceneNode", fnv1a::runtime_hash(field));

        if (!node_offset || !vector_offset) return false;

        const auto node = memory::safe_read<std::uintptr_t>(
            entity + *node_offset).value_or(0);
        if (!node) return false;

        const auto value = memory::safe_read<math::vector3>(
            node + *vector_offset);
        if (!value) return false;

        if (!std::isfinite(value->x) ||
            !std::isfinite(value->y) ||
            !std::isfinite(value->z))
            return false;

        result = *value;
        return true;
    }

    std::uint32_t derive_entity_handle(std::uintptr_t entity)
    {
        // Source 2 public SDK CEntityIdentity layout:
        // entity + 0x10 -> CEntityIdentity*
        // identity + 0x10 -> CEntityHandle
        // identity + 0x30 -> EntityFlags_t
        // EF_IS_INVALID_EHANDLE decrements the 17-bit serial component.
        const auto identity = memory::safe_read<std::uintptr_t>(
            entity + 0x10).value_or(0);
        if (!identity) return 0;

        auto handle = memory::safe_read<std::uint32_t>(
            identity + 0x10).value_or(0xffffffffu);
        if (handle == 0xffffffffu) return 0;

        const auto flags = memory::safe_read<std::uint32_t>(
            identity + 0x30).value_or(0u);

        if ((flags & 0x1u) != 0)
        {
            constexpr std::uint32_t serial_step = 1u << 15;
            if (handle < serial_step) return 0;
            handle -= serial_step;
        }

        return handle;
    }

    int __cdecl entity_get_abs_origin(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref || !current_entity(*ref))
            return push_nil(*api, state);

        math::vector3 value{};
        if (!resolve_scene_vector(ref->ptr, "m_vecAbsOrigin", value))
            return push_nil(*api, state);

        push_constructor3(
            *api, state, "vec3_t", value.x, value.y, value.z);
        return 1;
    }

    int __cdecl entity_get_abs_rotation(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref || !current_entity(*ref))
            return push_nil(*api, state);

        math::vector3 value{};
        if (!resolve_scene_vector(ref->ptr, "m_angAbsRotation", value))
            return push_nil(*api, state);

        push_constructor3(
            *api, state, "angle_t", value.x, value.y, value.z);
        return 1;
    }

    int __cdecl entity_get_class_name(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref || !current_entity(*ref))
            return push_nil(*api, state);

        const auto* name = systems::g_entities.get_schema_name(ref->ptr);
        if (!name || !*name)
            return push_nil(*api, state);

        api->lua_pushstring(state, name);
        return 1;
    }

    int __cdecl entity_get_entity_handle(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref || !current_entity(*ref))
            return push_nil(*api, state);

        const auto handle =
            ref->source == entity_source::handle
                ? ref->handle
                : derive_entity_handle(ref->ptr);

        if (!handle) return push_nil(*api, state);
        api->lua_pushinteger(state, static_cast<std::ptrdiff_t>(handle));
        return 1;
    }

    int __cdecl entity_tostring(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref)
            return push_nil(*api, state);

        std::array<char, 192> buffer{};
        const auto* name =
            current_entity(*ref)
                ? systems::g_entities.get_schema_name(ref->ptr)
                : nullptr;

        const auto count = std::snprintf(
            buffer.data(), buffer.size(),
            "base_entity_t(%s, 0x%llx)",
            name && *name ? name : "invalid",
            static_cast<unsigned long long>(ref->ptr));

        if (count <= 0)
            return push_nil(*api, state);

        const auto length = static_cast<std::size_t>(
            std::min<int>(count, static_cast<int>(buffer.size() - 1)));
        api->lua_pushlstring(state, buffer.data(), length);
        return 1;
    }

    int __cdecl entity_eq(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* lhs = get_entity(*api, state, 1);
        const auto* rhs = get_entity(*api, state, 2);
        api->lua_pushboolean(
            state,
            lhs && rhs && lhs->ptr != 0 && lhs->ptr == rhs->ptr);
        return 1;
    }

    int __cdecl entity_index(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto* ref = get_entity(*api, state, 1);
        if (!ref || !current_entity(*ref))
            return push_nil(*api, state);

        const auto key_type = api->lua_type(state, 2);
        if (key_type == lua_tnumber)
        {
            const auto offset = api->lua_tointeger(state, 2);
            std::uintptr_t address{};
            if (!checked_address(ref->ptr, offset, address))
                return push_nil(*api, state);

            api->lua_pushlightuserdata(
                state, reinterpret_cast<void*>(address));
            return 1;
        }

        if (key_type != lua_tstring)
            return push_nil(*api, state);

        std::string_view key{};
        if (!get_lua_string(*api, state, 2, key))
            return push_nil(*api, state);

        nix_lua_cfunction function{};
        if (key == "get_abs_origin")
            function = &entity_get_abs_origin;
        else if (key == "get_abs_rotation")
            function = &entity_get_abs_rotation;
        else if (key == "get_class_name")
            function = &entity_get_class_name;
        else if (key == "get_entity_handle")
            function = &entity_get_entity_handle;
        else
            return push_nil(*api, state);

        api->lua_pushcclosure(state, function, 0);
        return 1;
    }

    int __cdecl entitylist_local_pawn(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto local = systems::g_local.get();
        push_entity(
            *api, state, local.pawn, entity_source::local_pawn);
        return 1;
    }

    int __cdecl entitylist_local_controller(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto local = systems::g_local.get();
        push_entity(
            *api, state, local.controller,
            entity_source::local_controller);
        return 1;
    }

    int __cdecl entitylist_from_handle(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        if (api->lua_type(state, 1) != lua_tnumber)
            return push_nil(*api, state);

        const auto raw = api->lua_tointeger(state, 1);
        if (raw <= 0 ||
            static_cast<std::uint64_t>(raw) >
                std::numeric_limits<std::uint32_t>::max())
            return push_nil(*api, state);

        const auto handle = static_cast<std::uint32_t>(raw);
        const auto entity = systems::g_entities.lookup(handle);
        push_entity(
            *api, state, entity, entity_source::handle, handle);
        return 1;
    }

    int __cdecl engine_get_netvar_offset(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        std::string_view module{}, table{}, property{};
        if (!get_lua_string(*api, state, 1, module) ||
            !get_lua_string(*api, state, 2, table) ||
            !get_lua_string(*api, state, 3, property))
            return push_nil(*api, state);

        // Current MCB schema resolver is explicitly scoped to client.dll.
        // Do not fabricate offsets for another module.
        if (_stricmp(
                std::string(module).c_str(), "client.dll") != 0)
            return push_nil(*api, state);

        const auto offset = systems::schemas::try_lookup(
            std::string(table).c_str(),
            fnv1a::runtime_hash(std::string(property).c_str()));

        if (!offset)
            return push_nil(*api, state);

        api->lua_pushinteger(
            state, static_cast<std::ptrdiff_t>(*offset));
        return 1;
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
    bool install_entity_api(
        luajit_api& api,
        lua_State* state,
        std::string& error)
    {
        if (!state)
        {
            error = "entity bridge received null LuaJIT state";
            return false;
        }

        {
            std::scoped_lock lock(g_binding_mutex);
            if (!g_bindings.emplace(
                    state, state_binding{&api}).second)
            {
                error = "entity bridge already attached to this state";
                return false;
            }
        }

        const auto top = api.lua_gettop(state);

        // base_entity_t userdata metatable.
        api.luaL_newmetatable(state, entity_metatable);
        set_c_function(
            api, state, -2, "__index", &entity_index);
        set_c_function(
            api, state, -2, "__eq", &entity_eq);
        set_c_function(
            api, state, -2, "__tostring", &entity_tostring);
        api.lua_settop(state, top);

        // entitylist table. Only native-backed functions are published.
        api.lua_createtable(state, 0, 3);
        set_c_function(
            api, state, -2,
            "get_local_player_pawn", &entitylist_local_pawn);
        set_c_function(
            api, state, -2,
            "get_local_player_controller", &entitylist_local_controller);
        set_c_function(
            api, state, -2,
            "get_entity_from_handle", &entitylist_from_handle);
        api.lua_setfield(state, lua_globals_index, "entitylist");

        // engine table currently publishes only the schema function proven
        // to have a native resolver. Other Nixware engine functions remain
        // absent until backed by real game-side behavior.
        api.lua_createtable(state, 0, 1);
        set_c_function(
            api, state, -2,
            "get_netvar_offset", &engine_get_netvar_offset);
        api.lua_setfield(state, lua_globals_index, "engine");

        api.lua_settop(state, top);
        return true;
    }

    void detach_entity_api(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_binding_mutex);
        g_bindings.erase(state);
    }
}
