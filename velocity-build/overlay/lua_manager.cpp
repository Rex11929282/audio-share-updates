#include <pch/pch.hpp>
#include <algorithm>
#include <array>
#include <chrono>
#include <commdlg.h>
#include <cstdint>
#include <fstream>
#include <iterator>
#include <lua.hpp>
#include <shellapi.h>
#include <string_view>
#include <thread>
#include <utilities/logging/logging.hpp>
#include "../../external/xdraw/xdraw.hpp"
#include "scripting.hpp"

namespace
{
    constexpr std::size_t max_script = 1024u * 1024u;
    constexpr int hook_budget = 120000;
    constexpr int max_hud_draw_calls_per_callback = 512;

    void budget( lua_State* L, lua_Debug* )
    {
        luaL_error( L, "instruction budget exceeded" );
    }

    int clamp_channel( lua_State* L, int index, int fallback = 255 )
    {
        if ( lua_gettop( L ) < index || lua_isnoneornil( L, index ) )
        {
            return fallback;
        }
        return std::clamp( static_cast<int>( luaL_checkinteger( L, index ) ), 0, 255 );
    }

    bool consume_draw_budget( lua_State* L )
    {
        lua_getfield( L, LUA_REGISTRYINDEX, "velocity.draw_count" );
        const auto current = static_cast<int>( lua_tointeger( L, -1 ) );
        lua_pop( L, 1 );
        if ( current >= max_hud_draw_calls_per_callback )
        {
            return false;
        }
        lua_pushinteger( L, current + 1 );
        lua_setfield( L, LUA_REGISTRYINDEX, "velocity.draw_count" );
        return true;
    }

    void reset_draw_budget( lua_State* L )
    {
        lua_pushinteger( L, 0 );
        lua_setfield( L, LUA_REGISTRYINDEX, "velocity.draw_count" );
    }

    std::string script_name( lua_State* L )
    {
        lua_getfield( L, LUA_REGISTRYINDEX, "velocity.name" );
        const char* value = lua_tostring( L, -1 );
        std::string result = value ? value : "?";
        lua_pop( L, 1 );
        return result;
    }

    int log_fn( lua_State* L )
    {
        size_t size{};
        const char* value = luaL_checklstring( L, 1, &size );
        auto text = "[Lua:" + script_name( L ) + "] " + std::string( value, size );
        logging::console::print_raw( text.c_str( ) );
        return 0;
    }

    int now_fn( lua_State* L )
    {
        using namespace std::chrono;
        lua_pushinteger(
            L,
            static_cast< lua_Integer >(
                duration_cast< milliseconds >( steady_clock::now( ).time_since_epoch( ) ).count( ) ) );
        return 1;
    }

    int screen_size_fn( lua_State* L )
    {
        const auto [w, h] = xdraw::viewport_size( );
        lua_pushinteger( L, w );
        lua_pushinteger( L, h );
        return 2;
    }

    int hud_line_fn( lua_State* L )
    {
        if ( !consume_draw_budget( L ) )
        {
            return 0;
        }

        const auto x1 = static_cast<float>( luaL_checknumber( L, 1 ) );
        const auto y1 = static_cast<float>( luaL_checknumber( L, 2 ) );
        const auto x2 = static_cast<float>( luaL_checknumber( L, 3 ) );
        const auto y2 = static_cast<float>( luaL_checknumber( L, 4 ) );
        const auto r = clamp_channel( L, 5 );
        const auto g = clamp_channel( L, 6 );
        const auto b = clamp_channel( L, 7 );
        const auto a = clamp_channel( L, 8 );
        const auto thickness = std::clamp(
            static_cast<float>( luaL_optnumber( L, 9, 1.0 ) ), 0.5f, 8.0f );

        xdraw::get( xdraw::layer::top ).line(
            x1, y1, x2, y2,
            xdraw::color{ static_cast<std::uint8_t>( r ), static_cast<std::uint8_t>( g ),
                          static_cast<std::uint8_t>( b ), static_cast<std::uint8_t>( a ) },
            thickness, true );
        return 0;
    }

