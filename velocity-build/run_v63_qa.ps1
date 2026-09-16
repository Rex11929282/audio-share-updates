$ErrorActionPreference = 'Stop'
$root = (Resolve-Path 'mcb-src/cs2/MCB-CS2').Path
$inc = "$root\project"
$stub = (Resolve-Path 'velocity-build/v63-qa/stub').Path
$test = (Resolve-Path 'velocity-build/v63-qa/test_xui_v63.cpp').Path
# Use installed release artifacts, never a similarly named vcpkg source/build file.
$triplet = "$root\vcpkg_installed\x64-windows-static\x64-windows-static"
$depinc = "$triplet\include"
$ft = "$triplet\lib\freetype.lib"
if (-not (Test-Path $ft) -or -not (Test-Path "$depinc\ft2build.h")) { throw 'Installed FreeType dependency missing' }
$compiler = "$env:VSROOT\VC\Tools\Llvm\x64\bin\clang-cl.exe"
if (-not (Test-Path $compiler)) { throw 'Production clang-cl compiler missing' }
$dev = "$env:VSROOT\Common7\Tools\VsDevCmd.bat"
$cmd = '"' + $dev + '" -arch=x64 -host_arch=x64 && "' + $compiler + '" /MT /nologo /EHsc /utf-8 /std:c++latest /DNOMINMAX /I"' + $stub + '" /I"' + $inc + '" /I"' + $depinc + '" "' + $test + '" "' + $inc + '\external\xdraw\xdraw.cpp" "' + $inc + '\external\xdraw\xui\xui.cpp" /Fe:xui_test.exe /link "' + $ft + '" d3d11.lib dxgi.lib windowscodecs.lib ole32.lib user32.lib gdi32.lib && xui_test.exe'
cmd.exe /d /s /c $cmd 2>&1 | Tee-Object -FilePath release/PRODUCTION_XUI_TESTS.txt
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
'production_xui_tests=PASS' | Add-Content release/BUILD_STATUS.txt
