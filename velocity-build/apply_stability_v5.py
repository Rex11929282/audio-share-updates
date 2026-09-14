from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "MCB-CS2"


def fail(msg: str):
    raise SystemExit("[stability-v5] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        fail(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, repl: str, label: str, flags=re.S) -> str:
    out, count = re.subn(pattern, repl, text, count=1, flags=flags)
    if count != 1:
        fail(f"{label}: expected 1 regex occurrence, got {count}")
    return out


# ---------------------------------------------------------------------------
# 1) XDraw: transactional GPU buffers, resize-safe blur targets, WIC retry,
#    direct SVG upload, zero-size viewport guard, explicit shutdown.
# ---------------------------------------------------------------------------
xdraw_hpp = v / "project/external/xdraw/xdraw.hpp"
t = xdraw_hpp.read_text(encoding="utf-8")
if "void shutdown( );" not in t:
    t = t.replace("\tbool initialize( ID3D11Device* device, ID3D11DeviceContext* context );\n",
                  "\tbool initialize( ID3D11Device* device, ID3D11DeviceContext* context );\n\tvoid shutdown( );\n", 1)
xdraw_hpp.write_text(t, encoding="utf-8")

xdraw = v / "project/external/xdraw/xdraw.cpp"
t = xdraw.read_text(encoding="utf-8")

# Buffer growth must not destroy the old working buffer until replacement succeeds.
t = regex_once(
    t,
    r"\t\tstatic bool grow_buffer\( ComPtr<ID3D11Buffer>& buf, std::uint32_t& capacity, std::uint32_t required, D3D11_BIND_FLAG bind \)\n\t\t\{.*?\n\t\t\}\n\n\t\tstatic bool create_shaders",
    '''\t\tstatic bool grow_buffer( ComPtr<ID3D11Buffer>& buf, std::uint32_t& capacity, std::uint32_t required, D3D11_BIND_FLAG bind )
\t\t{
\t\t\tif ( required <= capacity && buf )
\t\t\t{
\t\t\t\treturn true;
\t\t\t}

\t\t\tstd::uint64_t next = std::max<std::uint64_t>( capacity ? capacity : 4096u, 4096u );
\t\t\twhile ( next < required )
\t\t\t{
\t\t\t\tnext = std::min<std::uint64_t>( next * 2ull, 0xFFFFFFFFull );
\t\t\t\tif ( next < required && next == 0xFFFFFFFFull )
\t\t\t\t{
\t\t\t\t\treturn false;
\t\t\t\t}
\t\t\t}

\t\t\tconst auto new_capacity = static_cast<std::uint32_t>( next );
\t\t\tComPtr<ID3D11Buffer> replacement{};
\t\t\tif ( !create_buffer( g.device.Get( ), replacement, new_capacity, bind ) )
\t\t\t{
\t\t\t\treturn false;
\t\t\t}

\t\t\tbuf = std::move( replacement );
\t\t\tcapacity = new_capacity;
\t\t\treturn true;
\t\t}

\t\tstatic bool create_shaders''',
    "transactional grow_buffer")

# Replace blur target allocation with an all-or-nothing build. If recreation
# fails after a resize, disable blur for that frame instead of keeping stale-size resources.
t = regex_once(
    t,
    r"\t\tstatic void create_blur_textures\( int w, int h \)\n\t\t\{.*?\n\t\t\}\n\n\t\tstatic void update_blur_cb",
    '''\t\tstatic void release_blur_textures( )
\t\t{
\t\t\tg.blur_scene_tex.Reset( );
\t\t\tg.blur_scene_srv.Reset( );
\t\t\tfor ( auto& lvl : g.blur_chain )
\t\t\t{
\t\t\t\tlvl.tex.Reset( );
\t\t\t\tlvl.rtv.Reset( );
\t\t\t\tlvl.srv.Reset( );
\t\t\t\tlvl.w = 0;
\t\t\t\tlvl.h = 0;
\t\t\t}
\t\t\tg.glow_tex.Reset( );
\t\t\tg.glow_rtv.Reset( );
\t\t\tg.glow_srv.Reset( );
\t\t\tg.blur_cached_w = 0;
\t\t\tg.blur_cached_h = 0;
\t\t}

\t\tstatic bool create_blur_textures( int w, int h )
\t\t{
\t\t\tif ( !g.device || w <= 0 || h <= 0 )
\t\t\t{
\t\t\t\treturn false;
\t\t\t}

\t\t\tComPtr<ID3D11Texture2D> scene_tex{};
\t\t\tComPtr<ID3D11ShaderResourceView> scene_srv{};
\t\t\tstate::blur_level chain[ state::k_blur_iterations ]{};
\t\t\tComPtr<ID3D11Texture2D> glow_tex{};
\t\t\tComPtr<ID3D11RenderTargetView> glow_rtv{};
\t\t\tComPtr<ID3D11ShaderResourceView> glow_srv{};

\t\t\tD3D11_TEXTURE2D_DESC td{};
\t\t\ttd.Width = static_cast<UINT>( w );
\t\t\ttd.Height = static_cast<UINT>( h );
\t\t\ttd.MipLevels = 1;
\t\t\ttd.ArraySize = 1;
\t\t\ttd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
\t\t\ttd.SampleDesc.Count = 1;
\t\t\ttd.Usage = D3D11_USAGE_DEFAULT;
\t\t\ttd.BindFlags = D3D11_BIND_SHADER_RESOURCE;

\t\t\tif ( FAILED( g.device->CreateTexture2D( &td, nullptr, &scene_tex ) ) ) return false;
\t\t\tD3D11_SHADER_RESOURCE_VIEW_DESC sv{};
\t\t\tsv.Format = td.Format;
\t\t\tsv.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
\t\t\tsv.Texture2D.MipLevels = 1;
\t\t\tif ( FAILED( g.device->CreateShaderResourceView( scene_tex.Get( ), &sv, &scene_srv ) ) ) return false;

\t\t\tauto mw = w;
\t\t\tauto mh = h;
\t\t\tfor ( auto i = 0; i < state::k_blur_iterations; ++i )
\t\t\t{
\t\t\t\tmw = std::max( 1, mw / 2 );
\t\t\t\tmh = std::max( 1, mh / 2 );
\t\t\t\tauto& lvl = chain[ i ];
\t\t\t\tlvl.w = mw;
\t\t\t\tlvl.h = mh;

\t\t\t\tD3D11_TEXTURE2D_DESC ltd{};
\t\t\t\tltd.Width = static_cast<UINT>( mw );
\t\t\t\tltd.Height = static_cast<UINT>( mh );
\t\t\t\tltd.MipLevels = 1;
\t\t\t\tltd.ArraySize = 1;
\t\t\t\tltd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
\t\t\t\tltd.SampleDesc.Count = 1;
\t\t\t\tltd.Usage = D3D11_USAGE_DEFAULT;
\t\t\t\tltd.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
\t\t\t\tif ( FAILED( g.device->CreateTexture2D( &ltd, nullptr, &lvl.tex ) ) ||
\t\t\t\t\tFAILED( g.device->CreateRenderTargetView( lvl.tex.Get( ), nullptr, &lvl.rtv ) ) ||
\t\t\t\t\tFAILED( g.device->CreateShaderResourceView( lvl.tex.Get( ), nullptr, &lvl.srv ) ) )
\t\t\t\t{
\t\t\t\t\treturn false;
\t\t\t\t}
\t\t\t}

\t\t\tD3D11_TEXTURE2D_DESC gtd{};
\t\t\tgtd.Width = static_cast<UINT>( w );
\t\t\tgtd.Height = static_cast<UINT>( h );
\t\t\tgtd.MipLevels = 1;
\t\t\tgtd.ArraySize = 1;
\t\t\tgtd.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
\t\t\tgtd.SampleDesc.Count = 1;
\t\t\tgtd.Usage = D3D11_USAGE_DEFAULT;
\t\t\tgtd.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
\t\t\tif ( FAILED( g.device->CreateTexture2D( &gtd, nullptr, &glow_tex ) ) ||
\t\t\t\tFAILED( g.device->CreateRenderTargetView( glow_tex.Get( ), nullptr, &glow_rtv ) ) ||
\t\t\t\tFAILED( g.device->CreateShaderResourceView( glow_tex.Get( ), nullptr, &glow_srv ) ) )
\t\t\t{
\t\t\t\treturn false;
\t\t\t}

\t\t\tg.blur_scene_tex = std::move( scene_tex );
\t\t\tg.blur_scene_srv = std::move( scene_srv );
\t\t\tfor ( auto i = 0; i < state::k_blur_iterations; ++i ) g.blur_chain[ i ] = std::move( chain[ i ] );
\t\t\tg.glow_tex = std::move( glow_tex );
\t\t\tg.glow_rtv = std::move( glow_rtv );
\t\t\tg.glow_srv = std::move( glow_srv );
\t\t\tg.blur_cached_w = w;
\t\t\tg.blur_cached_h = h;
\t\t\treturn true;
\t\t}

\t\tstatic void ensure_blur_textures_for_bound_target( )
\t\t{
\t\t\tif ( !g.context ) return;
\t\t\tComPtr<ID3D11RenderTargetView> rtv{};
\t\t\tg.context->OMGetRenderTargets( 1, &rtv, nullptr );
\t\t\tif ( !rtv ) return;
\t\t\tComPtr<ID3D11Resource> res{};
\t\t\trtv->GetResource( &res );
\t\t\tComPtr<ID3D11Texture2D> tex{};
\t\t\tif ( !res || FAILED( res.As( &tex ) ) ) return;
\t\t\tD3D11_TEXTURE2D_DESC desc{};
\t\t\ttex->GetDesc( &desc );
\t\t\tconst auto w = static_cast<int>( desc.Width );
\t\t\tconst auto h = static_cast<int>( desc.Height );
\t\t\tif ( w <= 0 || h <= 0 ) return;
\t\t\tif ( w != g.blur_cached_w || h != g.blur_cached_h )
\t\t\t{
\t\t\t\tif ( !create_blur_textures( w, h ) ) release_blur_textures( );
\t\t\t}
\t\t}

\t\tstatic void update_blur_cb''',
    "transactional blur resources")

# WIC failure must be retryable; do not permanently cache a null factory.
old_factory = '''\t\tstatic ComPtr<IWICImagingFactory> factory = [ ]
\t\t\t{
\t\t\t\tComPtr<IWICImagingFactory> f{};
\t\t\t\t( void )CoCreateInstance( CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS( &f ) );
\t\t\t\treturn f;
\t\t\t}( );

\t\tif ( !factory )'''
new_factory = '''\t\tstatic ComPtr<IWICImagingFactory> factory{};
\t\tif ( !factory )
\t\t{
\t\t\t( void )CoCreateInstance( CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS( &factory ) );
\t\t}

\t\tif ( !factory )'''
if t.count(old_factory) < 2:
    fail("WIC factory blocks not found")
t = t.replace(old_factory, new_factory)

# NanoSVG already produced RGBA pixels. Upload them directly instead of RGBA -> PNG -> RGBA.
t = regex_once(
    t,
    r"(\t\tif \( out_width \) \{ \*out_width = width; \}\n\t\tif \( out_height \) \{ \*out_height = height; \}\n)\n\t\tstatic ComPtr<IWICImagingFactory> factory\{\};.*?\n\t\treturn load_texture\( std::span<const std::byte>\{ png_data.data\( \), read \}, nullptr, nullptr \);",
    r"\1\n\t\treturn create_srv_from_rgba( pixels.data( ), width, height );",
    "direct SVG RGBA upload")

# Rebuild blur targets at frame start, before any command stores their raw SRV pointer.
marker = '''\t\tfor ( auto i = 0; i < 3; ++i )
\t\t{
\t\t\tdetail::g.lists[ i ].clear( );'''
if "ensure_blur_textures_for_bound_target( );" not in t:
    t = replace_once(t, marker,
        '''\t\tdetail::ensure_blur_textures_for_bound_target( );

\t\tfor ( auto i = 0; i < 3; ++i )
\t\t{
\t\t\tdetail::g.lists[ i ].clear( );''', "frame-start blur rebuild")

# Remove resize-dependent blur recreation from end_frame; doing it there invalidates
# raw texture pointers already stored in this frame's draw commands.
t = re.sub(
    r"\n\t\tauto bb_w\{ 0 \};\n\t\tauto bb_h\{ 0 \};\n\n\t\t\{.*?\n\t\tif \( bb_w > 0 && bb_h > 0 && \( bb_w != d\.blur_cached_w \|\| bb_h != d\.blur_cached_h \) \)\n\t\t\{\n\t\t\tdetail::create_blur_textures\( bb_w, bb_h \);\n\t\t\}\n",
    "\n", t, count=1, flags=re.S)

# Zero-sized viewport can happen during minimize/resize. Never generate infinities.
vp_anchor = '''\t\td.context->RSGetViewports( &vp_count, &vp );

\t\tD3D11_RECT full_scissor{};'''
if vp_anchor in t:
    t = replace_once(t, vp_anchor,
        '''\t\td.context->RSGetViewports( &vp_count, &vp );
\t\tif ( vp_count == 0 || vp.Width <= 0.0f || vp.Height <= 0.0f )
\t\t{
\t\t\treturn;
\t\t}

\t\tD3D11_RECT full_scissor{};''', "zero viewport guard")

# Render paths must honor grow_buffer failure.
t = t.replace(
    '''\t\t\tgrow_buffer( g.vb, g.vb_capacity, vtx_bytes, D3D11_BIND_VERTEX_BUFFER );
\t\t\tgrow_buffer( g.ib, g.ib_capacity, idx_bytes, D3D11_BIND_INDEX_BUFFER );

\t\t\tD3D11_MAPPED_SUBRESOURCE vtx_map{}, idx_map{};''',
    '''\t\t\tif ( !grow_buffer( g.vb, g.vb_capacity, vtx_bytes, D3D11_BIND_VERTEX_BUFFER ) ||
\t\t\t\t !grow_buffer( g.ib, g.ib_capacity, idx_bytes, D3D11_BIND_INDEX_BUFFER ) )
\t\t\t{
\t\t\t\treturn;
\t\t\t}

\t\t\tD3D11_MAPPED_SUBRESOURCE vtx_map{}, idx_map{};''')
t = t.replace(
    '''\t\t\t\tdetail::grow_buffer( d.vb, d.vb_capacity, vtx_bytes, D3D11_BIND_VERTEX_BUFFER );
\t\t\t\tdetail::grow_buffer( d.ib, d.ib_capacity, idx_bytes, D3D11_BIND_INDEX_BUFFER );

\t\t\t\tD3D11_MAPPED_SUBRESOURCE vtx_map{}, idx_map{};''',
    '''\t\t\t\tif ( !detail::grow_buffer( d.vb, d.vb_capacity, vtx_bytes, D3D11_BIND_VERTEX_BUFFER ) ||
\t\t\t\t\t !detail::grow_buffer( d.ib, d.ib_capacity, idx_bytes, D3D11_BIND_INDEX_BUFFER ) )
\t\t\t\t{
\t\t\t\t\treturn;
\t\t\t\t}

\t\t\t\tD3D11_MAPPED_SUBRESOURCE vtx_map{}, idx_map{};''')

# Explicit xdraw teardown is required for a real device recreation.
if "\tvoid shutdown( )\n\t{" not in t:
    init_end = '''\t\treturn true;
\t}

\tvoid begin_frame( bool update_timing )'''
    t = replace_once(t, init_end,
        '''\t\treturn true;
\t}

\tvoid shutdown( )
\t{
\t\tdetail::g = detail::state{};
\t}

\tvoid begin_frame( bool update_timing )''', "xdraw shutdown")

xdraw.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 2) XUI: thread-safe event snapshot, focus reset, safe numeric editor,
#    and keybind isolation while typing/capturing.
# ---------------------------------------------------------------------------
xui_hpp = v / "project/external/xdraw/xui/xui.hpp"
t = xui_hpp.read_text(encoding="utf-8")
if "bool focus_lost{};" not in t:
    t = t.replace("\t\tfloat scroll_delta{};\n", "\t\tfloat scroll_delta{};\n\t\tbool focus_lost{};\n", 1)
