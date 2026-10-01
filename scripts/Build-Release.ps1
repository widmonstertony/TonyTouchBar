#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [switch]$UseExistingBridge
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnet)) { throw '.NET 8 SDK is required to build a release.' }
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts "TonyTouchBar-v$Version-win-x64"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'app'), (Join-Path $stage 'bridge'), (Join-Path $stage 'scripts') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stage 'integrations') -Force | Out-Null

& $dotnet publish (Join-Path $root 'src\T2TouchBar\T2TouchBar.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o (Join-Path $stage 'app')
if ($LASTEXITCODE -ne 0) { throw 'Windows host build failed.' }

$bridgeTarget = Join-Path $stage 'bridge\t2-touchbar-bridge'
if ($UseExistingBridge) {
    $existingBridge = Join-Path $root 'bridge\t2-touchbar-bridge'
    if (-not (Test-Path -LiteralPath $existingBridge)) { throw 'Existing bridge binary was not found.' }
    Copy-Item -LiteralPath $existingBridge -Destination $bridgeTarget
} else {
    $bridgeWindowsPath = Join-Path $root 'bridge\t2-touchbar-bridge.c'
    $bridgeWslPath = (& wsl.exe -d Ubuntu-24.04 --exec wslpath -a $bridgeWindowsPath).Trim()
    $bridgeOutput = (& wsl.exe -d Ubuntu-24.04 --exec wslpath -a $bridgeTarget).Trim()
    & wsl.exe -d Ubuntu-24.04 --exec sh -c "cc -O2 -Wall -Wextra -Werror -pthread -o '$bridgeOutput' '$bridgeWslPath' -lusb-1.0 -pthread"
    if ($LASTEXITCODE -ne 0) { throw 'Linux bridge build failed. Install build-essential and libusb-1.0-0-dev in Ubuntu.' }
}

Copy-Item -LiteralPath (Join-Path $root 'scripts\Install.ps1'), (Join-Path $root 'scripts\Setup-Device.ps1'), (Join-Path $root 'scripts\Uninstall.ps1'), (Join-Path $root 'scripts\Diagnostics.ps1') -Destination (Join-Path $stage 'scripts')
Copy-Item -LiteralPath (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'bridge\LICENSE') -Destination (Join-Path $stage 'bridge\LICENSE')
Copy-Item -LiteralPath (Join-Path $root 'integrations\codex') -Destination (Join-Path $stage 'integrations\codex') -Recurse

$hashes = Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($stage, $_.FullName).Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$hashes | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
$zip = "$stage.zip"
Compress-Archive -LiteralPath $stage -DestinationPath $zip -Force
$setupPayload = Join-Path $artifacts 'setup-payload.zip'
Compress-Archive -Path (Join-Path $stage 'app'), (Join-Path $stage 'bridge'), (Join-Path $stage 'integrations') -DestinationPath $setupPayload -Force
& $dotnet publish (Join-Path $root 'src\TonyTouchBar.Setup\TonyTouchBar.Setup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -p:SetupPayload=$setupPayload -o (Join-Path $artifacts 'setup')
if ($LASTEXITCODE -ne 0) { throw 'Setup build failed.' }
Write-Host "Release package: $zip" -ForegroundColor Green
Write-Host "Installer:       $(Join-Path $artifacts 'setup\TonyTouchBar-Setup.exe')" -ForegroundColor Green
