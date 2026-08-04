[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathRooted($DestinationDirectory)) {
    throw 'DestinationDirectory must be an absolute path.'
}

$DestinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$glassDirectory = [System.IO.Path]::GetFullPath((Join-Path $DestinationDirectory 'glass'))
$expectedGlassDirectory = [System.IO.Path]::Combine($DestinationDirectory, 'glass')
$glassParent = [System.IO.Path]::GetDirectoryName($glassDirectory)
if (-not [string]::Equals($glassDirectory, $expectedGlassDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($glassParent, $DestinationDirectory.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unsafe Lyrics Glass output path: $glassDirectory"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$glassProjectDirectory = Join-Path $projectRoot 'lyrics-glass'
$gradleWrapper = Join-Path $glassProjectDirectory 'gradlew.bat'
if (-not (Test-Path -LiteralPath $gradleWrapper -PathType Leaf)) {
    throw "Gradle wrapper is missing: $gradleWrapper"
}

Push-Location $glassProjectDirectory
try {
    & $gradleWrapper createDistributable --no-daemon
    if ($LASTEXITCODE -ne 0) {
        throw 'Lyrics Glass createDistributable failed.'
    }
}
finally {
    Pop-Location
}

$appImageRoot = Join-Path $glassProjectDirectory 'build\compose\binaries\main\app'
if (-not (Test-Path -LiteralPath $appImageRoot -PathType Container)) {
    throw "Lyrics Glass app-image output is missing: $appImageRoot"
}

$helperExecutableName = 'FlowCast Lyrics Glass.exe'
$appImageCandidates = @(
    Get-Item -LiteralPath $appImageRoot
    Get-ChildItem -LiteralPath $appImageRoot -Directory -Recurse
) | Where-Object {
    Test-Path -LiteralPath (Join-Path $_.FullName $helperExecutableName) -PathType Leaf
}

if ($appImageCandidates.Count -ne 1) {
    throw "Expected exactly one complete Lyrics Glass app image, found $($appImageCandidates.Count)."
}

$appImageDirectory = $appImageCandidates[0].FullName
foreach ($requiredPath in @(
    (Join-Path $appImageDirectory $helperExecutableName),
    (Join-Path $appImageDirectory 'app'),
    (Join-Path $appImageDirectory 'runtime')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Lyrics Glass app image is incomplete: missing $requiredPath"
    }
}

if (Test-Path -LiteralPath $glassDirectory) {
    $glassItem = Get-Item -LiteralPath $glassDirectory -Force
    if (($glassItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to replace a reparse-point Lyrics Glass directory: $glassDirectory"
    }

    Remove-Item -LiteralPath $glassDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
Copy-Item -LiteralPath $appImageDirectory -Destination $glassDirectory -Recurse -Force

foreach ($requiredPath in @(
    (Join-Path $glassDirectory $helperExecutableName),
    (Join-Path $glassDirectory 'app'),
    (Join-Path $glassDirectory 'runtime')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Copied Lyrics Glass app image is incomplete: missing $requiredPath"
    }
}

Write-Host "Built $glassDirectory"