xui_hpp.write_text(t, encoding="utf-8")

xui = v / "project/external/xdraw/xui/xui.cpp"
t = xui.read_text(encoding="utf-8")
if "#include <charconv>" not in t:
    t = t.replace("#include <pch/pch.hpp>\n", "#include <pch/pch.hpp>\n#include <charconv>\n#include <mutex>\n#include <optional>\n", 1)

# clear_frame must clear the edge-triggered focus flag.
t = t.replace("\t\tthis->scroll_delta = 0.0f;\n\n\t\tthis->char_count = 0;",
              "\t\tthis->scroll_delta = 0.0f;\n\t\tthis->focus_lost = false;\n\n\t\tthis->char_count = 0;", 1)

# Focus/capture loss resets held input in the pending event state.
focus_case = '''\t\tcase WM_KILLFOCUS:
\t\tcase WM_CANCELMODE:
\t\tcase WM_CAPTURECHANGED:
\t\t\ts.mouse_down = false;
\t\t\ts.rmb_down = false;
\t\t\ts.keys.clear( );
\t\t\ts.focus_lost = true;
\t\t\treturn true;

'''
if "case WM_KILLFOCUS:" not in t:
    t = t.replace("\t\tswitch ( msg )\n\t\t{\n", "\t\tswitch ( msg )\n\t\t{\n" + focus_case, 1)

