#pragma once

#include <string>

namespace mcb::presets
{
    enum class kind
    {
        legit,
        rage,
        hvh
    };

    struct result
    {
        bool success{};
        bool saved{};
        bool rollback_ok{true};
        std::string message{};
    };

    [[nodiscard]] const wchar_t* registry_name(kind value) noexcept;
    [[nodiscard]] result apply_and_save(kind value);
}
