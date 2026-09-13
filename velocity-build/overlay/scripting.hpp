#pragma once

#include <atomic>
#include <chrono>
#include <cstddef>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

struct lua_State;

namespace scripting
{
    struct lua_script_status
    {
        std::string name{};
        bool loaded{};
        bool suspended{};
        std::string last_error{};
    };

    class lua_manager
    {
    public:
        bool initialize( void* module_handle );
        void shutdown( );
        void on_frame( );
        void reload_all( );

        // GUI helpers. These only manage .lua files in the sandboxed scripts folder.
        bool import_script_dialog( void* owner_window );
        bool open_script_directory( );
        [[nodiscard]] std::size_t script_count( );
        [[nodiscard]] std::vector<lua_script_status> script_statuses( );

        [[nodiscard]] const std::filesystem::path& script_directory( ) const
        {
            return m_script_directory;
        }

    private:
        struct script
        {
            std::filesystem::path path{};
            std::filesystem::file_time_type write_time{};
            lua_State* state{};
            bool suspended{};
            std::string last_error{};
        };

        void scan_unlocked( bool force_reload );
        void load_script_unlocked( script& value );
        void unload_script_unlocked( script& value );
        bool call_unlocked( script& value, const char* callback, double number_arg, bool has_number_arg );
        void log_error_unlocked( script& value, const char* phase, const char* error );
        void start_file_watcher( );
        void stop_file_watcher( );

        std::mutex m_mutex{};
        std::filesystem::path m_script_directory{};
        std::vector<std::unique_ptr<script>> m_scripts{};
        std::chrono::steady_clock::time_point m_last_frame{};
        std::atomic_bool m_scan_requested{ true };
        std::jthread m_watch_thread{};
        bool m_initialized{};
    };

    inline lua_manager g_lua{};
}