# Decouple WndProc mutations from the render snapshot.
if "pending_input_state" not in t:
    helper = '''
\tnamespace
\t{
\t\tinput_state& pending_input_state( )
\t\t{
\t\t\tstatic input_state value{};
\t\t\treturn value;
\t\t}

\t\tstd::mutex& pending_input_mutex( )
\t\t{
\t\t\tstatic std::mutex value{};
\t\t\treturn value;
\t\t}

\t\tvoid snapshot_pending_input( input_state& destination )
\t\t{
\t\t\tstd::lock_guard lock( pending_input_mutex( ) );
\t\t\tdestination = pending_input_state( );
\t\t\tpending_input_state( ).clear_frame( );
\t\t}
\t}

'''
    t = t.replace("\tvoid begin( )\n\t{", helper + "\tvoid begin( )\n\t{", 1)

# Snapshot exactly once at the beginning of a UI frame and clear active capture on focus loss.
begin_anchor = "\tvoid begin( )\n\t{\n\t\tauto& c = get_ctx( );\n"
if begin_anchor in t and "snapshot_pending_input( c.input );" not in t:
    t = t.replace(begin_anchor, begin_anchor +
        '''\t\tsnapshot_pending_input( c.input );
\t\tif ( c.input.focus_lost )
\t\t{
\t\t\tc.active_window = null_id;
\t\t\tc.active_resize = null_id;
\t\t\tc.active_slider = null_id;
\t\t\tc.active_keybind = null_id;
\t\t\tc.active_text_input = null_id;
\t\t\tc.active_slider_edit = null_id;
\t\t\tc.active_child_scroll = null_id;
\t\t}
''', 1)

