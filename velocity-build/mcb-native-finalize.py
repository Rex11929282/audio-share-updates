"""Resolve a native namespace collision and identify the new build distinctly."""
from pathlib import Path
import hashlib,json,sys
root=Path(sys.argv[1]);root=root/'cs2/MCB-CS2' if (root/'cs2/MCB-CS2/project').is_dir() else root
project=root/'project';changes=[]
def edit(relative,replacements):
 p=project/relative;before=p.read_text(encoding='utf-8-sig');after=before
 for old,new in replacements:
  if after.count(old)!=1:raise ValueError('Unexpected source anchor: '+old)
  after=after.replace(old,new)
 p.write_text(after,encoding='utf-8')
 changes.append({'path':relative,'before_normalized_sha256':hashlib.sha256(before.encode()).hexdigest(),'after_normalized_sha256':hashlib.sha256(after.encode()).hexdigest()})
edit('core/rendering/impl/menu/menu.core.cpp',[(
 'xui::checkbox("開關動畫",animation);','xui::checkbox("開關動畫",mcb::appearance::animation);')])
# These are fresh-profile defaults only. Features remain user-selectable and no
# saved profile is edited. This is NOT counted as a performance optimization.
changes_to_defaults=[]
for name in ('bhop','airstrafe','jumpbug','fastladder'):
 changes_to_defaults.append((f'xui::setting {name}{{ true, {{}}, "{name}", "movement" }};',f'xui::setting {name}{{ false, {{}}, "{name}", "movement" }};'))
for name in ('zeusbot','knifebot'):
 changes_to_defaults.append((f'xui::setting enabled{{ true, {{}}, "{name}", "other \'bots\'" }};',f'xui::setting enabled{{ false, {{}}, "{name}", "other \'bots\'" }};'))
edit('core/settings.hpp',changes_to_defaults)
edit('mcb_version.rc',[
 ('FILEVERSION 1,6,2,0','FILEVERSION 1,7,0,0'),('PRODUCTVERSION 1,6,2,0','PRODUCTVERSION 1,7,0,0'),
 ('"FileVersion", "1.6.2\\0"','"FileVersion", "1.7.0\\0"'),('"ProductVersion", "1.6.2\\0"','"ProductVersion", "1.7.0\\0"'),
 ('"InternalName", "MCB-CS2\\0"','"InternalName", "MCB-Native\\0"'),
 ('"OriginalFilename", "MCB-CS2-v6.2-exact-panel.dll\\0"','"OriginalFilename", "MCB_NATIVE_INTEGRATED.dll\\0"')])
p=root/'MCB_INTEGRATION_MANIFEST.json';j=json.loads(p.read_text(encoding='utf-8'));j['release_reconciliation']=changes
j['fresh_profile_automatic_input_defaults']='inactive; manually selectable; not a performance claim'
p.write_text(json.dumps(j,ensure_ascii=False,indent=2),encoding='utf-8')
print('Native UI name resolution corrected; fresh input defaults inactive; product resource 1.7.0')
