from pathlib import Path
import re
import sys

root = Path(sys.argv[1]).resolve()
v = root / "cs2" / "velocity-cs2"


def die(msg: str):
    raise SystemExit("[ui-v2] " + msg)


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        die(f"{label}: expected 1 occurrence, got {count}")
    return text.replace(old, new, 1)


# ---------------------------------------------------------------------------
# 1) Menu state: dedicated Lua subtab + persistent/manual layout state.
# ---------------------------------------------------------------------------
hpp = v / "project" / "core" / "rendering" / "rendering.hpp"
t = hpp.read_text(encoding="utf-8")

t = t.replace(
    '{ { "general" },                                            1 }',
    '{ { "general", "lua" },                                     2 }')

if "m_user_layout_initialized" not in t:
    anchor = '''        float m_body_w{};\n        float m_body_h{};\n'''
    addition = '''        float m_body_w{};\n        float m_body_h{};\n\n        // User-controlled menu geometry. Once initialized we no longer\n        // overwrite it every frame, which also avoids tab-switch jitter.\n        bool m_user_layout_initialized{};\n        int m_layout_viewport_w{};\n        int m_layout_viewport_h{};\n'''
    t = replace_once(t, anchor, addition, "layout state")

hpp.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 2) Stable responsive layout. Initialize once, then preserve user size/pos.
#    Right-bottom resize grip is enabled by xui::begin_window(resizable=true).
# ---------------------------------------------------------------------------
menu = v / "project" / "core" / "rendering" / "impl" / "menu" / "menu.core.cpp"
t = menu.read_text(encoding="utf-8")

start_marker = "\t\t\t// Responsive full-view menu."
begin_marker = '\t\t\tif ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, false, 200.0f, 200.0f, menu_reveal ) )'
if start_marker in t and "MCB user-resizable stable layout" not in t:
    start = t.index(start_marker)
    begin = t.index(begin_marker, start)
    stable = '''\t\t\t// MCB user-resizable stable layout. The first frame picks a compact\n\t\t\t// centered size. After that, drag/resize values are preserved and are\n\t\t\t// only clamped when the viewport changes or the menu leaves the screen.\n\t\t\tconst auto [ viewport_w_i, viewport_h_i ] = xdraw::viewport_size( );\n\t\t\tif ( viewport_w_i > 0 && viewport_h_i > 0 )\n\t\t\t{\n\t\t\t\tconst auto viewport_w = static_cast< float >( viewport_w_i );\n\t\t\t\tconst auto viewport_h = static_cast< float >( viewport_h_i );\n\t\t\t\tconstexpr float safe_margin{ 20.0f };\n\t\t\t\tconst auto available_w = std::max( 1.0f, viewport_w - safe_margin * 2.0f );\n\t\t\t\tconst auto available_h = std::max( 1.0f, viewport_h - safe_margin * 2.0f );\n\t\t\t\tconst auto min_w = std::min( 620.0f, available_w );\n\t\t\t\tconst auto min_h = std::min( 460.0f, available_h );\n\n\t\t\t\tif ( !this->m_user_layout_initialized )\n\t\t\t\t{\n\t\t\t\t\tconstexpr float preferred_w{ 900.0f };\n\t\t\t\t\tconstexpr float preferred_h{ 700.0f };\n\t\t\t\t\tthis->m_w = std::clamp( preferred_w, min_w, available_w );\n\t\t\t\t\tthis->m_h = std::clamp( preferred_h, min_h, available_h );\n\t\t\t\t\tthis->m_x = std::floor( ( viewport_w - this->m_w ) * 0.5f );\n\t\t\t\t\tthis->m_y = std::floor( ( viewport_h - this->m_h ) * 0.5f );\n\t\t\t\t\tthis->m_user_layout_initialized = true;\n\t\t\t\t}\n\t\t\t\telse\n\t\t\t\t{\n\t\t\t\t\tthis->m_w = std::clamp( this->m_w, min_w, available_w );\n\t\t\t\t\tthis->m_h = std::clamp( this->m_h, min_h, available_h );\n\t\t\t\t\tconst auto max_x = std::max( 0.0f, viewport_w - this->m_w );\n\t\t\t\t\tconst auto max_y = std::max( 0.0f, viewport_h - this->m_h );\n\t\t\t\t\tthis->m_x = std::clamp( this->m_x, 0.0f, max_x );\n\t\t\t\t\tthis->m_y = std::clamp( this->m_y, 0.0f, max_y );\n\t\t\t\t}\n\n\t\t\t\tthis->m_layout_viewport_w = viewport_w_i;\n\t\t\t\tthis->m_layout_viewport_h = viewport_h_i;\n\t\t\t}\n\n'''
    t = t[:start] + stable + t[begin:]

