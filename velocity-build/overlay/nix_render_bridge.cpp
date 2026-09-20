#include <pch/pch.hpp>

#include "nix_render_bridge.hpp"

#include <core/systems/systems.hpp>
#include "../../external/xdraw/xdraw.hpp"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

namespace
{
    using scripting::nix_native::luajit_api;
    using scripting::nix_native::lua_globals_index;
    using scripting::nix_native::lua_tboolean;
    using scripting::nix_native::lua_tnumber;
    using scripting::nix_native::lua_tstring;
    using scripting::nix_native::lua_tuserdata;
    using ::nix_lua_cfunction;

    struct state_binding
    {
        luajit_api* api{};
        bool draw_allowed{};
        int clip_depth{};
    };

    struct font_resource
    {
        std::vector<std::byte> bytes{};
        float base_size{12.0f};
        std::unordered_map<int, xdraw::font*> variants{};
    };

    struct font_ref
    {
        std::uint64_t magic{};
        font_resource* resource{};
    };

    constexpr std::uint64_t font_magic{
        0x4D43424E49584654ull // "MCBNIXFT"
    };
    constexpr std::size_t max_font_bytes{
        64ull * 1024ull * 1024ull
    };
    constexpr std::size_t max_font_resources{128};

    std::mutex g_mutex{};
    std::unordered_map<lua_State*, state_binding> g_states{};
    std::atomic<std::uint64_t> g_frame_count{};
    std::mutex g_font_mutex{};

    // Intentionally process-lifetime storage. xdraw::load_font creates a
    // FreeType memory face that points into the supplied byte buffer, so
    // those bytes must outlive every xdraw::font using them. A leaked
    // process-lifetime pool also avoids cross-TU static destruction order
    // hazards during DLL/process teardown.
    auto& font_resources()
    {
        static auto* pool =
            new std::vector<std::unique_ptr<font_resource>>();
        return *pool;
    }

    luajit_api* api_for(lua_State* state)
    {
        std::scoped_lock lock(g_mutex);
        const auto it = g_states.find(state);
        return it == g_states.end() ? nullptr : it->second.api;
    }

    luajit_api* api_for_draw(lua_State* state)
    {
        std::scoped_lock lock(g_mutex);
        const auto it = g_states.find(state);
        if (it == g_states.end() || !it->second.draw_allowed)
            return nullptr;
        return it->second.api;
    }

    bool number(
        luajit_api& api,
        lua_State* state,
        int index,
        double& out)
    {
        if (api.lua_type(state, index) != lua_tnumber)
            return false;

        out = api.lua_tonumber(state, index);
        return std::isfinite(out);
    }

    std::uint8_t channel(double value, bool direct_255)
    {
        const auto scaled = direct_255
            ? std::clamp(value, 0.0, 255.0)
            : std::clamp(value, 0.0, 1.0) * 255.0;

        return static_cast<std::uint8_t>(
            std::lround(scaled));
    }

    bool color_from(
        luajit_api& api,
        lua_State* state,
        int first,
        xdraw::color& out)
    {
        double r{}, g{}, b{}, a{};
        if (!number(api, state, first + 0, r) ||
            !number(api, state, first + 1, g) ||
            !number(api, state, first + 2, b) ||
            !number(api, state, first + 3, a))
            return false;

        // Nixware examples commonly use normalized 0..1 colors, while
        // several real user scripts use 0..255 channels. Preserve both:
        // RGB selects direct mode if any RGB channel exceeds 1; alpha is
        // independently accepted as either 0..1 or 0..255.
        const bool rgb_255 =
            std::abs(r) > 1.0 ||
            std::abs(g) > 1.0 ||
            std::abs(b) > 1.0;
        const bool alpha_255 = std::abs(a) > 1.0;

        out = xdraw::color{
            channel(r, rgb_255),
            channel(g, rgb_255),
            channel(b, rgb_255),
            channel(a, alpha_255)
        };
        return true;
    }

