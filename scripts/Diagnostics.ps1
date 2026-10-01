#Requires -Version 5.1
[CmdletBinding()]
param([string]$Distribution = 'Ubuntu-24.04')

$ErrorActionPreference = 'Continue'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$work = Join-Path ([IO.Path]::GetTempPath()) "TonyTouchBar-$timestamp"
$output = Join-Path ([Environment]::GetFolderPath('Desktop')) "TonyTouchBar-Diagnostics-$timestamp.zip"
New-Item -ItemType Directory -Path $work -Force | Out-Null

@(
    "TonyTouchBar diagnostics",
    "Generated: $(Get-Date -Format o)",
    "Windows: $([Environment]::OSVersion.VersionString)",
    "PowerShell: $($PSVersionTable.PSVersion)",
    "Secure Boot: $(try { Confirm-SecureBootUEFI } catch { 'Unknown' })",
    "Host running: $([bool](Get-Process TonyTouchBar -ErrorAction SilentlyContinue))"
) | Set-Content -LiteralPath (Join-Path $work 'summary.txt') -Encoding utf8

(& wsl.exe --status 2>&1) | Set-Content -LiteralPath (Join-Path $work 'wsl-status.txt') -Encoding utf8
(& wsl.exe --list --verbose 2>&1) | Set-Content -LiteralPath (Join-Path $work 'wsl-distros.txt') -Encoding utf8
$usbipd = (Get-Command usbipd.exe -ErrorAction SilentlyContinue).Source
if (-not $usbipd) { $usbipd = Join-Path $env:ProgramFiles 'usbipd-win\usbipd.exe' }
if (Test-Path -LiteralPath $usbipd) { (& $usbipd list 2>&1) | Set-Content -LiteralPath (Join-Path $work 'usbipd-list.txt') -Encoding utf8 }
(& wsl.exe -d $Distribution --exec sh -c 'lsusb; ldd /opt/t2touchbar/t2-touchbar-bridge 2>&1' 2>&1) | Set-Content -LiteralPath (Join-Path $work 'bridge.txt') -Encoding utf8

$log = Join-Path $env:LOCALAPPDATA 'TonyTouchBar\logs\host.log'
if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 500 | Set-Content -LiteralPath (Join-Path $work 'host.log') -Encoding utf8 }
$settings = Join-Path $env:LOCALAPPDATA 'TonyTouchBar\settings.json'
if (Test-Path -LiteralPath $settings) { Copy-Item -LiteralPath $settings -Destination (Join-Path $work 'settings.json') }

Compress-Archive -Path (Join-Path $work '*') -DestinationPath $output -Force
Remove-Item -LiteralPath $work -Recurse -Force
Write-Host "Diagnostics written to $output" -ForegroundColor Green