t = t.replace(
    'if ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, false, 200.0f, 200.0f, menu_reveal ) )',
    'if ( !xui::begin_window( "##menu", this->m_x, this->m_y, this->m_w, this->m_h, true, 620.0f, 460.0f, menu_reveal ) )',
    1)

menu.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 3) Settings page: Lua gets its own obvious tab; general gets manual sliders.
# ---------------------------------------------------------------------------
config_cpp = v / "project" / "core" / "rendering" / "impl" / "menu" / "menu.config.cpp"
t = config_cpp.read_text(encoding="utf-8")

# Remove the older Lua block from the bottom of General so it is not hidden or duplicated.
old_lua_start = '\t\txui::layout::separator( );\n\t\txui::text( "Lua 腳本", tokens::col_text );\n'
if old_lua_start in t:
    a = t.index(old_lua_start)
    b = t.find('\n\t\txui::end_child( );', a)
    if b < 0:
        die("could not locate old Lua panel end")
    t = t[:a] + t[b:]

fn_anchor = '''\tvoid menu::draw_config( float group_w )\n\t{\n\t\t( void )group_w;\n'''
if "##lua_dedicated_page" not in t:
    branch = '''\tvoid menu::draw_config( float group_w )\n\t{\n\t\t( void )group_w;\n\n\t\t// Dedicated Lua tab so script import is never hidden below the config list.\n\t\tif ( this->m_subtab == 1 )\n\t\t{\n\t\t\txui::layout::set_cursor( this->m_body_x - this->m_x, this->m_body_y - this->m_y );\n\t\t\tif ( xui::begin_child( "##lua_dedicated_page", this->m_body_w, this->m_body_h, true ) )\n\t\t\t{\n\t\t\t\txui::text( "Lua 腳本管理", tokens::col_text );\n\t\t\t\txui::text( "在這裡直接導入、開啟資料夾或重新載入 Lua 腳本。", tokens::col_text_dim );\n\t\t\t\txui::layout::separator( );\n\n\t\t\t\tconst auto count = scripting::g_lua.script_count( );\n\t\t\t\tconst auto count_text = std::string( "目前已載入 " ) + std::to_string( count ) + " 個 Lua 腳本";\n\t\t\t\txui::text( count_text, tokens::col_text_dim );\n\t\t\t\tconst auto folder_text = std::string( "腳本資料夾：" ) + scripting::g_lua.script_directory( ).string( );\n\t\t\t\txui::text( folder_text, tokens::col_text_dim );\n\n\t\t\t\tconst auto [ avail_w, avail_h ] = xui::layout::avail( );\n\t\t\t\t( void )avail_h;\n\t\t\t\tconst auto& style = xui::ctx( ).style;\n\t\t\t\tconst auto button_w = std::max( 150.0f, ( avail_w - style.item_spacing_x * 2.0f ) / 3.0f );\n\t\t\t\tconstexpr auto button_h{ 32.0f };\n\n\t\t\t\tif ( xui::button( "導入 Lua 腳本##lua_import_dedicated", button_w, button_h ) )\n\t\t\t\t{\n\t\t\t\t\tscripting::g_lua.import_script_dialog( rendering::g_context.get_window( ) );\n\t\t\t\t}\n\t\t\t\txui::layout::same_line( );\n\t\t\t\tif ( xui::button( "開啟腳本資料夾##lua_folder_dedicated", button_w, button_h ) )\n\t\t\t\t{\n\t\t\t\t\tscripting::g_lua.open_script_directory( );\n\t\t\t\t}\n\t\t\t\txui::layout::same_line( );\n\t\t\t\tif ( xui::button( "重新載入 Lua##lua_reload_dedicated", button_w, button_h ) )\n\t\t\t\t{\n\t\t\t\t\tscripting::g_lua.reload_all( );\n\t\t\t\t}\n\n\t\t\t\txui::layout::separator( );\n\t\t\t\txui::text( "導入 .lua 檔後會自動複製到 scripts 資料夾並立即重新載入。", tokens::col_text_dim );\n\t\t\t\txui::text( "修改腳本檔案後也會自動熱重載。", tokens::col_text_dim );\n\t\t\t\txui::end_child( );\n\t\t\t}\n\t\t\treturn;\n\t\t}\n'''
    t = replace_once(t, fn_anchor, branch, "dedicated Lua tab")