    int hud_circle_fn( lua_State* L )
    {
        if ( !consume_draw_budget( L ) )
        {
            return 0;
        }

        const auto x = static_cast<float>( luaL_checknumber( L, 1 ) );
        const auto y = static_cast<float>( luaL_checknumber( L, 2 ) );
        const auto radius = std::clamp( static_cast<float>( luaL_checknumber( L, 3 ) ), 0.5f, 80.0f );
        const auto r = clamp_channel( L, 4 );
        const auto g = clamp_channel( L, 5 );
        const auto b = clamp_channel( L, 6 );
        const auto a = clamp_channel( L, 7 );

        xdraw::get( xdraw::layer::top ).circle_filled(
            x, y, radius,
            xdraw::color{ static_cast<std::uint8_t>( r ), static_cast<std::uint8_t>( g ),
                          static_cast<std::uint8_t>( b ), static_cast<std::uint8_t>( a ) },
            12, true );
        return 0;
    }

    int hud_text_fn( lua_State* L )
    {
        if ( !consume_draw_budget( L ) )
        {
            return 0;
        }

        const auto x = static_cast<float>( luaL_checknumber( L, 1 ) );
        const auto y = static_cast<float>( luaL_checknumber( L, 2 ) );
        size_t len{};
        const auto* text = luaL_checklstring( L, 3, &len );
        len = std::min<std::size_t>( len, 256 );
        const auto r = clamp_channel( L, 4 );
        const auto g = clamp_channel( L, 5 );
        const auto b = clamp_channel( L, 6 );
        const auto a = clamp_channel( L, 7 );

        xdraw::get( xdraw::layer::top ).text(
            x, y, std::string_view{ text, len },
            xdraw::color{ static_cast<std::uint8_t>( r ), static_cast<std::uint8_t>( g ),
                          static_cast<std::uint8_t>( b ), static_cast<std::uint8_t>( a ) } );
        return 0;
    }

    void open_safe_libraries( lua_State* L )
    {
        struct library
        {
            const char* name;
            lua_CFunction open;
        };

        constexpr library safe_libraries[]
        {
            { "_G", luaopen_base },
            { LUA_TABLIBNAME, luaopen_table },
            { LUA_STRLIBNAME, luaopen_string },
            { LUA_MATHLIBNAME, luaopen_math },
            { LUA_UTF8LIBNAME, luaopen_utf8 }
        };

        for ( const auto& lib : safe_libraries )
        {
            luaL_requiref( L, lib.name, lib.open, 1 );
            lua_pop( L, 1 );
        }

        lua_pushnil( L ); lua_setglobal( L, "dofile" );
        lua_pushnil( L ); lua_setglobal( L, "loadfile" );
        lua_pushnil( L ); lua_setglobal( L, "io" );
        lua_pushnil( L ); lua_setglobal( L, "os" );
        lua_pushnil( L ); lua_setglobal( L, "package" );
        lua_pushnil( L ); lua_setglobal( L, "debug" );
    }

    void install_api( lua_State* L, const std::filesystem::path& path )
    {
        const auto filename = path.filename( ).string( );
        lua_pushlstring( L, filename.data( ), filename.size( ) );
        lua_setfield( L, LUA_REGISTRYINDEX, "velocity.name" );
        reset_draw_budget( L );

        lua_newtable( L );
        lua_pushcfunction( L, log_fn ); lua_setfield( L, -2, "log" );
        lua_pushcfunction( L, now_fn ); lua_setfield( L, -2, "now_ms" );
        lua_pushcfunction( L, screen_size_fn ); lua_setfield( L, -2, "screen_size" );
        lua_pushcfunction( L, hud_line_fn ); lua_setfield( L, -2, "hud_line" );
        lua_pushcfunction( L, hud_circle_fn ); lua_setfield( L, -2, "hud_circle" );
        lua_pushcfunction( L, hud_text_fn ); lua_setfield( L, -2, "hud_text" );
        lua_pushliteral( L, "1.2" ); lua_setfield( L, -2, "api_version" );
        lua_setglobal( L, "velocity" );
    }

    bool read_script( const std::filesystem::path& path, std::string& out )
    {
        std::error_code ec;
        const auto size = std::filesystem::file_size( path, ec );
        if ( ec || size > max_script )
        {
            return false;
        }

        std::ifstream file( path, std::ios::binary );
        if ( !file )
        {
            return false;
        }

        out.assign( std::istreambuf_iterator< char >( file ), {} );
        return true;
    }

