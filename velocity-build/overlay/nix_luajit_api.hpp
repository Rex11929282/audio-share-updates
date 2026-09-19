#pragma once

#include <cstddef>
#include <cstdint>
#include <windows.h>

struct lua_State;
using nix_lua_cfunction = int (__cdecl*)(lua_State*);

namespace scripting::nix_native
{
    inline constexpr int lua_registry_index = -10000;
    inline constexpr int lua_globals_index = -10002;

    inline constexpr int lua_tnil = 0;
    inline constexpr int lua_tboolean = 1;
    inline constexpr int lua_tlightuserdata = 2;
    inline constexpr int lua_tnumber = 3;
    inline constexpr int lua_tstring = 4;
    inline constexpr int lua_ttable = 5;
    inline constexpr int lua_tfunction = 6;
    inline constexpr int lua_tuserdata = 7;

    struct luajit_api
    {
        lua_State* (__cdecl* luaL_newstate)(){};
        void (__cdecl* lua_close)(lua_State*){};
        void (__cdecl* luaL_openlibs)(lua_State*){};
        int (__cdecl* luaL_loadbuffer)(lua_State*, const char*, std::size_t, const char*){};
        int (__cdecl* lua_pcall)(lua_State*, int, int, int){};

        int (__cdecl* lua_gettop)(lua_State*){};
        void (__cdecl* lua_settop)(lua_State*, int){};
        int (__cdecl* lua_type)(lua_State*, int){};
        const char* (__cdecl* lua_tolstring)(lua_State*, int, std::size_t*){};
        std::ptrdiff_t (__cdecl* lua_tointeger)(lua_State*, int){};
        double (__cdecl* lua_tonumber)(lua_State*, int){};
        int (__cdecl* lua_toboolean)(lua_State*, int){};
        void* (__cdecl* lua_touserdata)(lua_State*, int){};

        void (__cdecl* lua_getfield)(lua_State*, int, const char*){};
        void (__cdecl* lua_setfield)(lua_State*, int, const char*){};
        void (__cdecl* lua_createtable)(lua_State*, int, int){};
        void* (__cdecl* lua_newuserdata)(lua_State*, std::size_t){};
        int (__cdecl* lua_setmetatable)(lua_State*, int){};

        void (__cdecl* lua_pushnil)(lua_State*){};
        void (__cdecl* lua_pushnumber)(lua_State*, double){};
        void (__cdecl* lua_pushinteger)(lua_State*, std::ptrdiff_t){};
        const char* (__cdecl* lua_pushlstring)(lua_State*, const char*, std::size_t){};
        const char* (__cdecl* lua_pushstring)(lua_State*, const char*){};
        void (__cdecl* lua_pushboolean)(lua_State*, int){};
        void (__cdecl* lua_pushlightuserdata)(lua_State*, void*){};
        void (__cdecl* lua_pushcclosure)(lua_State*, nix_lua_cfunction, int){};
        int (__cdecl* lua_error)(lua_State*){};

        int (__cdecl* luaL_newmetatable)(lua_State*, const char*){};

        template <typename T>
        static bool bind_one(HMODULE module, T& out, const char* name)
        {
            out = reinterpret_cast<T>(GetProcAddress(module, name));
            return out != nullptr;
        }

        bool bind(HMODULE module)
        {
#define MCB_NIX_BIND(name) bind_one(module, name, #name)
            return
                MCB_NIX_BIND(luaL_newstate) &&
                MCB_NIX_BIND(lua_close) &&
                MCB_NIX_BIND(luaL_openlibs) &&
                MCB_NIX_BIND(luaL_loadbuffer) &&
                MCB_NIX_BIND(lua_pcall) &&
                MCB_NIX_BIND(lua_gettop) &&
                MCB_NIX_BIND(lua_settop) &&
                MCB_NIX_BIND(lua_type) &&
                MCB_NIX_BIND(lua_tolstring) &&
                MCB_NIX_BIND(lua_tointeger) &&
                MCB_NIX_BIND(lua_tonumber) &&
                MCB_NIX_BIND(lua_toboolean) &&
                MCB_NIX_BIND(lua_touserdata) &&
                MCB_NIX_BIND(lua_getfield) &&
                MCB_NIX_BIND(lua_setfield) &&
                MCB_NIX_BIND(lua_createtable) &&
                MCB_NIX_BIND(lua_newuserdata) &&
                MCB_NIX_BIND(lua_setmetatable) &&
                MCB_NIX_BIND(lua_pushnil) &&
                MCB_NIX_BIND(lua_pushnumber) &&
                MCB_NIX_BIND(lua_pushinteger) &&
                MCB_NIX_BIND(lua_pushlstring) &&
                MCB_NIX_BIND(lua_pushstring) &&
                MCB_NIX_BIND(lua_pushboolean) &&
                MCB_NIX_BIND(lua_pushlightuserdata) &&
                MCB_NIX_BIND(lua_pushcclosure) &&
                MCB_NIX_BIND(lua_error) &&
                MCB_NIX_BIND(luaL_newmetatable);
#undef MCB_NIX_BIND
        }
    };

    inline void lua_pop(luajit_api& api, lua_State* L, int count)
    {
        api.lua_settop(L, -count - 1);
    }
}
