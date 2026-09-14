from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "MCB-CS2"


def fail(msg: str):
    raise SystemExit("[release-v5] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        fail(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, repl: str, label: str, flags=re.S) -> str:
    out, count = re.subn(pattern, repl, text, count=1, flags=flags)
    if count != 1:
        fail(f"{label}: expected 1 occurrence, got {count}")
    return out


# ---------------------------------------------------------------------------
# 1) Reuse the existing Ship configuration as MCB Stable. This avoids adding a
# third project configuration (which would require duplicating every per-file
# PCH/unity condition) while keeping the stable build optimized and protected.
# ---------------------------------------------------------------------------
proj = v / "MCB-CS2.vcxproj"
t = proj.read_text(encoding="utf-8")
t = replace_once(t, "<TargetName>MCB-CS2</TargetName>", "<TargetName>MCB-CS2-v5-stable</TargetName>", "stable target name")
ship_start = t.index("<ItemDefinitionGroup Condition=\"'$(Configuration)|$(Platform)'=='Ship|x64'\">")
ship_end = t.index("</ItemDefinitionGroup>", ship_start) + len("</ItemDefinitionGroup>")
ship = t[ship_start:ship_end]
ship = ship.replace("<BufferSecurityCheck>false</BufferSecurityCheck>", "<BufferSecurityCheck>true</BufferSecurityCheck>")
ship = ship.replace("<ExceptionHandling>false</ExceptionHandling>", "<ExceptionHandling>Sync</ExceptionHandling>")
ship = ship.replace("<OmitFramePointers>true</OmitFramePointers>", "<OmitFramePointers>false</OmitFramePointers>")
ship = ship.replace("NDEBUG;MCBCS2_EXPORTS;", "NDEBUG;MCB_STABLE;MCBCS2_EXPORTS;")
if "<Optimization>MaxSpeed</Optimization>" not in ship:
    ship = ship.replace("<FavorSizeOrSpeed>Speed</FavorSizeOrSpeed>", "<FavorSizeOrSpeed>Speed</FavorSizeOrSpeed>\n      <Optimization>MaxSpeed</Optimization>")
t = t[:ship_start] + ship + t[ship_end:]
proj.write_text(t, encoding="utf-8")

# Stable Windows metadata.
rc = v / "project/mcb_version.rc"
t = rc.read_text(encoding="utf-8")
t = t.replace("FILEVERSION 1,0,0,0", "FILEVERSION 1,5,0,0")
t = t.replace("PRODUCTVERSION 1,0,0,0", "PRODUCTVERSION 1,5,0,0")
t = t.replace('VALUE "FileVersion", "1.0.0\\0"', 'VALUE "FileVersion", "1.5.0\\0"')
t = t.replace('VALUE "ProductVersion", "1.0.0\\0"', 'VALUE "ProductVersion", "1.5.0\\0"')
t = t.replace('VALUE "OriginalFilename", "MCB-CS2-dev.dll\\0"', 'VALUE "OriginalFilename", "MCB-CS2-v5-stable.dll\\0"')
rc.write_text(t, encoding="utf-8")

# Stable keeps unhandled minidumps, but DEV-only first-chance/process-wide hooks
# remain disabled because MCB_STABLE is intentionally not DEV.
diag = v / "project/utilities/diag.hpp"
t = diag.read_text(encoding="utf-8")
t = t.replace("#if defined( DEV )", "#if defined( DEV ) || defined( MCB_STABLE )")
diag.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 2) Lua: memory quota, global draw-call budget and immediately cancellable
# watcher sleep. These are sandbox/lifecycle protections only.
# ---------------------------------------------------------------------------
scripting = v / "project/core/scripting/scripting.hpp"
t = scripting.read_text(encoding="utf-8")
if "#include <condition_variable>" not in t:
    t = t.replace("#include <chrono>\n", "#include <chrono>\n#include <condition_variable>\n", 1)
if "struct lua_memory_budget" not in t:
    t = t.replace("namespace scripting\n{\n", "namespace scripting\n{\n    struct lua_memory_budget\n    {\n        std::size_t used{};\n        std::size_t limit{ 32u * 1024u * 1024u };\n    };\n\n", 1)