# WndProc writes only to the pending state.
t = t.replace(
    '''\tbool wndproc( UINT msg, WPARAM wp, LPARAM lp )
\t{
\t\treturn feed_wndproc( get_ctx( ).input, msg, wp, lp );
\t}''',
    '''\tbool wndproc( UINT msg, WPARAM wp, LPARAM lp )
\t{
\t\tstd::lock_guard lock( pending_input_mutex( ) );
\t\treturn feed_wndproc( pending_input_state( ), msg, wp, lp );
\t}''')

# Do not trigger regular keybinds while a text box or key-capture widget owns the keyboard.
t = t.replace("\t\tbinds::process( c.input );",
              "\t\tif ( c.active_text_input == null_id && c.active_slider_edit == null_id && binds::listening_id( ) == 0 )\n\t\t{\n\t\t\tbinds::process( c.input );\n\t\t}", 1)

# Exception-free numeric parsing helper.
slider_marker = '''\tnamespace {

\t\ttemplate<typename T>
\t\tinline bool slider( std::uintptr_t id'''
if slider_marker in t and "parse_numeric_text" not in t:
    slider_helper = '''\tnamespace {

\t\ttemplate<typename T>
\t\tstd::optional<T> parse_numeric_text( std::string_view text )
\t\t{
\t\t\tif ( text.empty( ) ) return std::nullopt;
\t\t\tT value{};
\t\t\tconst auto* first = text.data( );
\t\t\tconst auto* last = first + text.size( );
\t\t\tconst auto result = std::from_chars( first, last, value );
\t\t\tif ( result.ec != std::errc{} || result.ptr != last ) return std::nullopt;
\t\t\treturn value;
\t\t}

\t\ttemplate<typename T>
\t\tinline bool slider( std::uintptr_t id'''
    t = replace_once(t, slider_marker, slider_helper, "safe slider parser helper")

# Replace both click-away and Enter parse blocks without exceptions.
t = re.sub(
    r'''if constexpr \( std::is_floating_point_v<T> \)\n\t\t\t\t\t\{\n\t\t\t\t\t\tv = std::clamp\( static_cast< T >\( std::stod\( buf \) \), v_min, v_max \);\n\t\t\t\t\t\}\n\t\t\t\t\telse\n\t\t\t\t\t\{\n\t\t\t\t\t\tv = std::clamp\( static_cast< T >\( std::stoi\( buf \) \), v_min, v_max \);\n\t\t\t\t\t\}\n\n\t\t\t\t\tchanged = true;''',
    '''if ( const auto parsed = parse_numeric_text<T>( buf ) )
\t\t\t\t\t{
\t\t\t\t\t\tv = std::clamp( *parsed, v_min, v_max );
\t\t\t\t\t\tchanged = true;
\t\t\t\t\t}''', t)
t = re.sub(
    r'''if constexpr \( std::is_floating_point_v<T> \)\n\t\t\t\t\t\t\t\{\n\t\t\t\t\t\t\t\tv = std::clamp\( static_cast< T >\( std::stod\( buf \) \), v_min, v_max \);\n\t\t\t\t\t\t\t\}\n\t\t\t\t\t\t\telse\n\t\t\t\t\t\t\t\{\n\t\t\t\t\t\t\t\tv = std::clamp\( static_cast< T >\( std::stoi\( buf \) \), v_min, v_max \);\n\t\t\t\t\t\t\t\}\n\n\t\t\t\t\t\t\tchanged = true;''',
    '''if ( const auto parsed = parse_numeric_text<T>( buf ) )
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tv = std::clamp( *parsed, v_min, v_max );
\t\t\t\t\t\t\t\tchanged = true;
\t\t\t\t\t\t\t}''', t)

