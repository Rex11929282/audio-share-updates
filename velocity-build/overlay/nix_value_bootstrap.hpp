#pragma once

#include <string_view>

namespace scripting::nix_bootstrap
{
    // Host-internal LuaJIT bootstrap. This is not a user script and is never
    // copied into the user's script directory. It creates real FFI cdata
    // values for the documented Nixware value-type surface.
    inline constexpr std::string_view value_types = R"MCB_NIX(
local ffi = require("ffi")
local sqrt, sin, cos, atan2 = math.sqrt, math.sin, math.cos, math.atan2
local rad, deg = math.rad, math.deg

ffi.cdef[[
typedef struct { float x, y; } mcb_nix_vec2;
typedef struct { float x, y, z; } mcb_nix_vec3;
typedef struct { float x, y, z, w; } mcb_nix_vec4;
typedef struct { float pitch, yaw, roll; } mcb_nix_angle;
typedef struct { float r, g, b, a; } mcb_nix_color;
]]

local Vec2, Vec3, Vec4, Angle, Color

local function is_scalar(v)
    return type(v) == "number"
end

local function assert_type(t, v, label)
    if not ffi.istype(t, v) then
        error("expected " .. label, 3)
    end
end

local vec2_methods = {}
local vec2_mt = {
    __index = vec2_methods,
    __tostring = function(v)
        return string.format("vec2_t(%g, %g)", v.x, v.y)
    end,
    __add = function(a, b)
        assert_type(Vec2, a, "vec2_t")
        assert_type(Vec2, b, "vec2_t")
        return Vec2(a.x + b.x, a.y + b.y)
    end,
    __sub = function(a, b)
        assert_type(Vec2, a, "vec2_t")
        assert_type(Vec2, b, "vec2_t")
        return Vec2(a.x - b.x, a.y - b.y)
    end,
    __unm = function(v)
        return Vec2(-v.x, -v.y)
    end,
    __mul = function(a, b)
        if ffi.istype(Vec2, a) and is_scalar(b) then
            return Vec2(a.x * b, a.y * b)
        elseif is_scalar(a) and ffi.istype(Vec2, b) then
            return Vec2(a * b.x, a * b.y)
        end
        error("vec2_t multiplication requires a scalar", 2)
    end,
    __div = function(a, b)
        assert_type(Vec2, a, "vec2_t")
        if not is_scalar(b) or b == 0 then
            error("vec2_t division requires a non-zero scalar", 2)
        end
        return Vec2(a.x / b, a.y / b)
    end,
    __eq = function(a, b)
        return ffi.istype(Vec2, a) and ffi.istype(Vec2, b)
            and a.x == b.x and a.y == b.y
    end
}
Vec2 = ffi.metatype("mcb_nix_vec2", vec2_mt)

local vec3_methods = {}
local vec3_mt = {
    __index = vec3_methods,
    __tostring = function(v)
        return string.format("vec3_t(%g, %g, %g)", v.x, v.y, v.z)
    end,
    __add = function(a, b)
        assert_type(Vec3, a, "vec3_t")
        assert_type(Vec3, b, "vec3_t")
        return Vec3(a.x + b.x, a.y + b.y, a.z + b.z)
    end,
    __sub = function(a, b)
        assert_type(Vec3, a, "vec3_t")
        assert_type(Vec3, b, "vec3_t")
        return Vec3(a.x - b.x, a.y - b.y, a.z - b.z)
    end,
    __unm = function(v)
        return Vec3(-v.x, -v.y, -v.z)
    end,
    __mul = function(a, b)
        if ffi.istype(Vec3, a) and is_scalar(b) then
            return Vec3(a.x * b, a.y * b, a.z * b)
        elseif is_scalar(a) and ffi.istype(Vec3, b) then
            return Vec3(a * b.x, a * b.y, a * b.z)
        end
        error("vec3_t multiplication requires a scalar", 2)
    end,
    __div = function(a, b)
        assert_type(Vec3, a, "vec3_t")
        if not is_scalar(b) or b == 0 then
            error("vec3_t division requires a non-zero scalar", 2)
        end
        return Vec3(a.x / b, a.y / b, a.z / b)
    end,
    __eq = function(a, b)
        return ffi.istype(Vec3, a) and ffi.istype(Vec3, b)
            and a.x == b.x and a.y == b.y and a.z == b.z
    end
}
Vec3 = ffi.metatype("mcb_nix_vec3", vec3_mt)

function vec3_methods:lerp(other, fraction)
    assert_type(Vec3, other, "vec3_t")
    return Vec3(
        self.x + (other.x - self.x) * fraction,
        self.y + (other.y - self.y) * fraction,
        self.z + (other.z - self.z) * fraction)
end

function vec3_methods:length_2d_sqr()
    return self.x * self.x + self.y * self.y
end

function vec3_methods:length_sqr()
    return self.x * self.x + self.y * self.y + self.z * self.z
end

function vec3_methods:length_2d()
    return sqrt(self:length_2d_sqr())
end

function vec3_methods:length()
    return sqrt(self:length_sqr())
end

function vec3_methods:dist_to_2d(other)
    assert_type(Vec3, other, "vec3_t")
    local dx, dy = self.x - other.x, self.y - other.y
    return sqrt(dx * dx + dy * dy)
end

function vec3_methods:dist_to(other)
    assert_type(Vec3, other, "vec3_t")
    local dx, dy, dz = self.x - other.x, self.y - other.y, self.z - other.z
    return sqrt(dx * dx + dy * dy + dz * dz)
end

function vec3_methods:dot(other)
    assert_type(Vec3, other, "vec3_t")
    return self.x * other.x + self.y * other.y + self.z * other.z
end

function vec3_methods:cross(other)
    assert_type(Vec3, other, "vec3_t")
    return Vec3(
        self.y * other.z - self.z * other.y,
        self.z * other.x - self.x * other.z,
        self.x * other.y - self.y * other.x)
end

function vec3_methods:normalized()
    local length = self:length()
    if length <= 0 then return Vec3(0, 0, 0) end
    return Vec3(self.x / length, self.y / length, self.z / length)
end

function vec3_methods:normalize()
    local length = self:length()
    if length > 0 then
        self.x, self.y, self.z =
            self.x / length, self.y / length, self.z / length
    end
    return length
end

local vec4_mt = {
    __tostring = function(v)
        return string.format("vec4_t(%g, %g, %g, %g)", v.x, v.y, v.z, v.w)
    end,
    __add = function(a, b)
        assert_type(Vec4, a, "vec4_t")
        assert_type(Vec4, b, "vec4_t")
        return Vec4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w)
    end,
    __sub = function(a, b)
        assert_type(Vec4, a, "vec4_t")
        assert_type(Vec4, b, "vec4_t")
        return Vec4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w)
    end,
    __mul = function(a, b)
        if ffi.istype(Vec4, a) and is_scalar(b) then
            return Vec4(a.x*b, a.y*b, a.z*b, a.w*b)
        elseif is_scalar(a) and ffi.istype(Vec4, b) then
            return Vec4(a*b.x, a*b.y, a*b.z, a*b.w)
        end
        error("vec4_t multiplication requires a scalar", 2)
    end
}
Vec4 = ffi.metatype("mcb_nix_vec4", vec4_mt)

