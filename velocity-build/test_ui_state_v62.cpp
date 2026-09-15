#include <core/rendering/ui_state_v62.hpp>
#include <iostream>
#include <stdexcept>
using namespace mcb_ui_v62;
static int tests=0;
void check(bool ok,const char* name){if(!ok)throw std::runtime_error(name);std::cout<<"PASS "<<name<<"\n";++tests;}
int main(){
    auto root=fs::temp_directory_path()/("MCB_UI_V62_TEST_"+std::to_string(
#ifdef _WIN32
        GetCurrentProcessId()
#else
        std::chrono::steady_clock::now().time_since_epoch().count()
#endif
    ));
    try {
        schema fields{{"enabled",{"toggle"}},{"dpi",{"range",75,150}},{"theme",{"select",0,5}},
                      {"color",{"color"}},{"tags",{"tags",0,0,2}},{"key",{"key",0,255}}};
        json source={{"enabled",true},{"dpi",100},{"theme",2},{"color",4294967295u},{"tags",{true,false}},{"key",45}};
        json out;std::string e;
        check(normalize(source,fields,out,e)&&out==source,"typed state roundtrip");
        auto wrong=source;wrong["enabled"]="yes";check(!normalize(wrong,fields,out,e),"reject wrong boolean type");
        wrong=source;wrong["dpi"]=json::array();check(!normalize(wrong,fields,out,e),"reject wrong numeric type");
        wrong=source;wrong["tags"]={true,1};check(!normalize(wrong,fields,out,e),"reject mixed tags");
        wrong=source;wrong["tags"]={true};check(!normalize(wrong,fields,out,e),"reject wrong tag count");
        wrong=source;wrong["color"]=-1;check(!normalize(wrong,fields,out,e),"reject negative color");
        wrong=source;wrong["color"]=4294967296ULL;check(!normalize(wrong,fields,out,e),"reject color overflow");
        wrong=source;wrong["dpi"]=18446744073709551615ULL;check(normalize(wrong,fields,out,e)&&out["dpi"]==150,"clamp huge unsigned range without overflow");
        wrong=source;wrong["theme"]=-200;check(normalize(wrong,fields,out,e)&&out["theme"]==0,"clamp enum lower bound");
        wrong=source;wrong["future"]=json{{"anything",1}};check(normalize(wrong,fields,out,e)&&!out.contains("future"),"ignore unknown nonexecutable state");
        check(write_json(root/"basic.json",source,e)&&read_json(root/"basic.json",out,e)&&out==source,"atomic JSON write and read");
        source["dpi"]=125;check(write_json(root/"basic.json",source,e)&&read_json(root/"basic.json",out,e)&&out["dpi"]==125,"replace existing JSON");
        {std::ofstream f(root/"bad.json");f<<"{broken";}
        check(!read_json(root/"bad.json",out,e),"reject malformed JSON");
        {std::ofstream f(root/"deep.json");for(int i=0;i<40;++i)f<<"{\"x\":";f<<0;for(int i=0;i<40;++i)f<<"}";}
        check(!read_json(root/"deep.json",out,e),"reject excessive JSON depth");
        {std::ofstream f(root/"large.json",std::ios::binary);f.seekp(max_file_bytes);f<<' ';}
        check(!read_json(root/"large.json",out,e),"reject oversized file before parsing");
        fs::create_directory(root/"blocked.json");check(!write_json(root/"blocked.json",source,e)&&fs::is_directory(root/"blocked.json"),"failed replacement preserves target");
        config_store store(root/"configs",fields);
        check(store.size()==4&&store.active_index()==0,"initialize four builtins");
        check(store.save(0,source,e)&&store.load(0,out,e)&&out==source,"save and load active config");
        json other=source;other["theme"]=4;
        int a=-1,b=-1;
        check(store.add("Alpha",source,a,e)&&a==4,"create custom A");
        check(store.add("Beta",other,b,e)&&b==5,"create custom B");
        auto beta=store.records()[b]["id"].get<std::string>();
        check(store.erase(a,e)&&store.index(beta)==4&&store.load(store.index(beta),out,e)&&out["theme"]==4,"delete earlier config preserves later stable identity");
        check(!store.erase(0,e)&&store.size()==5,"protect builtins");
        check(store.export_to(root/"export.json",4,other,e),"export selected working snapshot");
        int imported=-1;check(store.import_from(root/"export.json",imported,e)&&store.load(imported,out,e)&&out==other,"import includes actual snapshot values");
        check(store.restore(root/"export.json",out,e)&&out==other,"restore local snapshot values");
        auto snap=snapshot("Bad",source);snap["gameplay_execution"]=true;write_json(root/"invalid_snapshot.json",snap,e);
        const auto before=store.size();check(!store.import_from(root/"invalid_snapshot.json",imported,e)&&store.size()==before,"invalid import leaves database unchanged");
        snap=snapshot("BadType",source);snap["panel_state"]["dpi"]="wrong";write_json(root/"invalid_snapshot.json",snap,e);
        check(!store.import_from(root/"invalid_snapshot.json",imported,e)&&store.size()==before,"bad typed import leaves database unchanged");
        check(store.add("../Name Is Not A Path",source,imported,e)&&!fs::exists(root/"Name Is Not A Path.json"),"names never determine file paths");
        config_store again(root/"configs",fields);check(again.size()==store.size()&&again.records()==store.records(),"database reload roundtrip");
        write_json(root/"corrupt"/"panel_configs_v62.json",json{{"oops",true}},e);
        config_store corrupt(root/"corrupt",fields);check(!corrupt.writable()&&!corrupt.save(0,source,e),"corrupt database is not overwritten");
        read_json(root/"corrupt"/"panel_configs_v62.json",out,e);check(out.contains("oops"),"corrupt original preserved");
        write_json(root/"legacy"/"panel_configs.json",json{{"names",{"Global","Default","Legit","Visuals","Old"}}},e);
        write_json(root/"legacy"/"config_4.json",snapshot("Old",other),e);
        config_store legacy(root/"legacy",fields);check(legacy.migrate_v61(e)&&legacy.size()==5&&legacy.records()[4]["state"]==other,"legacy migration preserves custom snapshot");
        check(fs::exists(root/"legacy"/"config_4.json")&&fs::exists(root/"legacy"/"panel_configs.json"),"legacy migration leaves original files");
        auto drafts=default_drafts();check(validate_drafts(drafts,e),"valid independent editor drafts");
        auto previous=drafts["buffers"][0][1];drafts["buffers"][0][0][0]="new main text";
        check(drafts["buffers"][0][1]==previous&&drafts["buffers"][1][0][0]!="new main text","editor tabs and scripts do not share buffers");
        check(write_json(root/"drafts.json",drafts,e)&&read_json(root/"drafts.json",out,e)&&validate_drafts(out,e)&&out==drafts,"editor draft persistence roundtrip");
        drafts["buffers"][0][0][0]=512;check(!validate_drafts(drafts,e),"reject nontext draft line");
        drafts=default_drafts();drafts["buffers"][0][0][0]=std::string(513,'x');check(!validate_drafts(drafts,e),"bound draft line length");
        std::cout<<"tests_passed="<<tests<<"\nui_render_tested=NO\nin_game_runtime=NOT_TESTED\n";
        fs::remove_all(root);return 0;
    } catch(const std::exception& ex){std::cerr<<"FAIL "<<ex.what()<<"\n";std::error_code ec;fs::remove_all(root,ec);return 1;}
}
