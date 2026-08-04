[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DestinationDirectory,

    [Parameter(DontShow)]
    [string]$SourceAppImageDirectory
)

$ErrorActionPreference = 'Stop'

function Assert-NoReparseAncestors {
    param([string]$Path, [string]$Description)

    for ($directory = [System.IO.DirectoryInfo]::new([System.IO.Path]::GetFullPath($Path));
         $null -ne $directory;
         $directory = $directory.Parent) {
        if ($directory.Exists -and
            ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description contains a reparse-point ancestor: $($directory.FullName)"
        }
    }
}

function Assert-NoReparseTree {
    param([string]$Path, [string]$Description)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push([System.IO.Path]::GetFullPath($Path))
    while ($pending.Count -gt 0) {
        $currentPath = $pending.Pop()
        $currentItem = Get-Item -LiteralPath $currentPath -Force
        if (($currentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description contains a reparse point: $currentPath"
        }

        if ($currentItem.PSIsContainer) {
            foreach ($child in Get-ChildItem -LiteralPath $currentPath -Force) {
                if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "$Description contains a reparse point: $($child.FullName)"
                }

                if ($child.PSIsContainer) {
                    $pending.Push($child.FullName)
                }
            }
        }
    }
}

if (-not [System.IO.Path]::IsPathRooted($DestinationDirectory)) {
    throw 'DestinationDirectory must be an absolute path.'
}

$DestinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$glassDirectory = [System.IO.Path]::GetFullPath((Join-Path $DestinationDirectory 'glass'))
$expectedGlassDirectory = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($DestinationDirectory, 'glass'))
if (-not [string]::Equals($glassDirectory, $expectedGlassDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unsafe Lyrics Glass output path: $glassDirectory"
}
Assert-NoReparseAncestors $DestinationDirectory 'DestinationDirectory'

$projectRoot = Split-Path -Parent $PSScriptRoot
$glassProjectDirectory = Join-Path $projectRoot 'lyrics-glass'
$gradleWrapper = Join-Path $glassProjectDirectory 'gradlew.bat'
if (-not (Test-Path -LiteralPath $gradleWrapper -PathType Leaf)) {
    throw "Gradle wrapper is missing: $gradleWrapper"
}

if ([string]::IsNullOrWhiteSpace($SourceAppImageDirectory)) {
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
    Assert-NoReparseTree $appImageRoot 'Lyrics Glass app-image output'

    $appImageCandidates = @(
        Get-Item -LiteralPath $appImageRoot
        Get-ChildItem -LiteralPath $appImageRoot -Directory -Recurse
    ) | Where-Object {
        Test-Path -LiteralPath (Join-Path $_.FullName 'FlowCast Lyrics Glass.exe') -PathType Leaf
    }

    if ($appImageCandidates.Count -ne 1) {
        throw "Expected exactly one complete Lyrics Glass app image, found $($appImageCandidates.Count)."
    }

    $appImageDirectory = $appImageCandidates[0].FullName
}
else {
    if (-not [System.IO.Path]::IsPathRooted($SourceAppImageDirectory)) {
        throw 'SourceAppImageDirectory must be an absolute path.'
    }

    $appImageDirectory = [System.IO.Path]::GetFullPath($SourceAppImageDirectory)
    if (-not (Test-Path -LiteralPath $appImageDirectory -PathType Container)) {
        throw "Prepared Lyrics Glass app image is missing: $appImageDirectory"
    }
    Assert-NoReparseAncestors $appImageDirectory 'SourceAppImageDirectory'
    Assert-NoReparseTree $appImageDirectory 'Prepared Lyrics Glass app image'
}

$helperExecutableName = 'FlowCast Lyrics Glass.exe'
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
    Assert-NoReparseTree $glassDirectory 'Lyrics Glass output'
    Remove-Item -LiteralPath $glassDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
Assert-NoReparseAncestors $DestinationDirectory 'DestinationDirectory'
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