local angle_mt = {
    __tostring = function(v)
        return string.format("angle_t(%g, %g, %g)", v.pitch, v.yaw, v.roll)
    end,
    __add = function(a, b)
        assert_type(Angle, a, "angle_t")
        assert_type(Angle, b, "angle_t")
        return Angle(a.pitch+b.pitch, a.yaw+b.yaw, a.roll+b.roll)
    end,
    __sub = function(a, b)
        assert_type(Angle, a, "angle_t")
        assert_type(Angle, b, "angle_t")
        return Angle(a.pitch-b.pitch, a.yaw-b.yaw, a.roll-b.roll)
    end
}
Angle = ffi.metatype("mcb_nix_angle", angle_mt)

local color_methods = {}
local color_mt = {
    __index = color_methods,
    __tostring = function(v)
        return string.format("color_t(%g, %g, %g, %g)", v.r, v.g, v.b, v.a)
    end,
    __eq = function(a, b)
        return ffi.istype(Color, a) and ffi.istype(Color, b)
            and a.r == b.r and a.g == b.g and a.b == b.b and a.a == b.a
    end
}
Color = ffi.metatype("mcb_nix_color", color_mt)

function color_methods:lerp(other, fraction)
    assert_type(Color, other, "color_t")
    return Color(
        self.r + (other.r - self.r) * fraction,
        self.g + (other.g - self.g) * fraction,
        self.b + (other.b - self.b) * fraction,
        self.a + (other.a - self.a) * fraction)