if "lua_memory_budget memory" not in t:
    t = t.replace("            std::string last_error{};\n", "            std::string last_error{};\n            lua_memory_budget memory{};\n", 1)
if "m_watch_cv" not in t:
    t = t.replace("        std::jthread m_watch_thread{};\n", "        std::jthread m_watch_thread{};\n        std::mutex m_watch_wait_mutex{};\n        std::condition_variable_any m_watch_cv{};\n", 1)
scripting.write_text(t, encoding="utf-8")

lua = v / "project/core/scripting/lua_manager.cpp"
t = lua.read_text(encoding="utf-8")
if "#include <cstdlib>" not in t:
    t = t.replace("#include <cstdint>\n", "#include <cstdint>\n#include <cstdlib>\n", 1)

if "max_hud_draw_calls_per_frame" not in t:
    t = t.replace(
        "    constexpr int max_hud_draw_calls_per_callback = 512;\n",
        "    constexpr int max_hud_draw_calls_per_callback = 512;\n    constexpr int max_hud_draw_calls_per_frame = 2048;\n    thread_local int hud_draw_calls_this_frame{};\n", 1)
    t = t.replace(
        "        if ( current >= max_hud_draw_calls_per_callback )\n        {\n            return false;\n        }\n",
        "        if ( current >= max_hud_draw_calls_per_callback || hud_draw_calls_this_frame >= max_hud_draw_calls_per_frame )\n        {\n            return false;\n        }\n", 1)
    t = t.replace("        lua_pushinteger( L, current + 1 );\n", "        ++hud_draw_calls_this_frame;\n        lua_pushinteger( L, current + 1 );\n", 1)
    frame_anchor = "        const auto now = std::chrono::steady_clock::now( );\n"
    t = replace_once(t, frame_anchor, "        hud_draw_calls_this_frame = 0;\n\n" + frame_anchor, "Lua global HUD budget reset")

if "limited_lua_allocator" not in t:
    alloc_anchor = "    void budget( lua_State* L, lua_Debug* )\n"
    allocator = '''    void* limited_lua_allocator( void* ud, void* ptr, std::size_t osize, std::size_t nsize )
    {
        auto* budget = static_cast<scripting::lua_memory_budget*>( ud );
        const auto old_size = ptr ? osize : 0u;
        if ( nsize == 0 )
        {
            if ( ptr ) std::free( ptr );
            budget->used = old_size > budget->used ? 0u : budget->used - old_size;
            return nullptr;
        }

        if ( nsize > old_size )
        {
            const auto growth = nsize - old_size;
            if ( growth > budget->limit - std::min( budget->used, budget->limit ) ) return nullptr;
        }

        void* replacement = std::realloc( ptr, nsize );
        if ( !replacement ) return nullptr;
        if ( nsize >= old_size ) budget->used += nsize - old_size;
        else budget->used -= std::min( budget->used, old_size - nsize );
        return replacement;
    }

'''
    t = replace_once(t, alloc_anchor, allocator + alloc_anchor, "Lua allocator")

t = t.replace("        value.state = luaL_newstate( );", "        value.memory.used = 0;\n        value.state = lua_newstate( limited_lua_allocator, &value.memory );", 1)

# Wake the watcher immediately on shutdown instead of waiting up to 750 ms.
sleep = "                std::this_thread::sleep_for( std::chrono::milliseconds( 750 ) );\n"
if sleep in t:
    t = t.replace(sleep,
        "                {\n                    std::unique_lock wait_lock( m_watch_wait_mutex );\n                    m_watch_cv.wait_for( wait_lock, std::chrono::milliseconds( 750 ), [&] { return stop.stop_requested( ); } );\n                }\n", 1)
stop = "            m_watch_thread.request_stop( );\n            m_watch_thread.join( );"
if stop in t:
    t = t.replace(stop, "            m_watch_thread.request_stop( );\n            m_watch_cv.notify_all( );\n            m_watch_thread.join( );", 1)
