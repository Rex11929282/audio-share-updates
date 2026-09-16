from pathlib import Path
r=Path(__file__).parent
s=(r/'test_ui_state_v62.cpp').read_text(encoding='utf-8-sig').replace('panel_configs_v62.json','panel_configs_v63.json')
s=s.replace('{"key",{"key",0,255}}','{"key",{"key",0,255}},{"__native",{"native"}}')
needle='        std::cout<<"tests_passed="'
idx=s.index(needle)
s=s[:idx]+'''        json native=source;native["__native"]={{"version",1},{"fields",{{"1234abcd",true},{"00112233",37}}}};
        check(normalize(native,fields,out,e)&&out==native,"native registry values roundtrip");
        auto malformed=native;malformed["__native"]["fields"]["../../evil"]=true;
        check(!normalize(malformed,fields,out,e),"reject malformed native field IDs");
        int native_index=-1;
        check(store.add("Native Profile",native,native_index,e)&&store.load(native_index,out,e)&&out==native,"profiles retain actual native parameter values");
        check(store.export_to(root/"native.json",native_index,native,e)&&store.restore(root/"native.json",out,e)&&out==native,"export and restore native plus UI settings");
'''+s[idx:]
(r/'test_data_v63.cpp').write_text(s,encoding='utf-8')
