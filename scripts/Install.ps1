#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Distribution = 'Ubuntu-24.04',
    [switch]$SkipDeviceSetup
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path $PSScriptRoot -Parent
$appSource = Join-Path $releaseRoot 'app\TonyTouchBar.exe'
$settingsSource = Join-Path $releaseRoot 'app\settings.json'
$bridgeSource = Join-Path $releaseRoot 'bridge\t2-touchbar-bridge'
$installRoot = Join-Path $env:LOCALAPPDATA 'TonyTouchBar'
$appTarget = Join-Path $installRoot 'TonyTouchBar.exe'
$settingsTarget = Join-Path $installRoot 'settings.json'

foreach ($required in @($appSource, $settingsSource, $bridgeSource)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Release payload is incomplete: $required" }
}
if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) { throw 'WSL 2 is required. Run: wsl --install -d Ubuntu-24.04' }
$usbipd = (Get-Command usbipd.exe -ErrorAction SilentlyContinue).Source
if (-not $usbipd) { $usbipd = Join-Path $env:ProgramFiles 'usbipd-win\usbipd.exe' }
if (-not (Test-Path -LiteralPath $usbipd)) { throw 'usbipd-win 5.x is required. Install it with: winget install --interactive --exact dorssel.usbipd-win' }

$distros = (& wsl.exe --list --quiet) -replace "`0", '' | ForEach-Object Trim | Where-Object { $_ }
if ($Distribution -notin $distros) { throw "WSL distribution '$Distribution' was not found. Installed: $($distros -join ', ')" }

if (-not $SkipDeviceSetup) {
    $setup = Join-Path $PSScriptRoot 'Setup-Device.ps1'
    $quotedSetup = '"' + $setup.Replace('"', '""') + '"'
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File $quotedSetup"
    $shell = (Get-Process -Id $PID).Path
    $elevated = Start-Process $shell -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($elevated.ExitCode -ne 0) { throw 'Touch Bar device setup was cancelled or failed.' }
}

Get-Process TonyTouchBar -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -LiteralPath $appSource -Destination $appTarget -Force
if (-not (Test-Path -LiteralPath $settingsTarget)) { Copy-Item -LiteralPath $settingsSource -Destination $settingsTarget }

$wslBridgeSource = (& wsl.exe -d $Distribution --exec wslpath -a $bridgeSource).Trim()
if (-not $wslBridgeSource) { throw 'Could not translate the bridge path for WSL.' }
& wsl.exe -d $Distribution -u root --exec install -D -m 0755 $wslBridgeSource /opt/t2touchbar/t2-touchbar-bridge
if ($LASTEXITCODE -ne 0) { throw 'Could not install the Linux bridge into WSL.' }

$config = Get-Content -LiteralPath $settingsTarget -Raw | ConvertFrom-Json
$config.wslDistribution = $Distribution
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsTarget -Encoding utf8

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name 'TonyTouchBar' -Value ('"{0}"' -f $appTarget) -PropertyType String -Force | Out-Null
Start-Process -FilePath $appTarget

Write-Host ''
Write-Host 'TonyTouchBar is installed.' -ForegroundColor Green
Write-Host "App:      $appTarget"
Write-Host "Settings: $settingsTarget"
Write-Host 'Secure Boot does not need to be disabled.'