lua.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 3) Track outstanding skin decode jobs so orderly shutdown cannot unload MCB
# while a host thread-pool callback still points into this DLL.
# ---------------------------------------------------------------------------
changer = v / "project/core/features/changer/changer.hpp"
t = changer.read_text(encoding="utf-8")
if "#include <utilities/threadpool/threadpool.hpp>" not in t:
    t = t.replace("#include <filesystem>\n", "#include <filesystem>\n#include <utilities/threadpool/threadpool.hpp>\n", 1)
if "void shutdown( );" not in t.split("class agents", 1)[0]:
    t = t.replace("        void flush_skin_images( );\n", "        void flush_skin_images( );\n        void shutdown( );\n", 1)
if "m_image_jobs" not in t:
    t = t.replace("        std::mutex m_image_mutex{};\n", "        std::mutex m_image_mutex{};\n        std::mutex m_job_mutex{};\n        std::vector<threadpool::job> m_image_jobs{};\n        std::atomic_bool m_shutting_down{};\n", 1)
changer.write_text(t, encoding="utf-8")

econ = v / "project/core/features/changer/impl/econ_item_system.cpp"
t = econ.read_text(encoding="utf-8")
t = regex_once(
    t,
    r"\tvoid econ_item_system::request_decode\( const std::string& image_inventory \)\n\t\{.*?\n\t\}\n\n\tbool econ_item_system::finalize_texture",
    '''\tvoid econ_item_system::request_decode( const std::string& image_inventory )
\t{
\t\tif ( this->m_shutting_down.load( std::memory_order_acquire ) ) return;
\t\tconst auto key = image_inventory + xs( "_png" );
\t\tauto it = this->m_image_cache.find( image_inventory );
\t\tif ( it == this->m_image_cache.end( ) ) return;
\t\tit->second->state.store( image_state::loading, std::memory_order_release );

\t\tauto handle = threadpool::run( [ this, inv = image_inventory, key ]( )
\t\t{
\t\t\tif ( this->m_shutting_down.load( std::memory_order_acquire ) ) return;
\t\t\tstd::vector<std::byte> data;
\t\t\t{
\t\t\t\tstd::lock_guard lock( this->m_vpk_mutex );
\t\t\t\tdata = this->read_vpk( key );
\t\t\t}

\t\t\timage_entry decoded{};
\t\t\tconst auto ok = !data.empty( ) && this->decode_vtex(
\t\t\t\tstd::span<const std::byte>( data.data( ), data.size( ) ), decoded );
\t\t\tif ( this->m_shutting_down.load( std::memory_order_acquire ) ) return;

\t\t\tstd::lock_guard lock( this->m_image_mutex );
\t\t\tconst auto target = this->m_image_cache.find( inv );
\t\t\tif ( target == this->m_image_cache.end( ) ) return;
\t\t\tif ( !ok )
\t\t\t{
\t\t\t\ttarget->second->state.store( image_state::failed, std::memory_order_release );
\t\t\t\treturn;
\t\t\t}

\t\t\ttarget->second->mip_buffers = std::move( decoded.mip_buffers );
\t\t\ttarget->second->width = decoded.width;
\t\t\ttarget->second->height = decoded.height;
\t\t\ttarget->second->format = decoded.format;
\t\t\ttarget->second->state.store( image_state::decoded, std::memory_order_release );
\t\t} );

\t\tif ( handle )
\t\t{
\t\t\tstd::lock_guard lock( this->m_job_mutex );
\t\t\tstd::erase_if( this->m_image_jobs, []( const threadpool::job& job ) { return job.complete( ); } );
\t\t\tthis->m_image_jobs.emplace_back( std::move( handle ) );
\t\t}
\t}

\tvoid econ_item_system::shutdown( )
\t{
\t\tthis->m_shutting_down.store( true, std::memory_order_release );
\t\tstd::vector<threadpool::job> jobs;
\t\t{
\t\t\tstd::lock_guard lock( this->m_job_mutex );
\t\t\tjobs.swap( this->m_image_jobs );
\t\t}
\t\tfor ( const auto& job : jobs )
\t\t{
\t\t\tif ( job ) job.wait( );
\t\t}
\t\tthis->flush_skin_images( );
\t}

\tbool econ_item_system::finalize_texture''',
    "tracked skin decode jobs")