xui.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 3) Rendering context: active swap-chain identity, failed-resize recovery,
#    full xdraw reset on real device/window recreation, no viewport side effect.
# ---------------------------------------------------------------------------
render_hpp = v / "project/core/rendering/rendering.hpp"
t = render_hpp.read_text(encoding="utf-8")
t = t.replace("\t\tvoid on_resize_buffers( );\n", "\t\tvoid on_resize_buffers( IDXGISwapChain* swap_chain );\n", 1)
t = t.replace("\t\tvoid create_rtv( IDXGISwapChain* swap_chain );\n", "\t\tbool create_rtv( IDXGISwapChain* swap_chain );\n", 1)
if "IDXGISwapChain* m_swap_chain" not in t:
    t = t.replace("\t\tID3D11Device* m_device{ nullptr };\n", "\t\tIDXGISwapChain* m_swap_chain{ nullptr };\n\t\tID3D11Device* m_device{ nullptr };\n", 1)
render_hpp.write_text(t, encoding="utf-8")

context = v / "project/core/rendering/impl/context.cpp"
t = context.read_text(encoding="utf-8")

# initialize: save swapchain and fail if RTV cannot be created.
t = t.replace("\t\tthis->m_window = desc.OutputWindow;\n\n\t\tthis->create_rtv( swap_chain );",
              "\t\tthis->m_window = desc.OutputWindow;\n\t\tthis->m_swap_chain = swap_chain;\n\n\t\tif ( !this->create_rtv( swap_chain ) )\n\t\t{\n\t\t\tthis->shutdown( );\n\t\t\treturn false;\n\t\t}", 1)

# shutdown must invalidate UI readiness and release all xdraw device-owned objects.
shutdown_anchor = "\tvoid context::shutdown( )\n\t{\n"
if shutdown_anchor in t and "xdraw::shutdown( );" not in t:
    t = t.replace(shutdown_anchor, shutdown_anchor + "\t\txdraw::shutdown( );\n", 1)
t = t.replace("\t\tthis->m_window = nullptr;\n\t\tthis->m_initialized = false;",
              "\t\tthis->m_swap_chain = nullptr;\n\t\tthis->m_window = nullptr;\n\t\tthis->m_ui_assets_ready = false;\n\t\tthis->m_initialized = false;", 1)

# Present: ignore unrelated swapchains; reinitialize if the main window moves to a new swapchain.
present_anchor = '''\tvoid context::on_present( IDXGISwapChain* swap_chain )
\t{
\t\tif ( !this->m_initialized ) [[unlikely]]'''
if present_anchor in t and "secondary_swap_chain" not in t:
    present_new = '''\tvoid context::on_present( IDXGISwapChain* swap_chain )
\t{
\t\tif ( this->m_initialized && this->m_swap_chain && swap_chain != this->m_swap_chain )
\t\t{
\t\t\tDXGI_SWAP_CHAIN_DESC secondary_swap_chain{};
\t\t\tif ( FAILED( swap_chain->GetDesc( &secondary_swap_chain ) ) || secondary_swap_chain.OutputWindow != this->m_window )
\t\t\t{
\t\t\t\treturn;
\t\t\t}
\t\t\tthis->shutdown( );
\t\t}

\t\tif ( !this->m_initialized ) [[unlikely]]'''
    t = replace_once(t, present_anchor, present_new, "swapchain identity")

# Recover a missing RTV on Present (including after failed ResizeBuffers).
t = t.replace("\t\tthis->try_bind_ui_assets( );\n\t\tfeatures::misc::g_dlight.on_present( );",
              "\t\tif ( !this->m_rtv && !this->create_rtv( swap_chain ) ) return;\n\t\tthis->try_bind_ui_assets( );\n\t\tfeatures::misc::g_dlight.on_present( );", 1)

# Resize only touches the active swapchain; post runs on success or failure to restore old RTV.
t = t.replace("\tvoid context::on_resize_buffers( )\n\t{\n\t\tif ( this->m_rtv )",
              "\tvoid context::on_resize_buffers( IDXGISwapChain* swap_chain )\n\t{\n\t\tif ( swap_chain != this->m_swap_chain ) return;\n\t\tif ( this->m_rtv )", 1)
t = t.replace("\tvoid context::on_resize_buffers_post( IDXGISwapChain* swap_chain )\n\t{\n\t\tthis->create_rtv( swap_chain );\n\t}",
              "\tvoid context::on_resize_buffers_post( IDXGISwapChain* swap_chain )\n\t{\n\t\tif ( swap_chain != this->m_swap_chain ) return;\n\t\t( void )this->create_rtv( swap_chain );\n\t}", 1)

# create_rtv returns a result and never mutates the host viewport.
t = regex_once(
    t,
    r"\tvoid context::create_rtv\( IDXGISwapChain\* swap_chain \)\n\t\{.*?\n\t\}\n\n\tvoid context::setup_zdraw",
    '''\tbool context::create_rtv( IDXGISwapChain* swap_chain )
\t{
\t\tif ( !swap_chain || !this->m_device ) return false;
\t\tID3D11Texture2D* back_buffer{};
\t\tif ( FAILED( swap_chain->GetBuffer( 0, __uuidof( ID3D11Texture2D ), reinterpret_cast<void**>( &back_buffer ) ) ) || !back_buffer )
\t\t{
\t\t\treturn false;
\t\t}

\t\tID3D11RenderTargetView* replacement{};
\t\tconst auto hr = this->m_device->CreateRenderTargetView( back_buffer, nullptr, &replacement );
\t\tback_buffer->Release( );
\t\tif ( FAILED( hr ) || !replacement ) return false;

\t\tif ( this->m_rtv ) this->m_rtv->Release( );
\t\tthis->m_rtv = replacement;
\t\treturn true;
\t}

\tvoid context::setup_zdraw''', "create_rtv isolation")
context.write_text(t, encoding="utf-8")

