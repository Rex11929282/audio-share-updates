#include <lua.hpp>
#include <cstdio>

static int g_draw_calls = 0;

static int now_ms(lua_State* L) { lua_pushinteger(L, 1234567); return 1; }
static int screen_size(lua_State* L) { lua_pushinteger(L, 1440); lua_pushinteger(L, 1080); return 2; }
static int log_fn(lua_State*) { return 0; }
static int draw_fn(lua_State*) { ++g_draw_calls; return 0; }

static bool call(lua_State* L, const char* name, bool dt)
{
    lua_getglobal(L, name);
    if (!lua_isfunction(L, -1)) { lua_pop(L, 1); return false; }
    if (dt) lua_pushnumber(L, 1.0 / 60.0);
    if (lua_pcall(L, dt ? 1 : 0, 0, 0) != LUA_OK)
    {
        std::fprintf(stderr, "%s failed: %s\n", name, lua_tostring(L, -1));
        return false;
    }
    return true;
}

int main(int argc, char** argv)
{
    if (argc < 2) return 2;
    lua_State* L = luaL_newstate();
    if (!L) return 3;
    luaL_openlibs(L);

    lua_newtable(L);
    lua_pushcfunction(L, log_fn); lua_setfield(L, -2, "log");
    lua_pushcfunction(L, now_ms); lua_setfield(L, -2, "now_ms");
    lua_pushcfunction(L, screen_size); lua_setfield(L, -2, "screen_size");
    lua_pushcfunction(L, draw_fn); lua_setfield(L, -2, "hud_line");
    lua_pushcfunction(L, draw_fn); lua_setfield(L, -2, "hud_circle");
    lua_pushcfunction(L, draw_fn); lua_setfield(L, -2, "hud_text");
    lua_setglobal(L, "velocity");

    if (luaL_loadfilex(L, argv[1], "t") != LUA_OK || lua_pcall(L, 0, 0, 0) != LUA_OK)
    {
        std::fprintf(stderr, "load failed: %s\n", lua_tostring(L, -1));
        lua_close(L);
        return 4;
    }

    if (!call(L, "on_load", false) || !call(L, "on_frame", true))
    {
        lua_close(L);
        return 5;
    }

    if (g_draw_calls < 50)
    {
        std::fprintf(stderr, "not enough HUD draw calls: %d\n", g_draw_calls);
        lua_close(L);
        return 6;
    }

    std::printf("SNOW_HUD_SMOKE_OK draw_calls=%d\n", g_draw_calls);
    lua_close(L);
    return 0;
}