econ.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 4) Entry lifecycle: loader-lock attach does the minimum. Stable installs only
# the last-chance filter, not DEV's first-chance/TerminateProcess hooks. An
# exported orderly shutdown entry is provided; DllMain keeps a fallback for
# manual unload, while process termination avoids calls into tearing-down DLLs.
# ---------------------------------------------------------------------------
entry = v / "project/entry.cpp"
t = entry.read_text(encoding="utf-8")

# Diagnostics file I/O belongs on the init worker, not DLL_PROCESS_ATTACH.
init_anchor = "\t\tconst auto module_handle = static_cast<HMODULE>( param );\n\n"
if init_anchor in t and "diag::set_module( module_handle );" not in t[t.index("DWORD WINAPI init_thread_impl"):t.index("stage: thread start")]:
    t = t.replace(init_anchor, init_anchor + "\t\tdiag::set_module( module_handle );\n", 1)
t = t.replace("\n\t\tdiag::set_module( module_handle );\n\t\tdiag::step( \"stage: dll attach\" );\n\t\tdiag::step( \"build: development diagnostics\" );\n", "\n", 1)
t = t.replace("\t\tDisableThreadLibraryCalls( module_handle );\n", "", 1)

# Stable optional warnings must remain warnings rather than becoming fatal MessageBoxes.
t = t.replace("#define INIT_WARN( msg ) INIT_FAIL( msg )", "#define INIT_WARN( msg ) diag::write( diag::level::warning, msg )", 1)

# Correct COM ownership: initialize and uninitialize on the same worker thread.
if "struct com_apartment_guard" not in t:
    guard_anchor = "\tDWORD WINAPI init_thread_impl( LPVOID param )\n"
    guard = '''\tstruct com_apartment_guard
\t{
\t\tHRESULT result{ E_FAIL };
\t\tcom_apartment_guard( ) : result( CoInitializeEx( nullptr, COINIT_MULTITHREADED ) ) {}
\t\t~com_apartment_guard( ) { if ( SUCCEEDED( result ) ) CoUninitialize( ); }
\t};

'''
    t = replace_once(t, guard_anchor, guard + guard_anchor, "COM worker guard")
t = regex_once(
    t,
    r"\t\tdiag::step\( \"stage: coinit\" \);\n\t\tconst auto coinit_result =\n\t\t\tCoInitializeEx\( nullptr, COINIT_MULTITHREADED \);\n\t\tif \( FAILED\( coinit_result \) \)\n\t\t\{\n\t\t\tdiag::writef\(\n\t\t\t\tdiag::level::warning,\n\t\t\t\t\"CoInitializeEx failed; hresult=0x%08lX\",\n\t\t\t\tcoinit_result \);\n\t\t\}",
    '''\t\tdiag::step( "stage: coinit" );
\t\tcom_apartment_guard com_guard{};
\t\tif ( FAILED( com_guard.result ) )
\t\t{
\t\t\tdiag::writef( diag::level::warning, "CoInitializeEx failed; hresult=0x%08lX", com_guard.result );
\t\t}''',
    "COM guard usage")

# Stable keeps the unhandled filter, but only DEV receives the intrusive first-chance VEH.
veh_block = '''\t\tg_vectored_exception_handler =
\t\t\tAddVectoredExceptionHandler( 1, diag_vectored_exception_filter );
\t\tif ( !g_vectored_exception_handler )
\t\t{
\t\t\tdiag::writef(
\t\t\t\tdiag::level::error,
\t\t\t\t"failed to install vectored exception handler; win32_error=%lu",
\t\t\t\tGetLastError( ) );
\t\t}
\t\telse
\t\t{
\t\t\tdiag::write( diag::level::info, "crash handlers installed" );
\t\t}'''
if veh_block in t:
    t = t.replace(veh_block, "#if defined( DEV )\n" + veh_block + "\n#else\n\t\tdiag::write( diag::level::info, \"stable last-chance crash handler installed\" );\n#endif", 1)