    std::uint64_t directory_signature( const std::filesystem::path& directory )
    {
        std::error_code ec;
        std::vector<std::string> parts;
        for ( const auto& entry : std::filesystem::directory_iterator( directory, ec ) )
        {
            if ( ec )
            {
                break;
            }
            if ( !entry.is_regular_file( ec ) || entry.path( ).extension( ) != L".lua" )
            {
                ec.clear( );
                continue;
            }

            const auto wt = entry.last_write_time( ec );
            if ( ec )
            {
                ec.clear( );
                continue;
            }
            const auto sz = entry.file_size( ec );
            if ( ec )
            {
                ec.clear( );
                continue;
            }

            parts.push_back(
                entry.path( ).filename( ).string( ) + ":" +
                std::to_string( wt.time_since_epoch( ).count( ) ) + ":" +
                std::to_string( sz ) );
        }
        std::sort( parts.begin( ), parts.end( ) );

        std::uint64_t hash = 14695981039346656037ull;
        for ( const auto& part : parts )
        {
            for ( const auto c : part )
            {
                hash ^= static_cast<unsigned char>( c );
                hash *= 1099511628211ull;
            }
        }
        return hash;
    }
}

namespace scripting
{
    bool lua_manager::initialize( void* module_handle )
    {
        std::scoped_lock lock( m_mutex );
        if ( m_initialized )
        {
            return true;
        }

        std::array< wchar_t, 32768 > module_path{};
        const auto length = GetModuleFileNameW(
            static_cast< HMODULE >( module_handle ), module_path.data( ),
            static_cast< DWORD >( module_path.size( ) ) );
        if ( !length || length >= module_path.size( ) )
        {
            return false;
        }

        m_script_directory =
            std::filesystem::path( module_path.data( ) ).parent_path( ) / L"scripts";

        std::error_code ec;
        std::filesystem::create_directories( m_script_directory, ec );
        if ( ec )
        {
            return false;
        }

        // Do not synchronously enumerate/compile scripts on the injection/init path.
        // The first render frame performs the initial load instead.
        m_last_frame = std::chrono::steady_clock::now( );
        m_scan_requested.store( true, std::memory_order_release );
        m_initialized = true;
        start_file_watcher( );
        logging::console::print( "[Lua] 已初始化；腳本會在第一個渲染幀載入：{}", m_script_directory.string( ) );
        return true;
    }

    void lua_manager::start_file_watcher( )
    {
        if ( m_watch_thread.joinable( ) )
        {
            return;
        }

        const auto directory = m_script_directory;
        m_watch_thread = std::jthread( [this, directory]( std::stop_token stop )
        {
            auto previous = directory_signature( directory );
            while ( !stop.stop_requested( ) )
            {
                std::this_thread::sleep_for( std::chrono::milliseconds( 750 ) );
                if ( stop.stop_requested( ) )
                {
                    break;
                }
                const auto current = directory_signature( directory );
                if ( current != previous )
                {
                    previous = current;
                    m_scan_requested.store( true, std::memory_order_release );
                }
            }
        } );
    }

    void lua_manager::stop_file_watcher( )
    {
        if ( m_watch_thread.joinable( ) )
        {
            m_watch_thread.request_stop( );
            m_watch_thread.join( );
        }
    }

    void lua_manager::shutdown( )
    {
        stop_file_watcher( );
        std::scoped_lock lock( m_mutex );
        for ( auto& value : m_scripts )
        {
            unload_script_unlocked( *value );
        }
        m_scripts.clear( );
        m_initialized = false;
    }

    void lua_manager::reload_all( )
    {
        std::scoped_lock lock( m_mutex );
        if ( m_initialized )
        {
            scan_unlocked( true );
            m_scan_requested.store( false, std::memory_order_release );
            logging::console::print( "[Lua] 已重新載入全部腳本" );
        }
    }