end

function vec2_t(x, y) return Vec2(x, y) end
function vec3_t(x, y, z) return Vec3(x, y, z) end
function vec4_t(x, y, z, w) return Vec4(x, y, z, w) end
function angle_t(pitch, yaw, roll) return Angle(pitch, yaw, roll) end
function color_t(r, g, b, a) return Color(r, g, b, a) end

local function normalize_angle_impl(a)
    a = a % 360
    if a > 180 then a = a - 360 end
    return a
end

math.normalize_angle = normalize_angle_impl

math.vector_angles = function(forward)
    assert_type(Vec3, forward, "vec3_t")
    local horizontal = sqrt(forward.x * forward.x + forward.y * forward.y)
    return Angle(
        deg(atan2(-forward.z, horizontal)),
        deg(atan2(forward.y, forward.x)),
        0)
end

math.calc_angle = function(src, dst)
    assert_type(Vec3, src, "vec3_t")
    assert_type(Vec3, dst, "vec3_t")
    return math.vector_angles(dst - src)
end

math.calc_fov = function(src, dst)
    assert_type(Angle, src, "angle_t")
    assert_type(Angle, dst, "angle_t")
    local dp = normalize_angle_impl(dst.pitch - src.pitch)
    local dy = normalize_angle_impl(dst.yaw - src.yaw)
    return sqrt(dp * dp + dy * dy)
end

math.angle_vectors = function(angles)
    assert_type(Angle, angles, "angle_t")
    local sp, cp = sin(rad(angles.pitch)), cos(rad(angles.pitch))
    local sy, cy = sin(rad(angles.yaw)), cos(rad(angles.yaw))
    local sr, cr = sin(rad(angles.roll)), cos(rad(angles.roll))

    local forward = Vec3(cp * cy, cp * sy, -sp)
    local right = Vec3(
        -sr * sp * cy + cr * sy,
        -sr * sp * sy - cr * cy,
        -sr * cp)
    local up = Vec3(
        cr * sp * cy + sr * sy,
        cr * sp * sy - sr * cy,
        cr * cp)

    return forward, right, up
end

-- Internal conformance checks. These verify that the runtime really created
-- cdata/metatypes; they are not a claim about undocumented representation.
do
    local v = vec3_t(3, 4, 0)
    assert(type(v) == "cdata" and v:length() == 5)
    local original = v:normalize()
    assert(math.abs(original - 5) < 0.0001)
    assert(math.abs(v:length() - 1) < 0.0001)
    local f = math.angle_vectors(angle_t(0, 0, 0))
    assert(math.abs(f.x - 1) < 0.0001 and math.abs(f.y) < 0.0001)
    local c = color_t(0, 0, 0, 1):lerp(color_t(1, 1, 1, 1), 0.5)
    assert(math.abs(c.r - 0.5) < 0.0001)
end
)MCB_NIX";
}
