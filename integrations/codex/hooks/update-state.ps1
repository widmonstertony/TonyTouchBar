$ErrorActionPreference = 'SilentlyContinue'
$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

$eventData = $raw | ConvertFrom-Json
$eventName = [string]$eventData.hook_event_name
$toolName = [string]$eventData.tool_name
$phase = 'idle'
$label = 'Codex ready'
$detail = ''
$active = $false

switch ($eventName) {
    'UserPromptSubmit' { $phase = 'thinking'; $label = 'Codex is thinking'; $active = $true }
    'PreToolUse' {
        $phase = 'working'; $label = 'Codex is working'; $active = $true
        $detail = switch -Regex ($toolName) {
            'Bash|exec|command' { 'Running a command'; break }
            'apply_patch|write|edit' { 'Editing files'; break }
            'web|search|fetch' { 'Researching'; break }
            'MCP|mcp' { 'Using a connected app'; break }
            default { if ($toolName) { $toolName } else { 'Using a tool' } }
        }
    }
    'PermissionRequest' { $phase = 'approval'; $label = 'Approval needed'; $detail = 'Open Codex to review'; $active = $true }
    'PostToolUse' { $phase = 'thinking'; $label = 'Codex is thinking'; $active = $true }
    'Stop' { $phase = 'complete'; $label = 'Codex finished'; $active = $false }
    'Interrupt' { $phase = 'interrupted'; $label = 'Codex interrupted'; $active = $false }
    'SessionEnd' { $phase = 'idle'; $label = 'Codex ready'; $active = $false }
    'SessionStart' { $phase = 'idle'; $label = 'Codex ready'; $active = $false }
}

$root = Join-Path $env:LOCALAPPDATA 'TonyTouchBar\integrations'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$target = Join-Path $root 'codex-state.json'
$temporary = "$target.tmp"
[ordered]@{
    active = $active
    phase = $phase
    label = $label
    detail = $detail
    event = $eventName
    sessionId = [string]$eventData.session_id
    turnId = [string]$eventData.turn_id
    updatedAt = [DateTimeOffset]::Now.ToString('O')
} | ConvertTo-Json -Compress | Set-Content -LiteralPath $temporary -Encoding utf8
Move-Item -LiteralPath $temporary -Destination $target -Force
exit 0
