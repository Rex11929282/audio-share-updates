param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$PythonEmbedZip,

    [string]$HostPython
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\AudioShare.App\AudioShare.App.csproj'
$routerHelperBuildPath = Join-Path $projectRoot 'scripts\build-router-helper.ps1'
$thirdPartyNoticesPath = Join-Path $projectRoot 'ThirdPartyNotices.txt'
$dotNetRuntimeLicensePath = Join-Path $projectRoot 'DotNetRuntimeLicense.txt'
$dotNetRuntimeThirdPartyNoticesPath = Join-Path $projectRoot 'DotNetRuntimeThirdPartyNotices.txt'
$publishDirectory = Join-Path $OutputDirectory 'publish'
$zipPath = Join-Path $OutputDirectory 'AudioShare-win-x64.zip'
$checksumPath = Join-Path $OutputDirectory 'AudioShare-win-x64.zip.sha256'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue

dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

$helperBuildArguments = @{
    OutputDirectory = $publishDirectory
    PythonEmbedZip = $PythonEmbedZip
}
if (-not [string]::IsNullOrWhiteSpace($HostPython)) {
    $helperBuildArguments.HostPython = $HostPython
}

& $routerHelperBuildPath @helperBuildArguments

Copy-Item -LiteralPath $thirdPartyNoticesPath -Destination $publishDirectory
Copy-Item -LiteralPath $dotNetRuntimeLicensePath -Destination $publishDirectory
Copy-Item -LiteralPath $dotNetRuntimeThirdPartyNoticesPath -Destination $publishDirectory
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  AudioShare-win-x64.zip" -NoNewline

Write-Host "Created $zipPath"
Write-Host "Created $checksumPath"