# Insert manual width/height sliders right after the General child opens.
size_anchor = '''\t\tif ( !xui::begin_child( "##cfg_panel", this->m_body_w, this->m_body_h, false ) )\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\txui::text_input( "##cfg_search", detail::search_buf, 64, "search configs..." );'''
if "面板寬度##ui_panel_width" not in t:
    size_new = '''\t\tif ( !xui::begin_child( "##cfg_panel", this->m_body_w, this->m_body_h, false ) )\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\txui::text( "介面大小", tokens::col_text );\n\t\txui::text( "也可以直接拖曳主視窗右下角調整大小。", tokens::col_text_dim );\n\t\tconst auto [ ui_vw, ui_vh ] = xdraw::viewport_size( );\n\t\tconst auto max_ui_w = std::max( 620.0f, static_cast< float >( ui_vw ) - 20.0f );\n\t\tconst auto max_ui_h = std::max( 460.0f, static_cast< float >( ui_vh ) - 20.0f );\n\t\txui::slider_float( "面板寬度##ui_panel_width", this->m_w, 620.0f, max_ui_w, "%.0f px" );\n\t\txui::slider_float( "面板高度##ui_panel_height", this->m_h, 460.0f, max_ui_h, "%.0f px" );\n\t\tif ( xui::button( "置中面板##ui_center", 120.0f, 24.0f ) )\n\t\t{\n\t\t\tthis->m_x = std::floor( ( static_cast< float >( ui_vw ) - this->m_w ) * 0.5f );\n\t\t\tthis->m_y = std::floor( ( static_cast< float >( ui_vh ) - this->m_h ) * 0.5f );\n\t\t}\n\t\txui::layout::separator( );\n\n\t\txui::text_input( "##cfg_search", detail::search_buf, 64, "search configs..." );'''
    t = replace_once(t, size_anchor, size_new, "manual UI size controls")

# Old list reservation included space for the former bottom Lua panel. Use that
# space for the new size controls instead, but keep the list comfortably large.
t = t.replace(
    'const auto list_h = std::max( 80.0f, avail_h - btn_h - 96.0f - s.item_spacing_y * 3.0f );',
    'const auto list_h = std::max( 80.0f, avail_h - btn_h - 110.0f - s.item_spacing_y * 3.0f );')

config_cpp.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 4) Complete-ish translation fallback: exact additions plus token fallback.
#    Unknown game/model names remain unchanged, but ordinary UI words are
#    translated even if an exact phrase was missed by the original table.
# ---------------------------------------------------------------------------
loc = v / "project" / "core" / "localization" / "zh_tw.hpp"
t = loc.read_text(encoding="utf-8")

for inc in ("#include <array>", "#include <string>", "#include <cctype>"):
    if inc not in t:
        t = t.replace("#include <string_view>\n", "#include <string_view>\n" + inc + "\n", 1)

