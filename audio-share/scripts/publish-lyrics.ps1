[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\AudioShare.Lyrics\AudioShare.Lyrics.csproj'
$publishDirectory = Join-Path $OutputDirectory 'publish'
$zipPath = Join-Path $OutputDirectory 'FlowCast-Lyrics-win-x64.zip'
$checksumPath = Join-Path $OutputDirectory 'FlowCast-Lyrics-win-x64.zip.sha256'

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
foreach ($path in @($publishDirectory, $zipPath, $checksumPath)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    $parentPath = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not [string]::Equals($parentPath, $OutputDirectory.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unsafe Lyrics package output path: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        $item = Get-Item -LiteralPath $fullPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a reparse-point Lyrics package path: $fullPath"
        }

        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false -p:BuildLyricsGlassRenderer=true `
    --output $publishDirectory

if ($LASTEXITCODE -ne 0) {
    throw 'FlowCast Lyrics publish failed.'
}

$hostExecutable = Join-Path $publishDirectory 'FlowCast Lyrics.exe'
$helperExecutable = Join-Path $publishDirectory 'glass\FlowCast Lyrics Glass.exe'
foreach ($requiredPath in @(
    $hostExecutable,
    $helperExecutable,
    (Join-Path $publishDirectory 'glass\app'),
    (Join-Path $publishDirectory 'glass\runtime')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Standalone Lyrics package is incomplete: missing $requiredPath"
    }
}

$forbiddenPayloads = @(
    'AudioShare.App.exe',
    'router-helper',
    'WebView2',
    'React',
    'liquid-glass-react',
    'rdev',
    'FlowCast-Setup',
    'AudioShare-win-x64',
    'publish-release.ps1',
    'installer',
    'updater'
)
$packageEntries = Get-ChildItem -LiteralPath $publishDirectory -Recurse -Force | ForEach-Object {
    $_.FullName.Substring($publishDirectory.Length).TrimStart('\', '/')
}
foreach ($entry in $packageEntries) {
    foreach ($forbiddenPayload in $forbiddenPayloads) {
        if ($entry.IndexOf($forbiddenPayload, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Forbidden payload in standalone Lyrics package: $entry"
        }
    }
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  FlowCast-Lyrics-win-x64.zip" -NoNewline -Encoding ascii

Write-Host "Created $publishDirectory"
Write-Host "Created $zipPath"
Write-Host "Created $checksumPath"
