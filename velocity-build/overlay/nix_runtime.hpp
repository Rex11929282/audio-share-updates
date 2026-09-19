#pragma once

#include <filesystem>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace scripting
{
    struct nix_script_status
    {
        std::string name{};
        bool loaded{};
        bool suspended{};
        std::string last_error{};
    };

    // Dedicated LuaJIT runtime for unmodified Nixware scripts.
    // The legacy MCB Lua runtime remains separate and is not handed a LuaJIT state.
    class nix_runtime
    {
    public:
        nix_runtime();
        ~nix_runtime();

        nix_runtime(const nix_runtime&) = delete;
        nix_runtime& operator=(const nix_runtime&) = delete;

        bool initialize(void* module_handle);
        void shutdown();
        void on_frame();
        void on_override_view(std::uintptr_t view_setup);
        void on_game_event(
            const char* event_name,
            std::uintptr_t event);
        void reload_all();
        bool import_script_dialog(void* owner_window);

        [[nodiscard]] bool ready() const;
        [[nodiscard]] const std::filesystem::path& script_directory() const;
        [[nodiscard]] std::vector<nix_script_status> script_statuses() const;

    private:
        struct impl;
        std::unique_ptr<impl> m_impl;
    };

    inline nix_runtime g_nix{};
}
