from pathlib import Path
import subprocess,os,json,hashlib,shutil,sys
root=Path.cwd();out=root/'review-output';out.mkdir(exist_ok=True)
project=root/'checked/native-source'
vswhere=Path(os.environ['ProgramFiles(x86)'])/'Microsoft Visual Studio/Installer/vswhere.exe'
vs=Path(subprocess.check_output([str(vswhere),'-latest','-products','*','-requires','Microsoft.Component.MSBuild','-property','installationPath'],text=True).strip())
vcpkg=vs/'VC/vcpkg/vcpkg.exe'
if not vcpkg.exists():vcpkg=Path(os.environ['VCPKG_INSTALLATION_ROOT'])/'vcpkg.exe'
def run(args,log):
 with (out/log).open('w',encoding='utf-8') as stream:
  process=subprocess.Popen(args,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
  for line in process.stdout:print(line,end='',flush=True);stream.write(line)
  code=process.wait()
  if code:raise SystemExit(code)
run([sys.executable,str(root/'mcb-unified-20260927/review_fixes.py'),str(project)],'SOURCE_FIXES.log')
run([sys.executable,str(root/'mcb-unified-20260927/compile_fixes.py'),str(project)],'COMPILE_FIXES.log')
deps=root/'checked/deps'
run([str(vcpkg),'install','--triplet=x64-windows-static','--x-manifest-root='+str(project),'--x-install-root='+str(deps)],'DEPENDENCIES.log')
from xml.sax.saxutils import escape
triplet=deps/'x64-windows-static'
includes=escape(str(triplet/'include'))
library=escape(str(triplet/'lib'))
(project/'Directory.Build.targets').write_text('''<Project><ItemDefinitionGroup><ClCompile><AdditionalIncludeDirectories>'''+includes+''';%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories></ClCompile><Link><AdditionalLibraryDirectories>'''+library+''';'''+library+'''\\manual-link;%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories><AdditionalDependencies>'''+library+'''\\*.lib;%(AdditionalDependencies)</AdditionalDependencies></Link></ItemDefinitionGroup></Project>''',encoding='utf-8')
run([str(vs/'MSBuild/Current/Bin/MSBuild.exe'),str(project/'MCB-CS2.vcxproj'),'/m','/p:Configuration=Ship','/p:Platform=x64','/p:VcpkgEnabled=false','/p:VcpkgEnableManifest=false','/p:VcpkgManifestInstall=false','/v:minimal'],'BUILD.log')
tests=root/'mcb-unified-20260927'
forced=out/'test_config.hpp'
forced.write_text('#define MCB_CONFIG_TEST_DIRECTORY L"mcb-native-test-settings"\n',encoding='utf-8')
clang=vs/'VC/Tools/Llvm/x64/bin/clang-cl.exe'
if not clang.exists():clang=Path(shutil.which('clang-cl'))
args=[str(clang),'/nologo','/std:c++latest','/EHsc','/utf-8','/MT','/W1','/FI'+str(forced),'/I'+str(project/'project'),'/I'+str(project/'project/external/phnt'),'/I'+str(triplet/'include'),str(tests/'test_native.cpp'),str(project/'project/external/xdraw/xdraw.cpp'),str(project/'project/external/xdraw/xui/xui.cpp'),str(project/'project/core/mcb/mcb_presets.cpp'),'/Fe:'+str(out/'native_test.exe'),'/link','/LIBPATH:'+str(triplet/'lib'),'freetype.lib','d3d11.lib','dxgi.lib','d3dcompiler.lib','windowscodecs.lib','ole32.lib','user32.lib','gdi32.lib','shell32.lib','advapi32.lib']
batch=out/'native_test.cmd'
batch.write_text('@echo off\ncall "'+str(vs/'VC/Auxiliary/Build/vcvars64.bat')+'"\nif errorlevel 1 exit /b %errorlevel%\n'+subprocess.list2cmdline(args)+'\nif errorlevel 1 exit /b %errorlevel%\n"'+str(out/'native_test.exe')+'"\nexit /b %errorlevel%\n',encoding='utf-8')
run(['cmd','/d','/c',str(batch)],'NATIVE_TEST.log')
dll=root/'checked/bin/MCB_UNIFIED_SOURCE.dll'
assert dll.exists(),'Newly built DLL missing'
target=out/'internal-candidate';target.mkdir(exist_ok=True)
shutil.copy2(dll,target/dll.name)
for pdb in dll.parent.glob('*.pdb'):shutil.copy2(pdb,target/pdb.name)
(out/'BUILD_RESULT.json').write_text(json.dumps({'windows_build':True,'game_tested':False,'dll_sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'dll_bytes':dll.stat().st_size},indent=2),encoding='utf-8')
