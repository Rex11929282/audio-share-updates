from pathlib import Path
import hashlib,json,sys
root=Path(sys.argv[1]);changes=[]
def edit(rel,items):
 p=root/rel;s=p.read_text(encoding='utf-8');before=hashlib.sha256(s.encode()).hexdigest()
 for old,new in items:
  assert s.count(old)==1,(rel,old)
  s=s.replace(old,new)
 p.write_text(s,encoding='utf-8',newline='\n')
 changes.append({'path':rel,'before':before,'after':hashlib.sha256(s.encode()).hexdigest()})
edit('project/core/features/movement/impl/slowwalk.cpp',[
 ('#include <core/features/features.hpp>','#include <core/features/features.hpp>\n#include <core/integration/readiness.hpp>'),
 ('const auto base = cmd->csgo_user_cmd.mutable_base( );','const auto base = cmd->csgo_user_cmd.mutable_base( );\n        if(!base) return;')])
edit('project/core/rendering/impl/menu/menu.config.cpp',[
 ('#include <pch/pch.hpp>','#include <pch/pch.hpp>\n#include <shellapi.h>')])
checks=[]
for unit in (root/'project/core/features/movement/impl').glob('*.cpp'):
 text=unit.read_text(encoding='utf-8')
 if 'mcb::integration::movement_feature' in text:
  assert '#include <core/integration/readiness.hpp>' in text,unit.name+' missing readiness dependency'
  checks.append(unit.name)
Path('review-output/compile_fixes.json').write_text(json.dumps({'changes':changes,'movement_dependencies_checked':checks},indent=2),encoding='utf-8')
print('Movement compilation dependency checks:',len(checks))
