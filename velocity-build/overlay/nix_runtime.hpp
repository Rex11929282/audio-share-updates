#pragma once

#include <filesystem>
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
        void reload_all();

        [[nodiscard]] bool ready() const;
        [[nodiscard]] const std::filesystem::path& script_directory() const;
        [[nodiscard]] std::vector<nix_script_status> script_statuses() const;

    private:
        struct impl;
        std::unique_ptr<impl> m_impl;
    };

    inline nix_runtime g_nix{};
}
