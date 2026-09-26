from pathlib import Path
import hashlib,json,sys
root=Path(sys.argv[1]);p=root/'project/core/features/movement/impl/slowwalk.cpp'
s=p.read_text(encoding='utf-8');before=hashlib.sha256(s.encode()).hexdigest()
old='#include <core/features/features.hpp>'
assert s.count(old)==1
s=s.replace(old,old+'\n#include <core/integration/readiness.hpp>')
old='const auto base = cmd->csgo_user_cmd.mutable_base( );'
assert s.count(old)==1
s=s.replace(old,old+'\n        if(!base) return;')
p.write_text(s,encoding='utf-8',newline='\n')
checks=[]
for unit in (root/'project/core/features/movement/impl').glob('*.cpp'):
 text=unit.read_text(encoding='utf-8')
 if 'mcb::integration::movement_feature' in text:
  assert '#include <core/integration/readiness.hpp>' in text,unit.name+' missing readiness dependency'
  checks.append(unit.name)
Path('review-output/compile_fixes.json').write_text(json.dumps({'path':str(p.relative_to(root)),'before':before,'after':hashlib.sha256(s.encode()).hexdigest(),'movement_dependencies_checked':checks},indent=2),encoding='utf-8')
print('Movement compilation dependency checks:',len(checks))