    bool string_arg(
        luajit_api& api,
        lua_State* state,
        int index,
        std::string& out,
        std::size_t max_length = 32768)
    {
        if (api.lua_type(state, index) != lua_tstring)
            return false;

        std::size_t length{};
        const auto* raw =
            api.lua_tolstring(state, index, &length);
        if (!raw || length == 0 || length > max_length)
            return false;

        const std::string_view view(raw, length);
        if (view.find('\0') != std::string_view::npos)
            return false;

        out.assign(view);
        return true;
    }

    font_ref* get_font_ref(
        luajit_api& api,
        lua_State* state,
        int index)
    {
        if (api.lua_type(state, index) != lua_tuserdata)
            return nullptr;

        auto* ref = static_cast<font_ref*>(
            api.lua_touserdata(state, index));
        if (!ref || ref->magic != font_magic || !ref->resource)
            return nullptr;
        return ref;
    }

    xdraw::font* resolve_font(
        font_resource& resource,
        float requested_size)
    {
        const auto px = std::clamp(
            std::isfinite(requested_size) && requested_size > 0.0f
                ? requested_size
                : resource.base_size,
            4.0f, 256.0f);
        const auto key = static_cast<int>(
            std::lround(px * 64.0f));

        std::scoped_lock lock(g_font_mutex);
        if (const auto it = resource.variants.find(key);
            it != resource.variants.end())
            return it->second;

        if (!xdraw::device())
            return nullptr;

        auto* font = xdraw::load_font(
            std::span<const std::byte>{
                resource.bytes.data(),
                resource.bytes.size()},
            px,
            2048,
            2048);
        if (!font)
            return nullptr;

        resource.variants.emplace(key, font);
        return font;
    }

    int __cdecl setup_font(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        std::string filename;
        double size{};
        if (!string_arg(*api, state, 1, filename, 32768) ||
            !number(*api, state, 2, size) ||
            size < 4.0 || size > 256.0)
        {
            api->lua_pushnil(state);
            return 1;
        }

        std::error_code ec;
        const auto path =
            std::filesystem::u8path(filename);
        const auto file_size =
            std::filesystem::file_size(path, ec);
        if (ec || file_size == 0 ||
            file_size > max_font_bytes)
        {
            api->lua_pushnil(state);
            return 1;
        }

        std::ifstream file(
            path, std::ios::binary);
        if (!file)
        {
            api->lua_pushnil(state);
            return 1;
        }

        auto resource =
            std::make_unique<font_resource>();
        resource->base_size =
            static_cast<float>(size);
        resource->bytes.resize(
            static_cast<std::size_t>(file_size));
        if (!file.read(
                reinterpret_cast<char*>(
                    resource->bytes.data()),
                static_cast<std::streamsize>(
                    resource->bytes.size())))
        {
            api->lua_pushnil(state);
            return 1;
        }

        font_resource* raw_resource{};
        {
            std::scoped_lock lock(g_font_mutex);
            auto& pool = font_resources();
            if (pool.size() >= max_font_resources)
            {
                api->lua_pushnil(state);
                return 1;
            }
            raw_resource = resource.get();
            pool.push_back(std::move(resource));
        }

        auto* ref = static_cast<font_ref*>(
            api->lua_newuserdata(
                state, sizeof(font_ref)));
        if (!ref)
        {
            api->lua_pushnil(state);
            return 1;
        }

        *ref = font_ref{
            font_magic,
            raw_resource
        };
        return 1;
    }

    int __cdecl calc_text_size(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        std::string value;
        if (!string_arg(*api, state, 1, value, 1 << 20))
        {
            api->lua_pushnil(state);
            return 1;
        }

        auto* ref = get_font_ref(*api, state, 2);
        if (!ref)
        {
            api->lua_pushnil(state);
            return 1;
        }

        double requested{};
        if (api->lua_type(state, 3) == lua_tnumber)
            requested = api->lua_tonumber(state, 3);
        else
            requested = ref->resource->base_size;

        auto* font = resolve_font(
            *ref->resource,
            static_cast<float>(requested));
        if (!font)
        {
            api->lua_pushnil(state);
            return 1;
        }

        const auto [w, h] =
            xdraw::measure_text(value, font);
        api->lua_pushnumber(state, w);
        api->lua_pushnumber(state, h);
        return 2;
    }

