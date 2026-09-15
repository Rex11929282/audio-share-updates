// Included inside namespace rendering, after exact_panel_detail definitions.
namespace exact_panel_v62 {
using json = nlohmann::json;
using mcb_ui_v62::schema;

const schema& fields() {
    static const schema s=[] {
        schema result;
        auto page=[&](const json& p) {
            if (!p.contains("groups")) return;
            for (const auto& g:p["groups"]) for (const auto& item:g["items"]) {
                const auto type=item[0].get<std::string>();
                mcb_ui_v62::field f; f.kind=type;
                if (type=="range") { f.low=item[3][0].get<int>(); f.high=item[3][1].get<int>(); }
                else if (type=="select" || type=="segment") { f.low=0; f.high=int(item[3].size())-1; }
                else if (type=="key") { f.low=0; f.high=255; }
                else if (type=="tags") f.count=std::min(32,int(item[3].size()));
                result.emplace(exact_panel_detail::make_key(p["id"].get<std::string>(),g["title"].get<std::string>(),item[1].get<std::string>()),f);
            }
        };
        const auto& model=exact_panel_detail::model();
        for (const auto& group:model["pageModel"]) for (const auto& p:group["pages"]) page(p);
        for (const auto& p:model["hiddenPages"]) page(p);
        return result;
    }();
    return s;
}
bool sanitize(const json& in,json& out,std::string& error) { return mcb_ui_v62::normalize(in,fields(),out,error); }
const json& defaults() {
    static const json result=[] {
        json out=json::object();
        auto page=[&](const json& p) {
            if (!p.contains("groups")) return;
            for (const auto& g:p["groups"]) for (const auto& item:g["items"]) {
                const auto key=exact_panel_detail::make_key(p["id"].get<std::string>(),g["title"].get<std::string>(),item[1].get<std::string>());
                const auto type=item[0].get<std::string>();
                if (type=="toggle" || type=="range") out[key]=item[2];
                else if (type=="select" || type=="segment") {
                    int index=0; for (int i=0;i<int(item[3].size());++i) if (item[3][i]==item[2]) { index=i; break; }
                    out[key]=index;
                } else if (type=="key") out[key]=exact_panel_detail::key_from_name(item[2].get<std::string>());
                else if (type=="color") out[key]=exact_panel_detail::parse_color(item[2].get<std::string>()).val;
                else if (type=="tags") { out[key]=json::array(); for (int i=0;i<std::min(32,int(item[3].size()));++i)
                    out[key].push_back(std::find(item[2].begin(),item[2].end(),item[3][i])!=item[2].end()); }
            }
        };
        const auto& model=exact_panel_detail::model();
        for (const auto& group:model["pageModel"]) for (const auto& p:group["pages"]) page(p);
        for (const auto& p:model["hiddenPages"]) page(p);
        return out;
    }();
    return result;
}
json expanded(const json& state) { auto out=defaults(); for (auto it=state.begin();it!=state.end();++it) out[it.key()]=it.value(); return out; }
json working() { json out; exact_panel_detail::serialize_state(out); return expanded(out); }
struct feedback { std::string text; bool error{}; float seconds{}; };
feedback& notice() { static feedback n; return n; }
void notify(std::string text,bool error=false) { notice()={std::move(text),error,5.0f}; }
mcb_ui_v62::config_store& repository() {
    static mcb_ui_v62::config_store r(exact_panel_detail::state_path().parent_path(),fields());
    static bool migrated=false;
    if (!migrated) { migrated=true; std::string e; if (!r.migrate_v61(e)) notify(e,true); if (!r.startup_error().empty()) notify(r.startup_error(),true); if (!exact_panel_detail::values().load_error.empty()) notify("Session settings rejected: "+exact_panel_detail::values().load_error,true); }
    return r;
}
void apply(const json& state,bool saved) {
    auto full=expanded(state); auto& s=exact_panel_detail::values();
    // Update in place: XUI bind registry and popup pointers retain valid addresses.
    for (auto& [k,v]:s.toggles) if(v && full.contains(k)) v->value=full[k].get<bool>();
    for (auto& [k,v]:s.numbers) if(full.contains(k)) v=full[k].get<int>();
    for (auto& [k,v]:s.choices) if(full.contains(k)) v=full[k].get<int>();
    for (auto& [k,v]:s.keys) if(full.contains(k)) v=full[k].get<int>();
    for (auto& [k,v]:s.colors) if(full.contains(k)) v=xdraw::color{full[k].get<std::uint32_t>()};
    for (auto& [k,v]:s.tags) if(full.contains(k)) for(int i=0;i<v.count;++i) v.selected[i]=full[k][i].get<bool>();
    s.disk=std::move(full); s.dirty=!saved;
    xui::overlays::close_all(); xui::ctx().active_text_input=xui::null_id; xui::ctx().active_slider_edit=xui::null_id;
}
bool dirty() { auto& r=repository(); return working()!=expanded(r.records()[r.active_index()]["state"]); }
bool confirm(const wchar_t* text) {
    return MessageBoxW(g_context.get_window(),text,L"MCB - Confirm",MB_YESNO|MB_ICONWARNING|MB_DEFBUTTON2)==IDYES;
}
bool save(int index) {
    auto& r=repository(); auto state=working(); std::string e;
    if (!r.save(index,state,e)) { notify(e,true); return false; }
    if (!mcb_ui_v62::write_json(exact_panel_detail::state_path(),state,e)) { notify("Config saved, session file failed: "+e,true); return false; }
    exact_panel_detail::values().disk=state; exact_panel_detail::values().dirty=false;
    notify("Saved "+r.name(index)); return true;
}
bool load(int index) {
    if (dirty() && !confirm(L"Discard unsaved UI changes and load this config?")) return false;
    auto& r=repository(); json state; std::string e;
    if (!r.load(index,state,e)) { notify(e,true); return false; }
    apply(state,true); notify("Loaded "+r.name(index)); return true;
}
std::filesystem::path choose_file(bool saving) {
    wchar_t path[32768]=L"MCB-ui-config.json";
    OPENFILENAMEW dialog{}; dialog.lStructSize=sizeof(dialog); dialog.hwndOwner=g_context.get_window();
    dialog.lpstrFilter=L"MCB UI Config (*.json)\0*.json\0\0"; dialog.lpstrFile=path; dialog.nMaxFile=32768;
    dialog.lpstrDefExt=L"json"; dialog.lpstrTitle=saving?L"Export MCB UI Config":L"Import MCB UI Config";
    dialog.Flags=OFN_EXPLORER|OFN_NOCHANGEDIR|OFN_PATHMUSTEXIST|OFN_DONTADDTORECENT|(saving?OFN_OVERWRITEPROMPT:OFN_FILEMUSTEXIST);
    const bool ok=saving?GetSaveFileNameW(&dialog):GetOpenFileNameW(&dialog);
    if (!ok) { if(CommDlgExtendedError()) notify("File dialog failed",true); return {}; }
    return std::filesystem::path(path);
}
struct config_view { int selected{}; std::string search; std::string name{"My Config"}; };
config_view& view() { static config_view s; return s; }
void draw_configs(float w,float h) {
    auto& r=repository(); auto& s=view(); s.selected=std::clamp(s.selected,0,int(r.size())-1);
    if(!xui::begin_child("##v62_configs",w,h,true)) return;
    xui::text("Configs",tokens::col_text); xui::text(dirty()?"Unsaved changes":"All changes saved",dirty()?tokens::col_accent:tokens::col_text_dim);
    xui::text_input("##v62_config_name",s.name,96,"New config name");
    std::string e;
    if(xui::button("New",64,26)) { if(r.add(s.name,working(),s.selected,e)) notify("Created "+s.name); else notify(e,true); }
    xui::layout::same_line();
    if(xui::button("Refresh",74,26)) { if(r.reload(e)) { s.selected=std::min(s.selected,int(r.size())-1); notify("Config list refreshed; working UI unchanged"); } else notify(e,true); }
    xui::layout::same_line();
    if(xui::button("Load",64,26)) load(s.selected);
    xui::layout::same_line();
    if(xui::button("Save",64,26)) save(s.selected);
    xui::layout::same_line();
    if(xui::button("Delete",70,26)) {
        if(s.selected<4) notify("Built-in configs cannot be deleted",true);
        else if(confirm(L"Delete this custom UI config? This cannot be undone.")) { if(r.erase(s.selected,e)) {s.selected=std::min(s.selected,int(r.size())-1);notify("Deleted config");}else notify(e,true); }
    }
    xui::layout::new_line();
    if(xui::button("Export JSON",102,26)) { const auto p=choose_file(true); if(!p.empty()) {if(r.export_to(p,s.selected,working(),e))notify("Exported UI snapshot");else notify(e,true);} }
    xui::layout::same_line();
    if(xui::button("Import JSON",102,26)) { const auto p=choose_file(false); if(!p.empty()){if(r.import_from(p,s.selected,e))notify("Imported config; press Load to apply");else notify(e,true);} }
    xui::layout::same_line();
    if(xui::button("Local Snapshot",114,26)) {if(r.export_to(r.directory()/L"local_snapshot.json",s.selected,working(),e))notify("Snapshot saved");else notify(e,true);}
    xui::layout::same_line();
    if(xui::button("Restore Snapshot",124,26)) {
        if(!dirty() || confirm(L"Discard unsaved UI changes and restore the local snapshot?")) {json state;if(r.restore(r.directory()/L"local_snapshot.json",state,e)){apply(state,false);notify("Snapshot restored; not yet saved to config");}else notify(e,true);}
    }
    xui::layout::separator(); xui::text_input("##v62_config_search",s.search,64,"Search configs...");
    int visible=0;
    for(int i=0;i<int(r.size());++i) {
        const auto name=r.name(i); if(!exact_panel_detail::search_match(s.search,"",name))continue;
        ++visible; const auto label=(i==r.active_index()?"[Active] ":"")+name+"##v62_cfg_"+r.records()[i]["id"].get<std::string>();
        if(xui::button(label,std::max(100.0f,xui::layout::avail().first),28))s.selected=i;
    }
    if(!visible)xui::text("No configs match your search",tokens::col_text_dim);
    xui::layout::separator();xui::text("Selected: "+r.name(s.selected),tokens::col_accent);
    xui::text("Stable IDs / local UI state only",tokens::col_text_dim);xui::end_child();
}
struct editor_view { int selected{},tab{}; std::string search; json draft=mcb_ui_v62::default_drafts(); json saved=draft; bool loaded{}; };
editor_view& editor(){static editor_view s;return s;}
bool reload_editor(bool initial=false) {
    auto& s=editor(); const auto p=repository().directory()/L"editor_drafts_v62.json";std::error_code ec;
    if(!std::filesystem::exists(p,ec) && initial) { s.loaded=true; return true; }
    json candidate;std::string e;
    if(!mcb_ui_v62::read_json(p,candidate,e)||!mcb_ui_v62::validate_drafts(candidate,e)){notify(e,true);s.loaded=true;return false;}
    s.draft=std::move(candidate);s.saved=s.draft;s.loaded=true;notify("Local drafts reloaded; no execution");return true;
}
void draw_scripts(float w,float h){
    auto& s=editor();if(!s.loaded)reload_editor(true);
    if(!xui::begin_child("##v62_scripts",w,h,true))return;
    xui::text("Scripts - local draft editor",tokens::col_text);
    xui::text("EXECUTION DISABLED",xdraw::color{255,174,95,255});
    xui::text_input("##v62_script_search",s.search,64,"Search script drafts...");
    if(xui::button("Save Drafts",110,26)){std::string e;if(mcb_ui_v62::write_json(repository().directory()/L"editor_drafts_v62.json",s.draft,e)){s.saved=s.draft;notify("Drafts saved locally");}else notify(e,true);}
    xui::layout::same_line();
    if(xui::button("Reload",80,26)){if(s.draft==s.saved||confirm(L"Discard unsaved draft edits and reload saved local drafts?"))reload_editor();}
    xui::layout::same_line();xui::text(s.draft==s.saved?"Saved / Idle":"Unsaved draft / Idle",tokens::col_text_dim);
    xui::layout::separator();
    static const char* names[]{"main.lua","settings.lua","hud_preview.lua","example.lua"};
    int visible=0;
    for(int i=0;i<4;++i){if(!exact_panel_detail::search_match(s.search,"",names[i]))continue;++visible;
        if(xui::button(std::string(s.selected==i?"> ":"")+names[i]+"##v62_script_"+std::to_string(i),std::max(100.0f,xui::layout::avail().first),25))s.selected=i;}
    if(!visible)xui::text("No matching script drafts",tokens::col_text_dim);
    xui::layout::separator();
    if(xui::button(s.tab==0?"[main.lua]##v62_main":"main.lua##v62_main",100,26))s.tab=0;
    xui::layout::same_line();if(xui::button(s.tab==1?"[settings.lua]##v62_set":"settings.lua##v62_set",125,26))s.tab=1;
    auto& lines=s.draft["buffers"][s.selected][s.tab];
    xui::layout::separator();
    for(int i=0;i<int(lines.size());++i){xui::push_id("v62_editor_"+std::to_string(s.selected)+"_"+std::to_string(s.tab)+"_"+std::to_string(i));
        xui::text_input(std::to_string(i+1),lines[i].get_ref<std::string&>(),512,"");xui::pop_id();}
    if(xui::button("Add Line",90,25)){if(lines.size()<64)lines.push_back("");else notify("Maximum 64 lines per draft",true);}
    xui::layout::same_line();if(xui::button("Remove Last Line",130,25)){if(lines.size()>1)lines.erase(lines.end()-1);}
    xui::layout::separator();xui::text("UTF-8 / Lua text only / "+std::to_string(lines.size())+" lines",tokens::col_text_dim);
    xui::text("Drafts are separate from the existing Lua runtime.",tokens::col_text_dim);xui::end_child();
}
} // namespace exact_panel_v62
namespace exact_panel_bridge {
void save_active(){auto& r=exact_panel_v62::repository();exact_panel_v62::save(r.active_index());}
bool has_unsaved(){return exact_panel_v62::dirty();}
void config_selector(){
    auto& r=exact_panel_v62::repository();auto& s=exact_panel_v62::view();
    static int selected{};selected=r.active_index();
    std::vector<std::string> names;std::vector<const char*> ptrs;
    for(int i=0;i<int(r.size());++i)names.push_back(r.name(i));for(auto& n:names)ptrs.push_back(n.c_str());
    if(xui::combo("##v62_active_config",selected,ptrs.data(),int(ptrs.size()),148.0f)){s.selected=selected;exact_panel_v62::load(selected);}
}
void draw_feedback(float x,float y,float w,float h){
    auto& n=exact_panel_v62::notice();if(n.seconds<=0 || n.text.empty())return;
    n.seconds=std::max(0.0f,n.seconds-xdraw::delta_time());
    const auto pos=std::clamp(choice("settings","Interface","Notification Position",3),0,3);
    const auto box_w=std::min(410.0f,w-24.0f);const auto bx=(pos==1||pos==3)?x+w-box_w-12:x+12;
    const auto by=pos>=2?y+h-52:y+76;auto& dl=xui::draw::current();
    dl.rect_filled(bx,by,box_w,38,xdraw::color{7,17,26,245},xdraw::corner_radius{6});
    dl.rect(bx,by,box_w,38,n.error?xdraw::color{255,126,112,255}:tokens::col_accent,xdraw::corner_radius{6},1);
    dl.text(bx+10,by+12,xui::truncate(n.text,box_w-20),tokens::col_text);
}
}