# Non-core presentation/index systems should degrade rather than kill the whole DLL.
t = t.replace('INIT_FAIL( "failed to initialize vpk parse system." );', 'INIT_WARN( "failed to initialize vpk parse system; icons disabled." );', 1)
t = t.replace('INIT_FAIL( "failed to initialize model preview system." );', 'INIT_WARN( "failed to initialize model preview system; preview disabled." );', 1)
t = t.replace('INIT_FAIL( "failed to initialize econ item system." );', 'INIT_WARN( "failed to initialize econ item system; skin browser disabled." );', 1)

# Bound the optional econ wait to five seconds instead of up to thirty.
econ_text = econ.read_text(encoding="utf-8")
if "constexpr auto max_attempts{ 300 };" in econ_text:
    econ_text = econ_text.replace("constexpr auto max_attempts{ 300 };", "constexpr auto max_attempts{ 50 };", 1)
    econ.write_text(econ_text, encoding="utf-8")

# Replace the heavy DEV-only detach block with one idempotent runtime shutdown helper.
detach_pos = t.index('extern "C" int __stdcall entry')
helper = '''\n\tstd::atomic_bool g_runtime_shutdown_done{};

\tvoid runtime_shutdown( )
\t{
\t\tif ( g_runtime_shutdown_done.exchange( true, std::memory_order_acq_rel ) ) return;

\t\thooks::utility::shutdown( );
\t\thooks::cheat::shutdown( );
\t\tsystems::events::shutdown( );
\t\tscripting::g_lua.shutdown( );
\t\tfeatures::changer::g_econ_item_system.shutdown( );
\t\tfeatures::esp::player::g_chams.bt( ).shutdown( );
\t\tfeatures::esp::player::g_chams.os( ).shutdown( );
\t\tfeatures::world::g_weather.release( );
\t\tsystems::g_icons.shutdown( );
\t\trendering::g_menu.shutdown( );
\t\trendering::g_context.shutdown( );

#if defined( DEV )
\t\tg_terminate_process_hook.reset( );
\t\tg_minidump_hook.reset( );
#endif
\t\tif ( g_vectored_exception_handler )
\t\t{
\t\t\tRemoveVectoredExceptionHandler( g_vectored_exception_handler );
\t\t\tg_vectored_exception_handler = nullptr;
\t\t}
\t\tconst auto previous_filter = g_previous_exception_filter.exchange( nullptr, std::memory_order_acq_rel );
\t\tconst auto current_filter = SetUnhandledExceptionFilter( previous_filter );
\t\tif ( current_filter != diag_unhandled_exception_filter ) SetUnhandledExceptionFilter( current_filter );
\t\tdiag::shutdown( );
\t}
'''
if "g_runtime_shutdown_done" not in t:
    t = t[:detach_pos] + helper + "\n" + t[detach_pos:]

# Replace the process-detach body. Process termination does not call back into host DLLs.
t = regex_once(
    t,
    r"\telse if \( reason == DLL_PROCESS_DETACH \)\n\t\{.*?\n\t\}\n\n\treturn 1;\n\}",
    '''\telse if ( reason == DLL_PROCESS_DETACH )
\t{
\t\tif ( reserved == nullptr )
\t\t{
\t\t\truntime_shutdown( );
\t\t}
\t\t_CRT_INIT( module_handle, reason, reserved );
\t}

\treturn 1;
}

extern "C" __declspec( dllexport ) void __stdcall MCB_Shutdown( )
{
\truntime_shutdown( );
}''',
    "stable detach lifecycle")

# The old detach-side CoUninitialize belonged to a different thread and must be gone.
t = t.replace("\t\tCoUninitialize( );\n", "")
entry.write_text(t, encoding="utf-8")

print("[release-v5] optimized stable configuration + orderly lifecycle protections applied")