extras = {
    "lua": "Lua 腳本",
    "search configs...": "搜尋設定檔...",
    "no configs found": "找不到設定檔",
    "no matches": "沒有符合項目",
    "save": "儲存",
    "reset": "重設",
    "confirm": "確認",
    "delete": "刪除",
    "import": "導入",
    "export": "匯出",
    "shop click": "商店點擊聲",
    "home click": "首頁點擊聲",
    "bell": "鈴聲",
    "killcard": "擊殺卡音效",
    "bullet casing": "彈殼聲",
    "coin pickup": "拾取硬幣聲",
    "item drop": "物品掉落聲",
    "popcan": "汽水罐聲",
    "key press": "按鍵聲",
    "custom": "自訂",
    "classic": "經典",
    "damage": "傷害",
    "both": "兩者",
    "overlay": "覆蓋顯示",
    "sparks": "火花",
    "scoped rifle": "帶鏡步槍",
    "scout": "輕型狙擊槍",
    "auto sniper": "自動狙擊槍",
    "dual elites": "雙持貝瑞塔",
    "five-seven/tec-9": "Five-SeveN / Tec-9",
    "deagle": "沙漠之鷹",
    "revolver": "左輪手槍",
    "molotov": "燃燒瓶",
    "he grenade": "高爆手榴彈",
    "smoke": "煙霧彈",
    "flashbang": "閃光彈",
    "decoy": "誘餌彈",
    "projectile trajectory": "投擲物軌跡",
    "straight throw": "直線投擲",
    "held color": "手持顏色",
    "thrown color": "投出顏色",
    "will damage held color": "手持可造成傷害顏色",
    "will damage thrown color": "投出可造成傷害顏色",
    "dynamic light": "動態光源",
    "z offset": "Z 軸偏移",
    "penetration crosshair": "穿透準星",
    "can penetrate": "可穿透",
    "can pen outline": "可穿透外框",
    "blocked": "無法穿透",
    "blocked outline": "阻擋外框",
    "bhop": "連跳",
    "airstrafe": "空中轉向",
    "fully directional": "全方向",
    "jumpbug": "跳躍 Bug",
    "edgejump": "邊緣跳",
    "edgebug": "邊緣 Bug",
    "pixelsurf": "像素滑行",
    "scoreboard weapons": "計分板武器",
    "bullet impacts": "子彈命中特效",
    "bullet tracers": "子彈曳光",
    "hit marker": "命中標記",
    "hit effect": "命中特效",
    "death sound": "死亡音效",
    "death effect": "死亡特效",
    "type": "類型",
    "volume": "音量",
    "preview": "預覽",
    "file": "檔案",
    "camera fov": "鏡頭視野角",
    "viewmodel fov": "持槍視野角",
    "thirdperson": "第三人稱",
    "thirdperson distance": "第三人稱距離",
    "aspect ratio": "畫面比例",
    "night mode": "夜間模式",
    "fog": "霧效",
    "fog density": "霧效濃度",
    "exposure": "曝光",
    "ambient": "環境光",
    "rain": "下雨",
    "snow": "下雪",
    "amount": "數量",
    "speed": "速度",
    "size": "大小",
    "scale": "縮放",
    "offset": "偏移",
    "primary weapon": "主武器",
    "secondary weapon": "副武器",
    "grenades": "手榴彈",
    "helmet": "頭盔",
    "armor value": "護甲值",
    "auto buy": "自動購買",
    "skin": "造型",
    "sticker": "貼紙",
    "stickers": "貼紙",
    "slot": "欄位",
    "paint kit": "塗裝",
    "rarity": "稀有度",
    "selected": "已選擇",
    "select": "選擇",
    "filter": "篩選",
    "all": "全部",
    "weapon group": "武器群組",
    "local player": "本地玩家",
    "enemy": "敵人",
    "friendly": "隊友",
    "teammate": "隊友",
    "primary color": "主要顏色",
    "secondary color": "次要顏色",
    "animation": "動畫",
    "speed scale": "速度倍率",
    "font": "字型",
    "font size": "字型大小",
    "watermark": "浮水印",
    "keybinds": "按鍵綁定",
    "keybind": "按鍵綁定",
    "hold": "按住",
    "toggle": "切換",
    "always": "永遠",
    "off": "關閉",
    "on": "開啟",
    "open": "開啟",
    "close": "關閉",
    "settings": "設定",
    "theme": "主題",
    "default": "預設",
    "advanced": "進階",
    "cloud": "雲端",
    "folder": "資料夾",
    "new": "新增",
    "load": "載入",
    "loaded": "已載入",
    "refresh": "重新整理",
    "copy": "複製",
    "paste": "貼上",
}

existing = set(re.findall(r'ZH\(\s*"([^"]+)"', t))
lines = []
for en, zh in extras.items():
    if en not in existing:
        esc_en = en.replace('\\', '\\\\').replace('"', '\\"')
        esc_zh = zh.replace('\\', '\\\\').replace('"', '\\"')
        lines.append(f'            ZH( "{esc_en}", "{esc_zh}" );')
if lines:
    t = t.replace("#undef ZH", "\n".join(lines) + "\n#undef ZH", 1)

