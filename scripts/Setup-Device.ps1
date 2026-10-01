#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$usbipd = (Get-Command usbipd.exe -ErrorAction SilentlyContinue).Source
if (-not $usbipd) { $usbipd = Join-Path $env:ProgramFiles 'usbipd-win\usbipd.exe' }
if (-not (Test-Path -LiteralPath $usbipd)) { throw 'usbipd-win 5.x is required. Install it with: winget install --interactive --exact dorssel.usbipd-win' }

Write-Host 'Sharing Apple Touch Bar USB device 05ac:8302 with WSL...'
& $usbipd bind --force --hardware-id 05ac:8302
if ($LASTEXITCODE -ne 0) { throw 'usbipd bind failed. See the message above.' }
Write-Host 'Device sharing is configured. This setting survives reboots.' -ForegroundColor Green
