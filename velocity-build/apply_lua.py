from pathlib import Path
import json,sys,shutil
r=Path(sys.argv[1]); base=Path(__file__).parent/'overlay'; v=r/'cs2/velocity-cs2';
s=v/'project/core/scripting';s.mkdir(parents=True,exist_ok=True)
shutil.copy2(base/'scripting.hpp',s/'scripting.hpp');shutil.copy2(base/'lua_manager.cpp',s/'lua_manager.cpp')
e=v/'lua_examples';e.mkdir(exist_ok=True);shutil.copy2(base/'hello.lua',e/'hello.lua');shutil.copy2(base/'snow_hud.lua',e/'snow_hud.lua')
p=v/'vcpkg.json';d=json.loads(p.read_text());deps=d.setdefault('dependencies',[]);names={x if isinstance(x,str) else x.get('name') for x in deps};
if 'lua' not in names:deps.append('lua');p.write_text(json.dumps(d,indent=2)+'\n')
p=v/'velocity-cs2.vcxproj';t=p.read_text()
if 'project\\core\\scripting\\lua_manager.cpp' not in t:t=t.replace('    <ClCompile Include="project\\entry.cpp" />','    <ClCompile Include="project\\core\\scripting\\lua_manager.cpp" />\n    <ClCompile Include="project\\entry.cpp" />',1)
if 'project\\core\\scripting\\scripting.hpp' not in t:t=t.replace('    <ClInclude Include="project\\core\\features\\changer\\changer.hpp" />','    <ClInclude Include="project\\core\\scripting\\scripting.hpp" />\n    <ClInclude Include="project\\core\\features\\changer\\changer.hpp" />',1)
t=t.replace('freetype.lib;Synchronization.lib;','freetype.lib;lua.lib;Synchronization.lib;');p.write_text(t)
p=v/'project/entry.cpp';t=p.read_text()
if '#include <core/scripting/scripting.hpp>' not in t:t=t.replace('#include <core/rendering/rendering.hpp>','#include <core/rendering/rendering.hpp>\n#include <core/scripting/scripting.hpp>',1)
if 'stage: lua' not in t:t=t.replace('\t\tdiag::step( "stage: integrity" );','\t\tdiag::step( "stage: lua" );\n\t\tif ( !scripting::g_lua.initialize( module_handle ) ) diag::write( diag::level::warning, "Lua scripting initialization failed" );\n\n\t\tdiag::step( "stage: integrity" );',1)
if 'scripting::g_lua.shutdown( );' not in t:t=t.replace('\t\tg_minidump_hook.reset( );\n','\t\tg_minidump_hook.reset( );\n\t\tscripting::g_lua.shutdown( );\n',1)
p.write_text(t)
p=v/'project/core/rendering/impl/context.cpp';t=p.read_text()
if '#include <core/scripting/scripting.hpp>' not in t:t=t.replace('#include <core/features/features.hpp>','#include <core/features/features.hpp>\n#include <core/scripting/scripting.hpp>',1)
if 'scripting::g_lua.on_frame( );' not in t:t=t.replace('\t\tfeatures::misc::g_dlight.on_present( );','\t\tfeatures::misc::g_dlight.on_present( );\n\t\tscripting::g_lua.on_frame( );',1)
p.write_text(t)