    int __cdecl draw_text(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        std::string value;
        if (!string_arg(*api, state, 1, value, 1 << 20))
            return 0;

        auto* ref = get_font_ref(*api, state, 2);
        if (!ref)
            return 0;

        double x{}, y{}, requested{};
        xdraw::color color{};
        if (!number(*api, state, 3, x) ||
            !number(*api, state, 4, y) ||
            !color_from(*api, state, 5, color))
            return 0;

        if (api->lua_type(state, 9) == lua_tnumber)
            requested = api->lua_tonumber(state, 9);
        else
            requested = ref->resource->base_size;

        auto* font = resolve_font(
            *ref->resource,
            static_cast<float>(requested));
        if (!font)
            return 0;

        xdraw::get(xdraw::layer::top).text(
            static_cast<float>(x),
            static_cast<float>(y),
            value,
            color,
            font);
        return 0;
    }

    int __cdecl screen_size(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        const auto [w, h] = xdraw::viewport_size();
        api->lua_pushnumber(state, static_cast<double>(w));
        api->lua_pushnumber(state, static_cast<double>(h));
        return 2;
    }

    int __cdecl frame_count(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        api->lua_pushnumber(
            state,
            static_cast<double>(
                g_frame_count.load(
                    std::memory_order_relaxed)));
        return 1;
    }

    int __cdecl frame_time(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        api->lua_pushnumber(
            state,
            static_cast<double>(xdraw::delta_time()));
        return 1;
    }

    int __cdecl world_to_screen(lua_State* state)
    {
        auto* api = api_for(state);
        if (!api) return 0;

        double x{}, y{}, z{};
        if (!number(*api, state, 1, x) ||
            !number(*api, state, 2, y) ||
            !number(*api, state, 3, z))
        {
            api->lua_pushnil(state);
            return 1;
        }

        const auto projected =
            systems::g_view.project_full(
                math::vector3{
                    static_cast<float>(x),
                    static_cast<float>(y),
                    static_cast<float>(z)
                });

        if (!projected.on_screen ||
            !std::isfinite(projected.screen.x) ||
            !std::isfinite(projected.screen.y))
        {
            api->lua_pushnil(state);
            return 1;
        }

        api->lua_pushnumber(state, projected.screen.x);
        api->lua_pushnumber(state, projected.screen.y);
        return 2;
    }

    int __cdecl line(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x1{}, y1{}, x2{}, y2{}, thickness{};
        xdraw::color color{};
        if (!number(*api, state, 1, x1) ||
            !number(*api, state, 2, y1) ||
            !number(*api, state, 3, x2) ||
            !number(*api, state, 4, y2) ||
            !color_from(*api, state, 5, color) ||
            !number(*api, state, 9, thickness))
            return 0;

        xdraw::get(xdraw::layer::top).line(
            static_cast<float>(x1),
            static_cast<float>(y1),
            static_cast<float>(x2),
            static_cast<float>(y2),
            color,
            std::clamp(
                static_cast<float>(thickness),
                0.1f, 64.0f),
            true);
        return 0;
    }

    int __cdecl rect(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x1{}, y1{}, x2{}, y2{};
        double rounding{}, thickness{};
        xdraw::color color{};
        if (!number(*api, state, 1, x1) ||
            !number(*api, state, 2, y1) ||
            !number(*api, state, 3, x2) ||
            !number(*api, state, 4, y2) ||
            !color_from(*api, state, 5, color) ||
            !number(*api, state, 9, rounding) ||
            !number(*api, state, 10, thickness))
            return 0;

        const auto x = static_cast<float>(
            std::min(x1, x2));
        const auto y = static_cast<float>(
            std::min(y1, y2));
        const auto w = static_cast<float>(
            std::abs(x2 - x1));
        const auto h = static_cast<float>(
            std::abs(y2 - y1));

        xdraw::get(xdraw::layer::top).rect(
            x, y, w, h, color,
            xdraw::corner_radius{
                std::clamp(
                    static_cast<float>(rounding),
                    0.0f, 256.0f)
            },
            std::clamp(
                static_cast<float>(thickness),
                0.1f, 64.0f),
            true);
        return 0;
    }

