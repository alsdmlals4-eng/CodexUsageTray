[CmdletBinding()]
param(
    [switch]$StartWithWindows,
    [switch]$DoNotLaunch,
    [switch]$SkipCodexHooks
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-windows.ps1'
$builtExecutable = Join-Path $projectRoot 'artifacts/win-x64/CodexUsageTray.exe'
$builtBridge = Join-Path $projectRoot 'artifacts/win-x64/CodexUsageTray.EventBridge.exe'
$installDirectory = Join-Path $env:LOCALAPPDATA 'CodexUsageTray'
$installedExecutable = Join-Path $installDirectory 'CodexUsageTray.exe'
$installedBridge = Join-Path $installDirectory 'CodexUsageTray.EventBridge.exe'

& $buildScript
if ($LASTEXITCODE -ne 0) {
    throw '빌드가 실패하여 설치를 중단했습니다.'
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -LiteralPath $builtExecutable -Destination $installedExecutable -Force
Copy-Item -LiteralPath $builtBridge -Destination $installedBridge -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'setup-integration.ps1') -Destination $installDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'remove-integration.ps1') -Destination $installDirectory -Force

if (-not $SkipCodexHooks) {
    & (Join-Path $PSScriptRoot 'setup-integration.ps1') -BridgePath $installedBridge
}

if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-ItemProperty `
        -Path $runKey `
        -Name 'CodexUsageTray' `
        -Value "`"$installedExecutable`" --startup" `
        -PropertyType String `
        -Force | Out-Null
}

if (-not $DoNotLaunch) {
    Start-Process -FilePath $installedExecutable
}

Write-Host "설치 완료: $installedExecutable"
if (-not $SkipCodexHooks) {
    Write-Host '중요: Codex를 다시 시작한 뒤 /hooks를 열어 Codex Usage Tray Hook 세 개를 검토하고 신뢰하세요.'
}
