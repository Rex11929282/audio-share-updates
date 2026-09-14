#pragma once
#include <string_view>

namespace rendering::exact_panel
{
    void draw_regular_page(
        std::string_view id,
        float body_x,
        float body_y,
        float body_w,
        float body_h,
        float menu_x,
        float menu_y );
}
