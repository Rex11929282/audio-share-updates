#pragma once

#include <string_view>

namespace scripting::nix_bootstrap
{
    inline constexpr std::string_view render_api = R"MCB_NIX_RENDER(
local host_screen_size = __mcb_render_screen_size
local host_frame_count = __mcb_render_frame_count
local host_frame_time = __mcb_render_frame_time
local host_world_to_screen = __mcb_render_world_to_screen
local host_setup_font = __mcb_render_setup_font
local host_calc_text_size = __mcb_render_calc_text_size
local host_text = __mcb_render_text
local host_line = __mcb_render_line
local host_rect = __mcb_render_rect
local host_rect_filled = __mcb_render_rect_filled
local host_rect_filled_fade = __mcb_render_rect_filled_fade
local host_circle = __mcb_render_circle
local host_circle_filled = __mcb_render_circle_filled
local host_push_clip = __mcb_render_push_clip
local host_pop_clip = __mcb_render_pop_clip

__mcb_render_screen_size = nil
__mcb_render_frame_count = nil
__mcb_render_frame_time = nil
__mcb_render_world_to_screen = nil
__mcb_render_setup_font = nil
__mcb_render_calc_text_size = nil
__mcb_render_text = nil
__mcb_render_line = nil
__mcb_render_rect = nil
__mcb_render_rect_filled = nil
__mcb_render_rect_filled_fade = nil
__mcb_render_circle = nil
__mcb_render_circle_filled = nil
__mcb_render_push_clip = nil
__mcb_render_pop_clip = nil

render = render or {}

local function rgba(c)
    if c == nil then return 1, 1, 1, 1 end
    assert(type(c) == "cdata", "color_t expected")
    return c.r, c.g, c.b, c.a
end

function render.screen_size()
    local w, h = host_screen_size()
    return vec2_t(w, h)
end

function render.frame_count()
    return host_frame_count()
end

function render.frame_time()
    return host_frame_time()
end

function render.world_to_screen(pos)
    assert(type(pos) == "cdata", "vec3_t expected")
    local x, y = host_world_to_screen(pos.x, pos.y, pos.z)
    if x == nil then return nil end
    return vec2_t(x, y)
end

function render.setup_font(filename, size, flags)
    assert(type(filename) == "string", "font filename string expected")
    assert(type(size) == "number", "font size number expected")
    return host_setup_font(filename, size, flags or 0)
end

function render.calc_text_size(text, font, size)
    assert(type(text) == "string", "text string expected")
    assert(type(font) == "userdata", "font_t userdata expected")
    local w, h = host_calc_text_size(text, font, size)
    if w == nil then return nil end
    return vec2_t(w, h)
end

function render.text(text, font, pos, color, size)
    assert(type(text) == "string", "text string expected")
    assert(type(font) == "userdata", "font_t userdata expected")
    assert(type(pos) == "cdata", "vec2_t expected")
    local r, g, b, a = rgba(color)
    host_text(
        text, font, pos.x, pos.y,
        r, g, b, a,
        size)
end

function render.line(from, to, color, thickness)
    local r, g, b, a = rgba(color)
    host_line(
        from.x, from.y, to.x, to.y,
        r, g, b, a,
        thickness or 1)
end

function render.rect(from, to, color, rounding, thickness)
    local r, g, b, a = rgba(color)
    host_rect(
        from.x, from.y, to.x, to.y,
        r, g, b, a,
        rounding or 0,
        thickness or 1)
end

function render.rect_filled(from, to, color, rounding)
    local r, g, b, a = rgba(color)
    host_rect_filled(
        from.x, from.y, to.x, to.y,
        r, g, b, a,
        rounding or 0)
end

function render.rect_filled_fade(from, to, tl, tr, br, bl)
    local r1,g1,b1,a1 = rgba(tl)
    local r2,g2,b2,a2 = rgba(tr)
    local r3,g3,b3,a3 = rgba(br)
    local r4,g4,b4,a4 = rgba(bl)
    host_rect_filled_fade(
        from.x, from.y, to.x, to.y,
        r1,g1,b1,a1,
        r2,g2,b2,a2,
        r3,g3,b3,a3,
        r4,g4,b4,a4)
end

function render.circle(pos, radius, segments, color, thickness)
    local r, g, b, a = rgba(color)
    host_circle(
        pos.x, pos.y,
        radius,
        segments or 0,
        r, g, b, a,
        thickness or 1)
end

function render.circle_filled(pos, radius, segments, color)
    local r, g, b, a = rgba(color)
    host_circle_filled(
        pos.x, pos.y,
        radius,
        segments or 0,
        r, g, b, a)
end

function render.push_clip_rect(from, to, intersect)
    host_push_clip(
        from.x, from.y, to.x, to.y,
        intersect == nil and true or not not intersect)
end

function render.pop_clip_rect()
    host_pop_clip()
end
)MCB_NIX_RENDER";
}