    int __cdecl rect_filled(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x1{}, y1{}, x2{}, y2{}, rounding{};
        xdraw::color color{};
        if (!number(*api, state, 1, x1) ||
            !number(*api, state, 2, y1) ||
            !number(*api, state, 3, x2) ||
            !number(*api, state, 4, y2) ||
            !color_from(*api, state, 5, color) ||
            !number(*api, state, 9, rounding))
            return 0;

        const auto x = static_cast<float>(
            std::min(x1, x2));
        const auto y = static_cast<float>(
            std::min(y1, y2));
        const auto w = static_cast<float>(
            std::abs(x2 - x1));
        const auto h = static_cast<float>(
            std::abs(y2 - y1));

        xdraw::get(xdraw::layer::top).rect_filled(
            x, y, w, h, color,
            xdraw::corner_radius{
                std::clamp(
                    static_cast<float>(rounding),
                    0.0f, 256.0f)
            },
            true);
        return 0;
    }

    int __cdecl rect_filled_fade(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x1{}, y1{}, x2{}, y2{};
        xdraw::color tl{}, tr{}, br{}, bl{};
        if (!number(*api, state, 1, x1) ||
            !number(*api, state, 2, y1) ||
            !number(*api, state, 3, x2) ||
            !number(*api, state, 4, y2) ||
            !color_from(*api, state, 5, tl) ||
            !color_from(*api, state, 9, tr) ||
            !color_from(*api, state, 13, br) ||
            !color_from(*api, state, 17, bl))
            return 0;

        const auto x = static_cast<float>(
            std::min(x1, x2));
        const auto y = static_cast<float>(
            std::min(y1, y2));
        const auto w = static_cast<float>(
            std::abs(x2 - x1));
        const auto h = static_cast<float>(
            std::abs(y2 - y1));

        xdraw::get(
            xdraw::layer::top).rect_filled_gradient(
                x, y, w, h, tl, tr, br, bl);
        return 0;
    }

    int __cdecl circle(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x{}, y{}, radius{}, segments{}, thickness{};
        xdraw::color color{};
        if (!number(*api, state, 1, x) ||
            !number(*api, state, 2, y) ||
            !number(*api, state, 3, radius) ||
            !number(*api, state, 4, segments) ||
            !color_from(*api, state, 5, color) ||
            !number(*api, state, 9, thickness))
            return 0;

        xdraw::get(xdraw::layer::top).circle(
            static_cast<float>(x),
            static_cast<float>(y),
            std::clamp(
                static_cast<float>(radius),
                0.0f, 10000.0f),
            color,
            std::clamp(
                static_cast<float>(thickness),
                0.1f, 64.0f),
            std::clamp(
                static_cast<int>(segments),
                0, 4096),
            true);
        return 0;
    }

    int __cdecl circle_filled(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x{}, y{}, radius{}, segments{};
        xdraw::color color{};
        if (!number(*api, state, 1, x) ||
            !number(*api, state, 2, y) ||
            !number(*api, state, 3, radius) ||
            !number(*api, state, 4, segments) ||
            !color_from(*api, state, 5, color))
            return 0;

        xdraw::get(xdraw::layer::top).circle_filled(
            static_cast<float>(x),
            static_cast<float>(y),
            std::clamp(
                static_cast<float>(radius),
                0.0f, 10000.0f),
            color,
            std::clamp(
                static_cast<int>(segments),
                0, 4096),
            true);
        return 0;
    }

