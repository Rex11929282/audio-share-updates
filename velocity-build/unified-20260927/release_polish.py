"""Finalize user-visible status strings and UTF-8 safe measured truncation."""
from pathlib import Path
import json,hashlib
from mcb_unify import once,function

def apply(root):
    changes=[]
    p=root/'project/core/mcb/mcb_presets.cpp';s=p.read_text(encoding='utf-8-sig');before=s
    terms={
      'CFG verification failed: missing fields object':'設定驗證失敗：缺少參數物件',
      'CFG verification failed: registry field count changed':'設定驗證失敗：參數數量改變',
      'CFG verification failed: invalid registry key':'設定驗證失敗：無效參數鍵',
      'CFG verification failed: non-preset field changed':'設定驗證失敗：預設以外的參數遭到更動',
      'CFG verification failed: mode exclusivity mismatch':'設定驗證失敗：瞄準模式互斥不符',
      'CFG verification failed: legit group mismatch':'設定驗證失敗：低調武器參數不符',
      'CFG verification failed: legit hitbox mismatch':'設定驗證失敗：低調命中部位不符',
      'CFG verification failed: rage group mismatch':'設定驗證失敗：進攻武器參數不符',
      'CFG verification failed: rage hitbox mismatch':'設定驗證失敗：進攻命中部位不符',
      'CFG verification failed: HVH anti-aim mismatch':'設定驗證失敗：私人對抗反瞄準不符',
      '; rollback verification FAILED':'；回復原設定的驗證失敗',
      'Native CFG save failed':'原生功能設定保存失敗',
      'Native CFG readback verification failed':'原生功能設定讀回驗證失敗',
      'MCB Legit: PARTIAL preset applied; FULL native CFG saved':'MCB 低調：已套用指定參數並保存完整功能設定',
      'MCB Rage: PARTIAL preset applied; FULL native CFG saved':'MCB 進攻：已套用指定參數並保存完整功能設定',
      'MCB HVH: PARTIAL preset applied; FULL native CFG saved':'MCB 私人對抗：已套用指定參數並保存完整功能設定',
    }
    for old,new in terms.items():
        expected=3 if old.startswith('; rollback') else 1
        assert s.count('"'+old+'"')==expected,old
        s=s.replace('"'+old+'"','"'+new+'"')
    p.write_text(s,encoding='utf-8',newline='\n');changes.append({'path':str(p.relative_to(root)),'before':hashlib.sha256(before.encode()).hexdigest(),'after':hashlib.sha256(s.encode()).hexdigest()})
    p=root/'project/external/xdraw/xui/xui.cpp';s=p.read_text(encoding='utf-8-sig');before=s
    s=function(s,'std::string_view truncate( std::string_view text, float max_width )',r'''std::string_view truncate( std::string_view text, float max_width )
    {
        text=localization::tr(text);
        if(!std::isfinite(max_width)||max_width<=0.0f)return {};
        if(xdraw::measure_text(text).first<=max_width)return text;
        const auto ellipsis=xdraw::measure_text("...").first;
        if(max_width<ellipsis)return {};
        const auto available=max_width-ellipsis;
        std::size_t lo=0,hi=text.size(),best=0;
        while(lo<=hi && hi!=static_cast<std::size_t>(-1)){
            const auto mid=lo+(hi-lo)/2;
            auto boundary=mid;
            while(boundary>0 && boundary<text.size() && (static_cast<unsigned char>(text[boundary])&0xC0)==0x80)--boundary;
            if(xdraw::measure_text(text.substr(0,boundary)).first<=available){best=boundary;lo=mid+1;}
            else {if(mid==0)break;hi=mid-1;}
        }
        auto& scratch=get_ctx().truncation_scratch;
        scratch.assign(text.data(),best);scratch.append("...");return scratch;
    }''')
    p.write_text(s,encoding='utf-8',newline='\n');changes.append({'path':str(p.relative_to(root)),'before':hashlib.sha256(before.encode()).hexdigest(),'after':hashlib.sha256(s.encode()).hexdigest()})
    p=Path(__file__).parent/'tests/native_ui.cpp';s=p.read_text(encoding='utf-8')
    s=once(s,'  // Redirect this test process',r'''  for(int width=0;width<=200;++width){
   const auto text=std::string(xui::truncate("中文介面透明玻璃與角色設定",float(width)));
   check(text.empty()||MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),int(text.size()),nullptr,0)>0,"truncated Chinese remains valid UTF-8");
   check(xdraw::measure_text(text).first<=float(width)+0.01f,"truncated text fits its measured width");
  }
  // Redirect this test process''')
    s=once(s,'   const auto applied=mcb::presets::apply_and_save(kind);',r'''   const auto applied=mcb::presets::apply_and_save(kind);
   auto message=applied.message;if(message.starts_with("MCB"))message.erase(0,3);
   check(std::none_of(message.begin(),message.end(),[](unsigned char c){return (c>='A'&&c<='Z')||(c>='a'&&c<='z');}),"native preset status is Chinese");''')
    anchor='out.rect=xui::layout::current_window()->last_item;'
    assert s.count(anchor)==2, 'expected root and child rectangle captures'
    s=s.replace(anchor,anchor+'out.rect.x=std::floor(out.rect.x+xui::layout::current_window()->bounds.x);out.rect.y=std::floor(out.rect.y+xui::layout::current_window()->bounds.y);')
    p.write_text(s,encoding='utf-8',newline='\n');changes.append({'test_harness':p.name,'after':hashlib.sha256(s.encode()).hexdigest()})
    (root/'MCB_RELEASE_POLISH.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2),encoding='utf-8')
    return changes