hooks = v / "project/core/hooks/impl/cheat.cpp"
t = hooks.read_text(encoding="utf-8")
t = t.replace("\t\trendering::g_context.on_resize_buffers( );", "\t\trendering::g_context.on_resize_buffers( thisptr );", 1)
# Always call post: on failure the old swapchain buffers remain valid and RTV must be restored.
t = t.replace(
    '''\t\tconst auto result = m_resize_buffers.call<long>( thisptr, buffer_count, width, height, new_format, swap_chain_flags );
\t\tif ( SUCCEEDED( result ) )
\t\t{
\t\t\trendering::g_context.on_resize_buffers_post( thisptr );
\t\t}

\t\treturn result;''',
    '''\t\tconst auto result = m_resize_buffers.call<long>( thisptr, buffer_count, width, height, new_format, swap_chain_flags );
\t\trendering::g_context.on_resize_buffers_post( thisptr );
\t\treturn result;''', 1)
hooks.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 4) Skin UI/data path: exception-free seed input, non-blocking decode lock,
#    correct compressed texture row pitch, checked VPK reads.
# ---------------------------------------------------------------------------
skins = v / "project/core/rendering/impl/menu/menu.skins.cpp"
t = skins.read_text(encoding="utf-8")
if "#include <charconv>" not in t:
    t = t.replace("#include <pch/pch.hpp>\n", "#include <pch/pch.hpp>\n#include <charconv>\n", 1)
old_seed = '''\t\t\t\t\t\tconst auto v = std::stoi( this->m_buf );
\t\t\t\t\t\tit->second.seed = std::clamp( v, 0, 1000 );'''
new_seed = '''\t\t\t\t\t\tint v{};
\t\t\t\t\t\tconst auto parsed = std::from_chars( this->m_buf.data( ), this->m_buf.data( ) + this->m_buf.size( ), v );
\t\t\t\t\t\tif ( !this->m_buf.empty( ) && parsed.ec == std::errc{} && parsed.ptr == this->m_buf.data( ) + this->m_buf.size( ) )
\t\t\t\t\t\t{
\t\t\t\t\t\t\tit->second.seed = std::clamp( v, 0, 1000 );
\t\t\t\t\t\t}'''
if old_seed in t:
    t = t.replace(old_seed, new_seed, 1)
# UTF-8-safe ASCII lowering: leave non-ASCII bytes untouched.
t = re.sub(r"c = static_cast< char >\( std::tolower\( c \) \);",
           "if ( c >= 'A' && c <= 'Z' ) c = static_cast<char>( c - 'A' + 'a' );", t)
t = re.sub(r"c = static_cast< char >\(std::tolower\(c\)\);",
           "if ( c >= 'A' && c <= 'Z' ) c = static_cast<char>( c - 'A' + 'a' );", t)
skins.write_text(t, encoding="utf-8")

# Config search gets the same safe ASCII folding.
config_menu = v / "project/core/rendering/impl/menu/menu.config.cpp"
t = config_menu.read_text(encoding="utf-8")
t = re.sub(r"c = static_cast< char >\( std::tolower\( c \) \);",
           "if ( c >= 'A' && c <= 'Z' ) c = static_cast<char>( c - 'A' + 'a' );", t)
config_menu.write_text(t, encoding="utf-8")

# Econ image decode: move VPK I/O and decode outside m_image_mutex. Commit only the finished result.
econ = v / "project/core/features/changer/impl/econ_item_system.cpp"
t = econ.read_text(encoding="utf-8")
t = regex_once(
    t,
    r"\tvoid econ_item_system::request_decode\( const std::string& image_inventory \)\n\t\{.*?\n\t\}\n\n\tbool econ_item_system::finalize_texture",
    '''\tvoid econ_item_system::request_decode( const std::string& image_inventory )
\t{
\t\tconst auto key = image_inventory + xs( "_png" );
\t\tauto it = this->m_image_cache.find( image_inventory );
\t\tif ( it == this->m_image_cache.end( ) ) return;
\t\tit->second->state.store( image_state::loading, std::memory_order_release );

\t\tthreadpool::run( [ this, inv = image_inventory, key ]( )
\t\t{
\t\t\tstd::vector<std::byte> data;
\t\t\t{
\t\t\t\tstd::lock_guard lock( this->m_vpk_mutex );
\t\t\t\tdata = this->read_vpk( key );
\t\t\t}

\t\t\timage_entry decoded{};
\t\t\tconst auto ok = !data.empty( ) && this->decode_vtex(
\t\t\t\tstd::span<const std::byte>( data.data( ), data.size( ) ), decoded );

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
\t}

\tbool econ_item_system::finalize_texture''',
    "async skin decode without image mutex")

# Correct row pitch for block-compressed formats. BC7 keeps the existing RGBA decode path.
t = t.replace("\t\tconst auto upload_pitch = entry.width * 4;",
'''\t\tauto upload_pitch = entry.width * 4u;
\t\tif ( upload_format == DXGI_FORMAT_BC1_UNORM || upload_format == DXGI_FORMAT_BC4_UNORM )
\t\t{
\t\t\tupload_pitch = std::max( 1u, ( entry.width + 3u ) / 4u ) * 8u;
\t\t}
\t\telse if ( upload_format == DXGI_FORMAT_BC3_UNORM || upload_format == DXGI_FORMAT_BC6H_UF16 || upload_format == DXGI_FORMAT_BC7_UNORM )
\t\t{
\t\t\tupload_pitch = std::max( 1u, ( entry.width + 3u ) / 4u ) * 16u;
\t\t}''', 1)

