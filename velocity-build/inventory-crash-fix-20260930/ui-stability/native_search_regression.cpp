#include <core/rendering/impl/menu/menu.ui-model.hpp>
namespace rendering::native_search_test {
static std::string to_lower_copy(std::string_view value) {
 std::string result(value);for(auto& ch:result)if(ch>='A'&&ch<='Z')ch=static_cast<char>(ch-'A'+'a');return result;
}
        [[nodiscard]] static std::size_t next_utf8_boundary( std::string_view text, std::size_t begin )
        {
            auto end = begin + 1;
            while ( end < text.size( ) && ( static_cast<unsigned char>( text[ end ] ) & 0xc0 ) == 0x80 ) ++end;
            return std::min( end, text.size( ) );
        }
        [[nodiscard]] static std::string fit_search_text( std::string_view text, float width )
        {
            if ( width <= 0.0f ) return {};
            if ( xdraw::measure_text( text ).first <= width ) return std::string( text );
            const auto suffix_w = xdraw::measure_text( "…" ).first;
            if ( suffix_w > width ) return {};
            std::size_t end{};
            while ( end < text.size( ) )
            {
                const auto next = next_utf8_boundary( text, end );
                if ( xdraw::measure_text( text.substr( 0, next ) ).first + suffix_w > width ) break;
                end = next;
            }
            return std::string( text.substr( 0, end ) ) + "…";
        }
        [[nodiscard]] static std::vector<std::string> wrap_search_path( std::string_view text, float width )
        {
            std::vector<std::string> lines;
            std::size_t begin{}, end{};
            while ( end < text.size( ) )
            {
                const auto next = next_utf8_boundary( text, end );
                if ( end > begin && xdraw::measure_text( text.substr( begin, next - begin ) ).first > width )
                {
                    lines.emplace_back( text.substr( begin, end - begin ) );
                    begin = end;
                }
                end = next;
            }
            if ( begin < text.size( ) ) lines.emplace_back( text.substr( begin ) );
            return lines;
        }
        [[nodiscard]] static std::string search_navigation_path( const ui_model::search_route& route, std::string_view category )
        {
            if ( route.hotkeys ) return "全局工具 / 快捷键总览";
            if ( route.layout_editor ) return "全局工具 / 界面布局编辑器";
            const auto lower = to_lower_copy( category );
            static constexpr const char* weapon_categories[]{ "手枪", "冲锋枪", "步枪", "霰弹枪", "狙击枪", "机枪" };
            if ( route.page == 0 )
            {
                static constexpr const char* modes[]{ "RAGE", "LEGIT", "ANTI-AIM" };
                auto path = std::string( "战斗 / " ) + modes[ std::clamp( route.combat_tab, 0, 2 ) ];
                if ( route.combat_tab >= 2 ) return path;
                const auto marker = lower.find( " - weapon - " );
                if ( marker != std::string::npos )
                {
                    const auto slug = std::string_view( lower ).substr( marker + 12 );
                    for ( const auto& weapon : settings::weapon_profiles::catalog )
                    {
                        if ( slug != weapon.slug ) continue;
                        return path + " / 单把武器 / " + weapon_categories[ settings::weapon_profiles::category_index( weapon.weapon_type ) ] + " / " + weapon.name;
                    }
                    return path + " / 单把武器 / " + std::string( slug );
                }
                if ( ui_model::has( lower, " - " ) && !ui_model::has( lower, " - global" ) )
                    return path + " / 武器分类 / " + weapon_categories[ std::clamp( route.subtab, 0, 5 ) ];
                return path + " / 全部武器";
            }
            if ( route.page == 3 )
            {
                static constexpr const char* groups[]{ "敌人", "队友", "自己" };
                return std::string( "玩家 / " ) + groups[ std::clamp( route.subtab, 0, 2 ) ];
            }
            if ( route.page == 5 )
            {
                static constexpr const char* categories[]{ "全部枪械", "手枪", "冲锋枪", "步枪", "霰弹枪", "狙击枪", "机枪", "刀具", "手套", "探员" };
                auto path = std::string( "库存 / " ) + categories[ std::clamp( route.subtab, 0, 9 ) ];
                if ( ui_model::has( category, "显示选项" ) || lower == "changer ui" ) path += " / 显示选项";
                else if ( ui_model::has( category, "物品详情" ) ) path += " / 物品详情";
                else if ( ui_model::has( category, "分类" ) ) path += " / 分类";
                return path;
            }
            if ( route.page == 7 ) return "脚本 / 脚本管理";
            if ( route.page == 8 ) return "配置 / 配置管理";
            if ( route.page == 9 )
            {
                auto path = std::string( "个人中心 / " ) + ( route.profile_tab == 1 ? "外观" : "个人" );
                if ( ui_model::has( lower, "watermark" ) || ui_model::has( category, "水印" ) ) path += " / 水印";
                return path;
            }
            static constexpr const char* sections[]{ "反馈、移动与系统", "物品、投掷物与战局信息", "环境与天气", "镜头与第一人称", "屏幕组件", "画面移除" };
            return std::string( "其他 / " ) + sections[ std::clamp( route.misc_section, 0, 5 ) ];
        }

}
static void ui_search_display_regression(){
 using namespace rendering::native_search_test;
 const std::string label="库存 / 显示选项 / 单把武器 / 狙击枪 / 测试中文长标签";
 for(float width:{32.0f,64.0f,120.0f,240.0f}) {
  const auto fitted=fit_search_text(label,width);
  check(fitted.empty()||MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,fitted.data(),int(fitted.size()),nullptr,0)>0,"search truncation keeps valid UTF-8 with actual font");
  check(xdraw::measure_text(fitted).first<=width+.01f,"search label fits actual measured badge-free column");
  const auto wrapped=wrap_search_path(label,width);std::string restored;
  for(const auto& line:wrapped){restored+=line;check(xdraw::measure_text(line).first<=width+.01f,"actual-font search breadcrumb line fits");}
  check(restored==label,"wrapped breadcrumbs retain complete route text");
 }
 for(const auto& weapon:settings::weapon_profiles::catalog) for(int mode=0;mode<2;++mode){
  const auto category=std::string(mode==0?"ragebot - weapon - ":"legitbot - weapon - ")+weapon.slug;
  const auto route=rendering::ui_model::category_route(category);
  const auto path=search_navigation_path(route,category);
  check(path.ends_with(weapon.name),"actual weapon route keeps full descriptor name");
 }
}
