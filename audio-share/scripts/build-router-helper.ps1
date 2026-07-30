[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$PythonEmbedZip,

    [string]$HostPython
)

$ErrorActionPreference = 'Stop'

$routerVersion = '1.1.2'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $projectRoot 'router-helper'
$helperSourcePath = Join-Path $sourceDirectory 'audio_share_router_helper.py'
$lockPath = Join-Path $sourceDirectory 'requirements.lock'
$noticesPath = Join-Path $projectRoot 'ThirdPartyNotices.txt'

foreach ($path in @($PythonEmbedZip, $helperSourcePath, $lockPath, $noticesPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required input is missing: $path"
    }
}

if ([string]::IsNullOrWhiteSpace($HostPython)) {
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $pythonCommand) {
        throw 'Host Python 3.12 is required. Supply -HostPython when python is not on PATH.'
    }

    $HostPython = $pythonCommand.Source
}

if (-not (Test-Path -LiteralPath $HostPython -PathType Leaf)) {
    throw "Host Python executable is missing: $HostPython"
}

& $HostPython -c 'import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 12) else 1)'
if ($LASTEXITCODE -ne 0) {
    throw 'Host Python must be 3.12.'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$packageDirectory = Join-Path $OutputDirectory 'router-helper'
if (Test-Path -LiteralPath $packageDirectory) {
    throw "Router helper output already exists: $packageDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Expand-Archive -LiteralPath $PythonEmbedZip -DestinationPath $packageDirectory

$embeddedPython = Join-Path $packageDirectory 'python.exe'
$embeddedPth = Join-Path $packageDirectory 'python312._pth'
$embeddedStdlib = Join-Path $packageDirectory 'python312.zip'
foreach ($path in @($embeddedPython, $embeddedPth, $embeddedStdlib)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "PythonEmbedZip is not a Python 3.12 embedded Windows runtime: missing $path"
    }
}

& $embeddedPython -c 'import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 12) else 1)'
if ($LASTEXITCODE -ne 0) {
    throw 'PythonEmbedZip must contain Python 3.12.'
}

$pthLines = Get-Content -LiteralPath $embeddedPth
$updatedPthLines = [System.Collections.Generic.List[string]]::new()
$importsSite = $false
foreach ($line in $pthLines) {
    if ($line.Trim() -in @('#import site', 'import site')) {
        $updatedPthLines.Add('import site')
        $importsSite = $true
    }
    else {
        $updatedPthLines.Add($line)
    }
}

if ($updatedPthLines -notcontains 'site-packages') {
    $updatedPthLines.Add('site-packages')
}

if (-not $importsSite) {
    $updatedPthLines.Add('import site')
}

Set-Content -LiteralPath $embeddedPth -Value $updatedPthLines -Encoding ascii

$sitePackages = Join-Path $packageDirectory 'site-packages'
& $HostPython -m pip install --disable-pip-version-check --require-hashes --no-deps --target $sitePackages -r $lockPath
if ($LASTEXITCODE -ne 0) {
    throw 'Installing locked router helper packages failed.'
}

foreach ($package in @('comtypes', 'psutil', 'pycaw', 'winappaudiorouter')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sitePackages $package))) {
        throw "Locked router helper package is missing after installation: $package"
    }
}

Copy-Item -LiteralPath $helperSourcePath -Destination $packageDirectory
Copy-Item -LiteralPath $lockPath -Destination $packageDirectory
Copy-Item -LiteralPath $noticesPath -Destination (Join-Path $packageDirectory 'ThirdPartyNotices.txt')
Copy-Item -LiteralPath (Join-Path $packageDirectory 'LICENSE.txt') -Destination (Join-Path $packageDirectory 'PythonLicense.txt')

$helperHash = (Get-FileHash -LiteralPath (Join-Path $packageDirectory 'audio_share_router_helper.py') -Algorithm SHA256).Hash.ToLowerInvariant()
@{
    helperSha256 = $helperHash
    routerVersion = $routerVersion
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageDirectory 'router-helper-manifest.json') -Encoding utf8

Write-Host "Built $packageDirectory"