# Compressed textures cannot be used as generic render targets for GenerateMips.
old_desc = '''\t\ttd.MipLevels = 0;
\t\ttd.ArraySize = 1;
\t\ttd.Format = upload_format;
\t\ttd.SampleDesc.Count = 1;
\t\ttd.Usage = D3D11_USAGE_DEFAULT;
\t\ttd.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
\t\ttd.MiscFlags = D3D11_RESOURCE_MISC_GENERATE_MIPS;'''
new_desc = '''\t\tconst auto compressed_upload = upload_format == DXGI_FORMAT_BC1_UNORM || upload_format == DXGI_FORMAT_BC3_UNORM ||
\t\t\tupload_format == DXGI_FORMAT_BC4_UNORM || upload_format == DXGI_FORMAT_BC6H_UF16 || upload_format == DXGI_FORMAT_BC7_UNORM;
\t\ttd.MipLevels = compressed_upload ? 1u : 0u;
\t\ttd.ArraySize = 1;
\t\ttd.Format = upload_format;
\t\ttd.SampleDesc.Count = 1;
\t\ttd.Usage = D3D11_USAGE_DEFAULT;
\t\ttd.BindFlags = D3D11_BIND_SHADER_RESOURCE | ( compressed_upload ? 0u : D3D11_BIND_RENDER_TARGET );
\t\ttd.MiscFlags = compressed_upload ? 0u : D3D11_RESOURCE_MISC_GENERATE_MIPS;'''
if old_desc in t:
    t = t.replace(old_desc, new_desc, 1)
t = t.replace("\t\tsv.Texture2D.MipLevels = static_cast< UINT >( -1 );",
              "\t\tsv.Texture2D.MipLevels = compressed_upload ? 1u : static_cast<UINT>( -1 );", 1)
t = t.replace("\t\tctx->GenerateMips( srv.Get( ) );",
              "\t\tif ( !compressed_upload ) ctx->GenerateMips( srv.Get( ) );", 1)

# VPK stream state/checks: a single short read must not poison the archive handle forever.
read_old = '''\t\tstream.seekg( entry.offset );

\t\tstd::vector<std::byte> data( entry.length );
\t\tstream.read( reinterpret_cast< char* >( data.data( ) ), entry.length );

\t\treturn data;'''
read_new = '''\t\tconstexpr std::uint32_t max_asset_bytes{ 32u * 1024u * 1024u };
\t\tif ( entry.length == 0 || entry.length > max_asset_bytes ) return {};
\t\tstream.clear( );
\t\tstream.seekg( static_cast<std::streamoff>( entry.offset ), std::ios::beg );
\t\tif ( !stream ) return {};

\t\tstd::vector<std::byte> data( entry.length );
\t\tstream.read( reinterpret_cast<char*>( data.data( ) ), static_cast<std::streamsize>( entry.length ) );
\t\tif ( stream.gcount( ) != static_cast<std::streamsize>( entry.length ) )
\t\t{
\t\t\tstream.clear( );
\t\t\treturn {};
\t\t}
\t\treturn data;'''
if read_old in t:
    t = t.replace(read_old, read_new, 1)

# VTEX dimensions/mips are bounded before shifts/allocations.
t = t.replace("\t\tif ( width == 0 || height == 0 || mip_count == 0 )",
              "\t\tif ( width == 0 || height == 0 || width > 8192 || height > 8192 || mip_count == 0 || mip_count > 16 )", 1)
econ.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 5) Menu search: index both original English identifiers and visible zh-TW labels.
# ---------------------------------------------------------------------------
menu_core = v / "project/core/rendering/impl/menu/menu.core.cpp"
t = menu_core.read_text(encoding="utf-8")
t = t.replace(
    '''\t\t\tentry.name_lower = detail::to_lower_copy( entry.name );
\t\t\tentry.category_lower = detail::to_lower_copy( entry.category );''',
    '''\t\t\tentry.name_lower = detail::to_lower_copy( entry.name );
\t\t\tentry.name_lower.push_back( '\\n' );
\t\t\tentry.name_lower.append( localization::tr( entry.name ) );
\t\t\tentry.category_lower = detail::to_lower_copy( entry.category );
\t\t\tentry.category_lower.push_back( '\\n' );
\t\t\tentry.category_lower.append( localization::tr( entry.category ) );''', 1)
menu_core.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 6) Lua: finite HUD inputs, bounded logs, UTF-safe filenames, watcher resilience,
#    case-insensitive .lua detection, close-time instruction hook.
# ---------------------------------------------------------------------------
lua = v / "project/core/scripting/lua_manager.cpp"
t = lua.read_text(encoding="utf-8")
if "#include <cmath>" not in t:
    t = t.replace("#include <chrono>\n", "#include <chrono>\n#include <cmath>\n", 1)

# Internal registry keys are MCB too.
t = t.replace('"velocity.draw_count"', '"mcb.draw_count"').replace('"velocity.name"', '"mcb.name"')

# Helper functions for finite numbers, UTF-8 filenames and extension checks.
if "finite_number" not in t:
    helper_anchor = "    constexpr int max_hud_draw_calls_per_callback = 512;\n"
    helper = '''    constexpr int max_hud_draw_calls_per_callback = 512;
    constexpr std::size_t max_log_bytes = 512;
    constexpr int max_logs_per_callback = 8;

    float finite_number( lua_State* L, int index, const char* label )
    {
        const auto value = static_cast<float>( luaL_checknumber( L, index ) );
        if ( !std::isfinite( value ) ) luaL_error( L, "%s must be finite", label );
        return value;
    }

    float finite_optional( lua_State* L, int index, float fallback, const char* label )
    {
        const auto value = static_cast<float>( luaL_optnumber( L, index, fallback ) );
        if ( !std::isfinite( value ) ) luaL_error( L, "%s must be finite", label );
        return value;
    }

    std::string path_filename_utf8( const std::filesystem::path& path )
    {
        const auto u8 = path.filename( ).u8string( );
        return std::string( reinterpret_cast<const char*>( u8.data( ) ), u8.size( ) );
    }

    bool is_lua_file( const std::filesystem::path& path )
    {
        const auto ext = path.extension( ).wstring( );
        return _wcsicmp( ext.c_str( ), L".lua" ) == 0;
    }
'''
    t = replace_once(t, helper_anchor, helper, "Lua v5 helpers")

