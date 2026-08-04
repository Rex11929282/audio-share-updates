[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(DontShow)]
    [string]$PreparedPublishDirectory
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

function Get-ExactChildPath {
    param([string]$Parent, [string]$Child)

    if ([System.IO.Path]::GetFileName($Child) -ne $Child) {
        throw "Refusing unsafe Lyrics package child name: $Child"
    }

    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Parent $Child))
    $expected = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Parent, $Child))
    if (-not [string]::Equals($candidate, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unsafe Lyrics package output path: $candidate"
    }

    return $candidate
}

function Test-FileContainsForbiddenText {
    param([string]$Path, [string[]]$Markers)

    $maximumMarkerLength = ($Markers | Measure-Object -Property Length -Maximum).Maximum
    $buffer = [char[]]::new(65536)
    $carry = ''
    $reader = [System.IO.StreamReader]::new($Path, $true)
    try {
        while (($charactersRead = $reader.ReadBlock($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $carry + [string]::new($buffer, 0, $charactersRead)
            foreach ($marker in $Markers) {
                if ($text.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    return $true
                }
            }

            $carryLength = [System.Math]::Min($maximumMarkerLength - 1, $text.Length)
            if ($carryLength -gt 0) {
                $carry = $text.Substring($text.Length - $carryLength)
            }
        }
    }
    finally {
        $reader.Dispose()
    }

    return $false
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
Assert-NoReparseAncestors $OutputDirectory 'OutputDirectory'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\AudioShare.Lyrics\AudioShare.Lyrics.csproj'
$publishDirectory = Get-ExactChildPath $OutputDirectory 'publish'
$zipPath = Get-ExactChildPath $OutputDirectory 'FlowCast-Lyrics-win-x64.zip'
$checksumPath = Get-ExactChildPath $OutputDirectory 'FlowCast-Lyrics-win-x64.zip.sha256'

if (-not [string]::IsNullOrWhiteSpace($PreparedPublishDirectory)) {
    if (-not [System.IO.Path]::IsPathRooted($PreparedPublishDirectory)) {
        throw 'PreparedPublishDirectory must be an absolute path.'
    }

    $PreparedPublishDirectory = [System.IO.Path]::GetFullPath($PreparedPublishDirectory)
    $outputPrefix = $OutputDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ([string]::Equals($PreparedPublishDirectory, $OutputDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
        $PreparedPublishDirectory.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'PreparedPublishDirectory must be outside OutputDirectory.'
    }
    Assert-NoReparseAncestors $PreparedPublishDirectory 'PreparedPublishDirectory'
    Assert-NoReparseTree $PreparedPublishDirectory 'Prepared publish input'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Assert-NoReparseAncestors $OutputDirectory 'OutputDirectory'
foreach ($path in @($publishDirectory, $zipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Assert-NoReparseTree $path 'Lyrics package output'
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

if ([string]::IsNullOrWhiteSpace($PreparedPublishDirectory)) {
    dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -p:BuildLyricsGlassRenderer=true `
        --output $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw 'FlowCast Lyrics publish failed.'
    }
}
else {
    New-Item -ItemType Directory -Path $publishDirectory | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $PreparedPublishDirectory -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $publishDirectory -Recurse -Force
    }
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

$textAssetExtensions = @('.css', '.htm', '.html', '.js', '.json', '.map', '.md', '.mjs', '.cjs', '.txt', '.xml', '.yaml', '.yml')
$forbiddenTextMarkers = @('WebView2', 'liquid-glass-react', 'rdev', 'React', 'ReactDOM', 'react-dom')
foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Force) {
    if ($file.Extension -notin $textAssetExtensions) {
        continue
    }

    if (Test-FileContainsForbiddenText $file.FullName $forbiddenTextMarkers) {
        throw "Forbidden content in standalone Lyrics package: $($file.FullName)"
    }
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  FlowCast-Lyrics-win-x64.zip" -NoNewline -Encoding ascii

Write-Host "Created $publishDirectory"
Write-Host "Created $zipPath"
Write-Host "Created $checksumPath"
