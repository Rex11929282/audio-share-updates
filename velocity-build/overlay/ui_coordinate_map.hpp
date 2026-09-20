#pragma once

namespace xui::coord
{
    struct point
    {
        float x{};
        float y{};
    };

    [[nodiscard]] constexpr point client_to_viewport(
        float client_x,
        float client_y,
        float client_w,
        float client_h,
        float viewport_w,
        float viewport_h ) noexcept
    {
        if ( client_w <= 0.0f || client_h <= 0.0f ||
             viewport_w <= 0.0f || viewport_h <= 0.0f )
            return { client_x, client_y };

        return
        {
            client_x * ( viewport_w / client_w ),
            client_y * ( viewport_h / client_h )
        };
    }
}