# Per-callback log count uses registry, reset together with draw budget.
if "mcb.log_count" not in t:
    t = t.replace(
        '''    void reset_draw_budget( lua_State* L )
    {
        lua_pushinteger( L, 0 );
        lua_setfield( L, LUA_REGISTRYINDEX, "mcb.draw_count" );
    }''',
        '''    void reset_draw_budget( lua_State* L )
    {
        lua_pushinteger( L, 0 );
        lua_setfield( L, LUA_REGISTRYINDEX, "mcb.draw_count" );
        lua_pushinteger( L, 0 );
        lua_setfield( L, LUA_REGISTRYINDEX, "mcb.log_count" );
    }''', 1)

# Bound log volume before synchronous diagnostic I/O.
t = regex_once(
    t,
    r"    int log_fn\( lua_State\* L \)\n    \{.*?\n    \}\n\n    int now_fn",
    '''    int log_fn( lua_State* L )
    {
        lua_getfield( L, LUA_REGISTRYINDEX, "mcb.log_count" );
        const auto count = static_cast<int>( lua_tointeger( L, -1 ) );
        lua_pop( L, 1 );
        if ( count >= max_logs_per_callback ) return 0;
        lua_pushinteger( L, count + 1 );
        lua_setfield( L, LUA_REGISTRYINDEX, "mcb.log_count" );

        size_t size{};
        const char* value = luaL_checklstring( L, 1, &size );
        size = std::min( size, max_log_bytes );
        auto text = "[MCB Lua:" + script_name( L ) + "] " + std::string( value, size );
        logging::console::print_raw( text.c_str( ) );
        return 0;
    }

    int now_fn''', "Lua log rate limit")

# Finite validation for HUD geometry.
t = t.replace("static_cast<float>( luaL_checknumber( L, 1 ) )", "finite_number( L, 1, \"x\" )")
t = t.replace("static_cast<float>( luaL_checknumber( L, 2 ) )", "finite_number( L, 2, \"y\" )")
t = t.replace("std::clamp( static_cast<float>( luaL_checknumber( L, 3 ) ), 0.5f, 80.0f )",
              "std::clamp( finite_number( L, 3, \"radius\" ), 0.5f, 80.0f )")
t = t.replace("static_cast<float>( luaL_checknumber( L, 3 ) )", "finite_number( L, 3, \"x2\" )")
t = t.replace("static_cast<float>( luaL_checknumber( L, 4 ) )", "finite_number( L, 4, \"y2\" )")
t = t.replace("static_cast<float>( luaL_optnumber( L, 9, 1.0 ) )", "finite_optional( L, 9, 1.0f, \"thickness\" )")

# UTF-8 script names and case-insensitive extensions.
t = t.replace("const auto filename = path.filename( ).string( );", "const auto filename = path_filename_utf8( path );")
t = t.replace("value.path.filename( ).string( ).c_str( )", "path_filename_utf8( value.path ).c_str( )")
t = t.replace("value.path.filename( ).string( )", "path_filename_utf8( value.path )")
t = t.replace("entry.path( ).filename( ).string( )", "path_filename_utf8( entry.path( ) )")
t = t.replace("entry.path( ).extension( ) != L\".lua\"", "!is_lua_file( entry.path( ) )")
t = t.replace("entry.path( ).extension( ) == L\".lua\"", "is_lua_file( entry.path( ) )")

# A failed directory enumeration must not be interpreted as deletion of every script.
scan_sort = "        std::sort( files.begin( ), files.end( ) );"
if scan_sort in t and "Lua scan aborted" not in t:
    t = t.replace(scan_sort,
        '''        if ( ec )
        {
            logging::console::print( "[MCB Lua] 掃描失敗，保留目前腳本：{}", ec.message( ) );
            return;
        }
        std::sort( files.begin( ), files.end( ) );''', 1)

# Watcher thread must never let filesystem exceptions escape into std::terminate.
watch_start = "        m_watch_thread = std::jthread( [this, directory]( std::stop_token stop )\n        {\n"
if watch_start in t and "watcher exception" not in t:
    t = t.replace(watch_start, watch_start + "            try\n            {\n", 1)
    watch_tail = "            }\n        } );\n    }\n\n    void lua_manager::stop_file_watcher"
    if watch_tail in t:
        t = t.replace(watch_tail,
            '''            }
            }
            catch ( const std::exception& e )
            {
                logging::console::print( "[MCB Lua] watcher exception: {}", e.what( ) );
            }
            catch ( ... )
            {
                logging::console::print( "[MCB Lua] watcher exception: unknown" );
            }
        } );
    }

    void lua_manager::stop_file_watcher''', 1)

# Re-enable instruction budget for Lua finalizers executed by lua_close.
t = t.replace("        lua_close( value.state );\n        value.state = nullptr;",
              "        lua_sethook( value.state, budget, LUA_MASKCOUNT, hook_budget );\n        lua_close( value.state );\n        value.state = nullptr;")

lua.write_text(t, encoding="utf-8")

print("[stability-v5] defensive stability/performance fixes applied")