    bool lua_manager::import_script_dialog( void* owner_window )
    {
        wchar_t selected[ 32768 ]{};
        constexpr wchar_t filter[] =
            L"Lua 腳本 (*.lua)\0*.lua\0所有檔案 (*.*)\0*.*\0\0";

        OPENFILENAMEW dialog{};
        dialog.lStructSize = sizeof( dialog );
        dialog.hwndOwner = static_cast< HWND >( owner_window );
        dialog.lpstrFilter = filter;
        dialog.lpstrFile = selected;
        dialog.nMaxFile = static_cast< DWORD >( std::size( selected ) );
        dialog.lpstrTitle = L"導入 Lua 腳本";
        dialog.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_EXPLORER;
        dialog.lpstrDefExt = L"lua";

        if ( !GetOpenFileNameW( &dialog ) )
        {
            return false;
        }

        std::filesystem::path source{ selected };
        if ( _wcsicmp( source.extension( ).c_str( ), L".lua" ) != 0 )
        {
            logging::console::print( "[Lua] 導入失敗：只允許 .lua 檔案" );
            return false;
        }

        std::filesystem::path destination;
        {
            std::scoped_lock lock( m_mutex );
            if ( !m_initialized )
            {
                return false;
            }
            destination = m_script_directory / source.filename( );
        }

        std::error_code ec;
        const auto source_canonical = std::filesystem::weakly_canonical( source, ec );
        ec.clear( );
        const auto destination_canonical = std::filesystem::weakly_canonical( destination, ec );
        ec.clear( );

        if ( source_canonical != destination_canonical )
        {
            std::filesystem::copy_file(
                source, destination,
                std::filesystem::copy_options::overwrite_existing, ec );
            if ( ec )
            {
                logging::console::print( "[Lua] 導入失敗：{}", ec.message( ) );
                return false;
            }
        }

        m_scan_requested.store( true, std::memory_order_release );
        logging::console::print( "[Lua] 已導入，將在下一幀載入：{}", destination.filename( ).string( ) );
        return true;
    }

    bool lua_manager::open_script_directory( )
    {
        std::filesystem::path directory;
        {
            std::scoped_lock lock( m_mutex );
            if ( !m_initialized )
            {
                return false;
            }
            directory = m_script_directory;
        }

        const auto result = reinterpret_cast< std::intptr_t >(
            ShellExecuteW( nullptr, L"open", directory.c_str( ), nullptr, nullptr, SW_SHOWNORMAL ) );
        return result > 32;
    }

    std::size_t lua_manager::script_count( )
    {
        std::scoped_lock lock( m_mutex );
        return static_cast<std::size_t>( std::count_if(
            m_scripts.begin( ), m_scripts.end( ),
            []( const auto& value )
            {
                return value && value->state != nullptr && !value->suspended;
            } ) );
    }

    std::vector<lua_script_status> lua_manager::script_statuses( )
    {
        std::scoped_lock lock( m_mutex );
        std::vector<lua_script_status> result;
        result.reserve( m_scripts.size( ) );
        for ( const auto& value : m_scripts )
        {
            if ( !value )
            {
                continue;
            }
            result.push_back( lua_script_status{
                value->path.filename( ).string( ),
                value->state != nullptr,
                value->suspended,
                value->last_error
            } );
        }
        return result;
    }

    void lua_manager::on_frame( )
    {
        std::scoped_lock lock( m_mutex );
        if ( !m_initialized )
        {
            return;
        }

        if ( m_scan_requested.exchange( false, std::memory_order_acq_rel ) )
        {
            scan_unlocked( false );
        }

        const auto now = std::chrono::steady_clock::now( );
        const double dt = std::clamp(
            std::chrono::duration<double>( now - m_last_frame ).count( ), 0.0, 0.1 );
        m_last_frame = now;

        for ( auto& value : m_scripts )
        {
            if ( value->state && !value->suspended )
            {
                call_unlocked( *value, "on_frame", dt, true );
            }
        }
    }