# Generic word-level fallback for phrases not explicitly listed.
word_map = {
    "auto": "自動", "aim": "瞄準", "aimbot": "自動瞄準", "anti": "反向", "air": "空中",
    "ammo": "彈藥", "amount": "數量", "angle": "角度", "animation": "動畫", "armor": "護甲",
    "back": "返回", "background": "背景", "bar": "條", "base": "基礎", "blocked": "阻擋",
    "bloom": "泛光", "bomb": "炸彈", "box": "框", "bullet": "子彈", "buy": "購買",
    "camera": "鏡頭", "chance": "機率", "chams": "材質透視", "chat": "聊天", "color": "顏色",
    "config": "設定", "control": "控制", "corner": "角落", "crosshair": "準星", "custom": "自訂",
    "damage": "傷害", "death": "死亡", "delay": "延遲", "direction": "方向", "directional": "方向",
    "distance": "距離", "drop": "丟棄", "dynamic": "動態", "effect": "特效", "enemy": "敵人",
    "enabled": "啟用", "exposure": "曝光", "export": "匯出", "file": "檔案", "fill": "填充",
    "filter": "篩選", "flag": "標記", "flags": "標記", "fog": "霧效", "font": "字型",
    "force": "強制", "friendly": "隊友", "fov": "視野角", "full": "完整", "gamma": "伽瑪",
    "glow": "發光", "grenade": "手榴彈", "grenades": "手榴彈", "group": "群組", "head": "頭部",
    "height": "高度", "hit": "命中", "hitchance": "命中率", "hold": "按住", "hud": "介面",
    "icon": "圖示", "impact": "命中", "impacts": "命中", "import": "導入", "indicator": "指示器",
    "intensity": "強度", "item": "物品", "key": "按鍵", "keybind": "按鍵綁定", "keybinds": "按鍵綁定",
    "left": "左側", "light": "光源", "lighting": "光照", "local": "本地", "logs": "記錄",
    "main": "主要", "material": "材質", "max": "最大", "min": "最小", "mode": "模式",
    "movement": "移動", "name": "名稱", "new": "新增", "normal": "一般", "offset": "偏移",
    "opacity": "透明度", "outline": "外框", "overlay": "覆蓋顯示", "penetration": "穿透", "player": "玩家",
    "position": "位置", "preview": "預覽", "primary": "主要", "projectile": "投擲物", "radius": "半徑",
    "ragdoll": "屍體", "recoil": "後座力", "reload": "重新載入", "remove": "移除", "reset": "重設",
    "right": "右側", "save": "儲存", "scale": "縮放", "scene": "場景", "scoreboard": "計分板",
    "search": "搜尋", "secondary": "次要", "select": "選擇", "selected": "已選擇", "settings": "設定",
    "shot": "射擊", "show": "顯示", "size": "大小", "skin": "造型", "skins": "造型",
    "sky": "天空", "skybox": "天空盒", "smooth": "平滑", "sound": "音效", "speed": "速度",
    "spread": "散布", "strength": "強度", "style": "樣式", "team": "隊伍", "text": "文字",
    "theme": "主題", "thickness": "粗細", "thirdperson": "第三人稱", "throw": "投擲", "toggle": "切換",
    "tracer": "曳光", "tracers": "曳光", "trajectory": "軌跡", "type": "類型", "value": "數值",
    "viewmodel": "持槍視角", "visible": "可見", "volume": "音量", "weapon": "武器", "weapons": "武器",
    "wear": "磨損", "weather": "天氣", "width": "寬度", "world": "世界", "zoom": "縮放",
}