    int __cdecl push_clip(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        double x1{}, y1{}, x2{}, y2{};
        if (!number(*api, state, 1, x1) ||
            !number(*api, state, 2, y1) ||
            !number(*api, state, 3, x2) ||
            !number(*api, state, 4, y2))
            return 0;

        const auto intersect =
            api->lua_type(state, 5) == lua_tboolean
                ? api->lua_toboolean(state, 5) != 0
                : true;

        const auto x = static_cast<float>(
            std::min(x1, x2));
        const auto y = static_cast<float>(
            std::min(y1, y2));
        const auto w = static_cast<float>(
            std::abs(x2 - x1));
        const auto h = static_cast<float>(
            std::abs(y2 - y1));

        auto& draw = xdraw::get(xdraw::layer::top);
        if (intersect)
            draw.push_clip(x, y, w, h);
        else
            draw.push_clip_absolute(x, y, w, h);

        {
            std::scoped_lock lock(g_mutex);
            const auto it = g_states.find(state);
            if (it != g_states.end() && it->second.draw_allowed)
                ++it->second.clip_depth;
        }
        return 0;
    }

    int __cdecl pop_clip(lua_State* state)
    {
        auto* api = api_for_draw(state);
        if (!api) return 0;

        bool may_pop{};
        {
            std::scoped_lock lock(g_mutex);
            const auto it = g_states.find(state);
            if (it != g_states.end() &&
                it->second.draw_allowed &&
                it->second.clip_depth > 0)
            {
                --it->second.clip_depth;
                may_pop = true;
            }
        }

        if (may_pop)
            xdraw::get(
                xdraw::layer::top).pop_clip();
        return 0;
    }

    void set_global(
        luajit_api& api,
        lua_State* state,
        const char* name,
        nix_lua_cfunction fn)
    {
        api.lua_pushcclosure(state, fn, 0);
        api.lua_setfield(
            state, lua_globals_index, name);
    }
}

namespace scripting::nix_native
{
    bool install_render_api(
        luajit_api& api,
        lua_State* state,
        std::string& error)
    {
        if (!state)
        {
            error = "render bridge received null LuaJIT state";
            return false;
        }

        {
            std::scoped_lock lock(g_mutex);
            if (!g_states.emplace(
                    state, state_binding{&api}).second)
            {
                error = "render bridge already attached to this state";
                return false;
            }
        }

        set_global(
            api, state,
            "__mcb_render_screen_size", &screen_size);
        set_global(
            api, state,
            "__mcb_render_frame_count", &frame_count);
        set_global(
            api, state,
            "__mcb_render_frame_time", &frame_time);
        set_global(
            api, state,
            "__mcb_render_world_to_screen", &world_to_screen);
        set_global(
            api, state,
            "__mcb_render_setup_font", &setup_font);
        set_global(
            api, state,
            "__mcb_render_calc_text_size", &calc_text_size);
        set_global(
            api, state,
            "__mcb_render_text", &draw_text);
        set_global(
            api, state,
            "__mcb_render_line", &line);
        set_global(
            api, state,
            "__mcb_render_rect", &rect);
        set_global(
            api, state,
            "__mcb_render_rect_filled", &rect_filled);
        set_global(
            api, state,
            "__mcb_render_rect_filled_fade", &rect_filled_fade);
        set_global(
            api, state,
            "__mcb_render_circle", &circle);
        set_global(
            api, state,
            "__mcb_render_circle_filled", &circle_filled);
        set_global(
            api, state,
            "__mcb_render_push_clip", &push_clip);
        set_global(
            api, state,
            "__mcb_render_pop_clip", &pop_clip);
        return true;
    }

    void detach_render_api(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_mutex);
        g_states.erase(state);
    }

    void advance_render_frame()
    {
        g_frame_count.fetch_add(
            1, std::memory_order_relaxed);
    }

    void begin_render_scope(lua_State* state)
    {
        if (!state) return;
        std::scoped_lock lock(g_mutex);
        const auto it = g_states.find(state);
        if (it == g_states.end()) return;
        it->second.draw_allowed = true;
        it->second.clip_depth = 0;
    }

    void end_render_scope(lua_State* state)
    {
        if (!state) return;

        int clips{};
        {
            std::scoped_lock lock(g_mutex);
            const auto it = g_states.find(state);
            if (it == g_states.end()) return;
            clips = std::max(0, it->second.clip_depth);
            it->second.clip_depth = 0;
            it->second.draw_allowed = false;
        }

        auto& draw = xdraw::get(xdraw::layer::top);
        while (clips-- > 0)
            draw.pop_clip();
    }
}
