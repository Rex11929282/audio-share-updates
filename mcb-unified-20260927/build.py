from pathlib import Path
import subprocess,os,json,hashlib,shutil
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
deps=root/'checked/deps'
run([str(vcpkg),'install','--triplet=x64-windows-static','--x-manifest-root='+str(project),'--x-install-root='+str(deps)],'DEPENDENCIES.log')
from xml.sax.saxutils import escape
triplet=deps/'x64-windows-static'
includes=escape(str(triplet/'include'))
library=escape(str(triplet/'lib'))
# Explicit dependency locations avoid the nested MSBuild triplet propagation issue.
(project/'Directory.Build.targets').write_text('''<Project><ItemDefinitionGroup><ClCompile><AdditionalIncludeDirectories>'''+includes+''';%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories></ClCompile><Link><AdditionalLibraryDirectories>'''+library+''';'''+library+'''\\manual-link;%(AdditionalLibraryDirectories)</AdditionalLibraryDirectories><AdditionalDependencies>'''+library+'''\\*.lib;%(AdditionalDependencies)</AdditionalDependencies></Link></ItemDefinitionGroup></Project>''',encoding='utf-8')
run([str(vs/'MSBuild/Current/Bin/MSBuild.exe'),str(project/'MCB-CS2.vcxproj'),'/m','/p:Configuration=Ship','/p:Platform=x64','/p:VcpkgEnabled=false','/p:VcpkgEnableManifest=false','/p:VcpkgManifestInstall=false','/v:minimal'],'BUILD.log')
dll=root/'checked/bin/MCB_UNIFIED_SOURCE.dll'
assert dll.exists(),'Newly built DLL missing'
target=out/'internal-candidate';target.mkdir(exist_ok=True)
shutil.copy2(dll,target/dll.name)
for pdb in dll.parent.glob('*.pdb'):shutil.copy2(pdb,target/pdb.name)
(out/'BUILD_RESULT.json').write_text(json.dumps({'windows_build':True,'game_tested':False,'dll_sha256':hashlib.sha256(dll.read_bytes()).hexdigest(),'dll_bytes':dll.stat().st_size},indent=2),encoding='utf-8')