if "generic_tr( std::string_view s )" not in t:
    # Replace the original final fallback before adding helper functions.
    t = t.replace("        return s;\n    }\n}", "        return generic_tr( s );\n    }\n}", 1)

    word_cases = []
    for en, zh in word_map.items():
        word_cases.append(f'            WORD( "{en}", "{zh}" );')

    helper = '''
    [[nodiscard]] inline std::string_view word_tr( std::string_view s ) noexcept
    {
        switch ( fnv1a( s ) )
        {
#define WORD(en, zh) case fnv1a(en): if ( s == en ) return zh; break
__WORD_CASES__
#undef WORD
        default:
            break;
        }
        return s;
    }

    [[nodiscard]] inline std::string_view generic_tr( std::string_view s ) noexcept
    {
        bool has_ascii_alpha{};
        for ( const auto c : s )
        {
            if ( ( c >= 'A' && c <= 'Z' ) || ( c >= 'a' && c <= 'z' ) )
            {
                has_ascii_alpha = true;
                break;
            }
        }
        if ( !has_ascii_alpha )
        {
            return s;
        }

        thread_local std::array<std::string, 64> buffers{};
        thread_local std::size_t next_buffer{};
        auto& out = buffers[ next_buffer++ % buffers.size( ) ];
        out.clear( );
        out.reserve( s.size( ) * 2 );

        bool changed{};
        std::size_t i{};
        while ( i < s.size( ) )
        {
            const auto c = static_cast< unsigned char >( s[ i ] );
            const auto alpha = ( c >= 'A' && c <= 'Z' ) || ( c >= 'a' && c <= 'z' );
            if ( !alpha )
            {
                out.push_back( static_cast< char >( c ) );
                ++i;
                continue;
            }

            const auto start = i;
            while ( i < s.size( ) )
            {
                const auto d = static_cast< unsigned char >( s[ i ] );
                if ( !(( d >= 'A' && d <= 'Z' ) || ( d >= 'a' && d <= 'z' )) )
                {
                    break;
                }
                ++i;
            }

            const auto token = s.substr( start, i - start );
            std::string lower( token );
            for ( auto& ch : lower )
            {
                if ( ch >= 'A' && ch <= 'Z' ) ch = static_cast< char >( ch - 'A' + 'a' );
            }

            const auto translated = word_tr( lower );
            if ( translated != lower )
            {
                out.append( translated.data( ), translated.size( ) );
                changed = true;
            }
            else
            {
                out.append( token.data( ), token.size( ) );
            }
        }

        return changed ? std::string_view{ out } : s;
    }

'''.replace("__WORD_CASES__", "\n".join(word_cases))

    tr_pos = t.index("    [[nodiscard]] inline std::string_view tr( std::string_view s ) noexcept")
    t = t[:tr_pos] + helper + t[tr_pos:]

# Prewarm every Chinese glyph used by exact + word translations to avoid
# first-visit atlas rebuild/flicker when switching tabs.
all_zh = re.findall(r'ZH\(\s*"[^"]+"\s*,\s*"([^"]+)"', t)
all_zh += list(word_map.values())
chars = []
seen_chars = set()
for phrase in all_zh:
    for ch in phrase:
        if ord(ch) > 127 and ch not in seen_chars:
            seen_chars.add(ch)
            chars.append(ch)
prewarm = "".join(chars).replace('\\', '\\\\').replace('"', '\\"')
if "prewarm_chars" not in t:
    marker = "    [[nodiscard]] inline std::string_view tr( std::string_view s ) noexcept"
    t = t.replace(marker, f'    inline constexpr std::string_view prewarm_chars{{ "{prewarm}" }};\n\n' + marker, 1)

loc.write_text(t, encoding="utf-8")


# ---------------------------------------------------------------------------
# 5) Pre-rasterize Chinese glyphs once during font setup to reduce tab flicker.
# ---------------------------------------------------------------------------
fonts_cpp = v / "project" / "core" / "rendering" / "impl" / "fonts.cpp"
t = fonts_cpp.read_text(encoding="utf-8")
if '#include <core/localization/zh_tw.hpp>' not in t:
    t = t.replace('#include <pch/pch.hpp>\n', '#include <pch/pch.hpp>\n#include <core/localization/zh_tw.hpp>\n', 1)

if "Prewarm Traditional-Chinese glyphs" not in t:
    anchor = '\n\t}\n\n\tvoid fonts::load_family'
    insert = '''

		// Prewarm Traditional-Chinese glyphs once. Without this, entering a page
		// for the first time can rebuild the CJK atlas mid-frame and visibly flash.
		if ( auto* primary = xdraw::primary_font( ) )
		{
			( void )primary->measure( localization::prewarm_chars );
			if ( primary->fallback )
			{
				primary->fallback->flush_atlas( );
			}
		}
	}

	void fonts::load_family'''
    t = replace_once(t, anchor, insert, "CJK prewarm")

fonts_cpp.write_text(t, encoding="utf-8")

print("[ui-v2] stable resize + Lua tab + translation fallback + CJK prewarm applied")
