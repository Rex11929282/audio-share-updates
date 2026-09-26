from pathlib import Path
import json,hashlib,sys
R=Path(sys.argv[1] if len(sys.argv)>1 else 'checked/native-source')
manifest=[]
def edit(rel,changes):
 p=R/rel;s=p.read_text(encoding='utf-8');before=hashlib.sha256(s.encode()).hexdigest()
 for old,new,count in changes:
  assert s.count(old)==count,(rel,old[:60],s.count(old),count)
  s=s.replace(old,new)
 p.write_text(s,encoding='utf-8',newline='\n');manifest.append({'path':rel,'before':before,'after':hashlib.sha256(s.encode()).hexdigest()})
edit('project/core/features/movement/impl/fastladder.cpp',[
 ('#include "../movement.hpp"','#include "../movement.hpp"\n#include <core/integration/readiness.hpp>',1)])
edit('project/external/config.hpp',[
 ('integer(j["b"]["m"],0,3)','integer(j["b"]["m"],0,2)',1),
 ('請使用舊版匯入功能','不同引擎的設定需要轉換；原檔未修改',1)])
edit('project/external/xdraw/xui/xui.cpp',[
 ('snapshot_pending_input( c.input );','''snapshot_pending_input( c.input );
        // Recompute after a resize even without a new mouse-move event.
        POINT cursor{};const auto window=get_hwnd();
        if(window && GetCursorPos(&cursor) && ScreenToClient(window,&cursor))
            update_mouse_position(c.input,static_cast<float>(cursor.x),static_cast<float>(cursor.y));
        else c.input.mouse_x=c.input.mouse_y=-1.0e8f;
        if(!window || GetForegroundWindow()!=window) {
            c.input.mouse_x=c.input.mouse_y=-1.0e8f;
            c.input.mouse_clicked=c.input.mouse_released=c.input.mouse_down=false;
            c.input.focus_lost=true;
        }''',1)])
edit('project/core/scripting/lua_manager.cpp',[
 ('// The first render frame performs the initial load instead.','// Existing files are loaded only after an explicit reload/import request.',1),
 ('m_scan_requested.store( true, std::memory_order_release );\n        m_initialized = true;','m_scan_requested.store( false, std::memory_order_release );\n        m_initialized = true;',1),
 ('已初始化；腳本會在第一個渲染幀載入：','已初始化；既有腳本須手動載入：',1),
 ('if ( it == m_scripts.end( ) )\n            {\n                auto value','if ( it == m_scripts.end( ) )\n            {\n                if(!force_reload) continue;\n                auto value',1),
 ('''        m_scan_requested.store( true, std::memory_order_release );
        logging::console::print( "[MCB Lua] 已導入，將在下一幀載入：{}", destination.filename( ).string( ) );''','''        {
            std::scoped_lock lock(m_mutex);
            if(!m_initialized)return false;
            const auto write_time=std::filesystem::last_write_time(destination,ec);
            if(ec)return false;
            const auto it=std::find_if(m_scripts.begin(),m_scripts.end(),[&](const auto& value){return value->path==destination;});
            if(it!=m_scripts.end()) {
                unload_script_unlocked(**it);(*it)->write_time=write_time;load_script_unlocked(**it);
            } else {
                auto value=std::make_unique<script>();value->path=destination;value->write_time=write_time;
                load_script_unlocked(*value);m_scripts.push_back(std::move(value));
            }
        }
        logging::console::print( "[MCB 腳本] 已導入選取的腳本：{}", destination.filename( ).string( ) );''',1),
 ('L"Lua 腳本 (*.lua)','L"腳本 (*.lua)',1),('L"導入 Lua 腳本"','L"導入腳本"',1)])
edit('project/core/rendering/impl/menu/menu.config.cpp',[
 ('重新載入腳本 腳本。','重新載入腳本。',1),
 ('目前沒有偵測到腳本','尚未載入任何腳本；按重新載入才會執行資料夾中的檔案',1),
 ('修改腳本檔案後也會自動熱重載。','已明確載入的腳本支援熱重載；新增檔案不會自動執行。',1)])
translations={
 'anim speed':'動畫速度','anisotropy':'各向異性','bottom offset':'底部偏移','chart height':'圖表高度','chart width':'圖表寬度','clantag':'戰隊標籤','defuser':'拆彈器','density':'密度','depth of field':'景深','disable game logs':'停用遊戲記錄','draw distance':'繪製距離','fade':'淡出','fade in':'淡入','far blurry':'遠端模糊距離','far crisp':'遠端清晰距離','fastladder':'快速爬梯','flash alpha':'閃光不透明度','fps':'每秒幀數','gap':'間距','hat':'帽子','hull size':'碰撞體大小','jump steps':'跳躍步驟','line length':'線條長度','map':'地圖','max backtrack':'最大回溯刻度','near blurry':'近端模糊距離','near crisp':'近端清晰距離','offset x':'水平偏移','offset y':'前後偏移','offset z':'垂直偏移','override name':'替換名稱','passes':'處理次數','preserve killfeed':'保留擊殺資訊','ratio':'比例','remove 3d skybox':'移除立體天空盒','remove decals':'移除貼花','remove legs':'隱藏腿部','remove overhead':'隱藏頭頂資訊','remove scope':'移除瞄準鏡遮罩','remove smoke':'移除煙霧','reveal radar':'顯示雷達資訊','scope overlay':'瞄準鏡覆蓋層','scoped fov':'開鏡視野角','scoped fov override':'自訂開鏡視野角','tick rate':'刻度頻率','time':'時間','turbulence':'亂流','user':'使用者','velocity':'移動速度','velocity chart':'速度圖表','velocity counter':'速度數值','viewmodel adjust':'持槍視角調整','wetness':'潮濕效果','wind':'風力','0: loose':'零：寬鬆','1: edge trace (default)':'一：邊緣追蹤（預設）','2: no jump held':'二：未按住跳躍','3: min speed':'三：最低速度','4: strict vz':'四：嚴格垂直速度','CT':'反恐陣營','T':'恐怖陣營','arms chams':'手臂材質','awp':'重型狙擊槍','backtrack chams':'回溯材質','bloom (iz)':'泛光材質','bucket':'水桶帽','distortion (iz)':'扭曲材質','flat (iz)':'平面材質','glow (iz)':'發光材質','hologram (iz)':'全息材質','kasa':'斗笠','liquid (iz)':'液體材質','matte (iz)':'霧面材質','onshot chams':'射擊瞬間材質','outlines (iz)':'輪廓材質','stars':'星光','stattrak':'擊殺計數','five-seven/tec-9':'穿甲手槍／半自動手槍','lua':'腳本'
}
p=R/'project/core/localization/zh_tw.hpp';s=p.read_text(encoding='utf-8');needle='[[nodiscard]] inline std::string_view tr( std::string_view s ) noexcept'
assert needle in s
start=s.index('{',s.index(needle))+1
code='\n        // Reviewed native menu labels; stable internal identifiers stay unchanged.\n'
for en,zh in translations.items():code+='        if(s=='+json.dumps(en,ensure_ascii=False)+') return '+json.dumps(zh,ensure_ascii=False)+';\n'
before=hashlib.sha256(s.encode()).hexdigest();s=s[:start]+code+s[start:];p.write_text(s,encoding='utf-8',newline='\n')
manifest.append({'path':str(p.relative_to(R)),'before':before,'after':hashlib.sha256(s.encode()).hexdigest()})
Path('review-output').mkdir(exist_ok=True)
Path('review-output/followup_source_manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('Checked follow-up source edits:',len(manifest))