    void lua_manager::scan_unlocked( bool force_reload )
    {
        std::error_code ec;
        std::vector< std::filesystem::path > files;

        for ( auto& entry : std::filesystem::directory_iterator( m_script_directory, ec ) )
        {
            if ( entry.is_regular_file( ec ) && entry.path( ).extension( ) == L".lua" )
            {
                files.push_back( entry.path( ) );
            }
        }
        std::sort( files.begin( ), files.end( ) );

        for ( auto it = m_scripts.begin( ); it != m_scripts.end( ); )
        {
            if ( std::find( files.begin( ), files.end( ), ( *it )->path ) == files.end( ) )
            {
                unload_script_unlocked( **it );
                it = m_scripts.erase( it );
            }
            else
            {
                ++it;
            }
        }

        for ( const auto& path : files )
        {
            const auto write_time = std::filesystem::last_write_time( path, ec );
            if ( ec )
            {
                ec.clear( );
                continue;
            }

            const auto it = std::find_if(
                m_scripts.begin( ), m_scripts.end( ),
                [&]( const auto& value ) { return value->path == path; } );

            if ( it == m_scripts.end( ) )
            {
                auto value = std::make_unique<script>( );
                value->path = path;
                value->write_time = write_time;
                load_script_unlocked( *value );
                m_scripts.push_back( std::move( value ) );
            }
            else if ( force_reload || ( *it )->write_time != write_time )
            {
                unload_script_unlocked( **it );
                ( *it )->write_time = write_time;
                ( *it )->suspended = false;
                ( *it )->last_error.clear( );
                load_script_unlocked( **it );
            }
        }
    }

    void lua_manager::load_script_unlocked( script& value )
    {
        value.suspended = false;
        value.last_error.clear( );

        std::string source;
        if ( !read_script( value.path, source ) )
        {
            log_error_unlocked( value, "載入", "讀取失敗或檔案超過 1 MiB" );
            return;
        }

        value.state = luaL_newstate( );
        if ( !value.state )
        {
            log_error_unlocked( value, "載入", "無法建立 Lua VM" );
            return;
        }

        open_safe_libraries( value.state );
        install_api( value.state, value.path );
        lua_sethook( value.state, budget, LUA_MASKCOUNT, hook_budget );

        int result = luaL_loadbufferx(
            value.state, source.data( ), source.size( ),
            value.path.filename( ).string( ).c_str( ), "t" );
        if ( result == LUA_OK )
        {
            reset_draw_budget( value.state );
            result = lua_pcall( value.state, 0, 0, 0 );
        }
        lua_sethook( value.state, nullptr, 0, 0 );

        if ( result != LUA_OK )
        {
            const auto error = lua_tostring( value.state, -1 );
            const std::string message = error ? error : "未知錯誤";
            log_error_unlocked( value, "載入", message.c_str( ) );
            lua_close( value.state );
            value.state = nullptr;
            return;
        }

        logging::console::print( "[Lua] 已載入：{}", value.path.filename( ).string( ) );
        call_unlocked( value, "on_load", 0.0, false );
    }

    void lua_manager::unload_script_unlocked( script& value )
    {
        if ( !value.state )
        {
            return;
        }
        if ( !value.suspended )
        {
            call_unlocked( value, "on_unload", 0.0, false );
        }
        lua_close( value.state );
        value.state = nullptr;
    }

    bool lua_manager::call_unlocked(
        script& value, const char* callback, double number_arg, bool has_number_arg )
    {
        lua_getglobal( value.state, callback );
        if ( !lua_isfunction( value.state, -1 ) )
        {
            lua_pop( value.state, 1 );
            return true;
        }

        reset_draw_budget( value.state );
        if ( has_number_arg )
        {
            lua_pushnumber( value.state, number_arg );
        }

        lua_sethook( value.state, budget, LUA_MASKCOUNT, hook_budget );
        const auto result = lua_pcall( value.state, has_number_arg ? 1 : 0, 0, 0 );
        lua_sethook( value.state, nullptr, 0, 0 );

        if ( result != LUA_OK )
        {
            const auto error = lua_tostring( value.state, -1 );
            const std::string message = error ? error : "未知錯誤";
            lua_pop( value.state, 1 );
            log_error_unlocked( value, callback, message.c_str( ) );
            if ( std::string_view{ callback } == "on_frame" || std::string_view{ callback } == "on_load" )
            {
                value.suspended = true;
                logging::console::print( "[Lua] 已暫停腳本避免持續卡頓：{}", value.path.filename( ).string( ) );
            }
            return false;
        }
        return true;
    }

    void lua_manager::log_error_unlocked(
        script& value, const char* phase, const char* error )
    {
        value.last_error = error ? error : "?";
        logging::console::print(
            "[Lua] {} {}：{}",
            value.path.filename( ).string( ), phase ? phase : "?", value.last_error );
    }
}
