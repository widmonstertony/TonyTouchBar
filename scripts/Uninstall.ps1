#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Distribution = 'Ubuntu-24.04',
    [switch]$PurgeData
)

$ErrorActionPreference = 'Stop'
Get-Process TonyTouchBar -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TonyTouchBar' -ErrorAction SilentlyContinue
& wsl.exe -d $Distribution -u root --exec rm -f /opt/t2touchbar/t2-touchbar-bridge 2>$null

$installRoot = Join-Path $env:LOCALAPPDATA 'TonyTouchBar'
if ($PurgeData) {
    if ((Resolve-Path -LiteralPath $installRoot -ErrorAction SilentlyContinue).Path -eq $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
} else {
    Remove-Item -LiteralPath (Join-Path $installRoot 'TonyTouchBar.exe') -Force -ErrorAction SilentlyContinue
    Write-Host "Settings and logs were preserved at $installRoot"
}
Write-Host 'TonyTouchBar was uninstalled. The persistent usbipd sharing rule was left unchanged.' -ForegroundColor Green
