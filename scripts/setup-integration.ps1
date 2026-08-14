[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BridgePath
)

$ErrorActionPreference = 'Stop'
$resolvedBridge = (Resolve-Path -LiteralPath $BridgePath).Path
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$hooksPath = Join-Path $codexHome 'hooks.json'
$markers = @('CodexUsageTray.EventBridge.exe', 'invoke-codex-hook.cmd')
$nativeHostName = 'com.alsdmlals4.codexusagetray'
$extensionOrigin = 'chrome-extension://mgeacoaocoijccehjlolcedfbhbaifhl/'
$installDirectory = Split-Path -Parent $resolvedBridge
$nativeManifestPath = Join-Path $installDirectory 'chatgpt-native-host.json'
$hookWrapperPath = Join-Path $installDirectory 'invoke-codex-hook.cmd'

$wrapperTemporaryPath = "$hookWrapperPath.tmp-$PID"
$wrapperText = @(
    '@echo off',
    '"%~dp0CodexUsageTray.EventBridge.exe" --hook "%~1" 2>nul',
    'exit /b 0',
    ''
) -join "`r`n"
[System.IO.File]::WriteAllText(
    $wrapperTemporaryPath,
    $wrapperText,
    [System.Text.Encoding]::ASCII)
Move-Item -LiteralPath $wrapperTemporaryPath -Destination $hookWrapperPath -Force

New-Item -ItemType Directory -Path $codexHome -Force | Out-Null

if (Test-Path -LiteralPath $hooksPath) {
    $backupPath = "$hooksPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -LiteralPath $hooksPath -Destination $backupPath -Force
    try {
        $document = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "기존 hooks.json을 해석할 수 없습니다. 백업: $backupPath"
    }
}
else {
    $document = [pscustomobject]@{
        description = 'User-level Codex hooks including Codex Usage Tray notifications.'
        hooks = [pscustomobject]@{}
    }
}

if (-not $document.PSObject.Properties['hooks']) {
    $document | Add-Member -MemberType NoteProperty -Name hooks -Value ([pscustomobject]@{})
}

function Remove-UsageTrayHandlers {
    param([object[]]$Groups)

    $result = @()
    foreach ($group in @($Groups)) {
        $hooksProperty = $group.PSObject.Properties['hooks']
        if (-not $hooksProperty) {
            $result += $group
            continue
        }

        $originalHandlers = @($hooksProperty.Value)
        $preservedHandlers = @($originalHandlers | Where-Object {
            $serializedHandler = $_ | ConvertTo-Json -Depth 20 -Compress
            $isUsageTrayHandler = $false
            foreach ($usageTrayMarker in $markers) {
                if ($serializedHandler -match [regex]::Escape($usageTrayMarker)) {
                    $isUsageTrayHandler = $true
                    break
                }
            }
            -not $isUsageTrayHandler
        })
        if ($preservedHandlers.Count -eq $originalHandlers.Count) {
            $result += $group
            continue
        }

        if ($preservedHandlers.Count -gt 0) {
            $hooksProperty.Value = $preservedHandlers
            $result += $group
        }
    }

    return $result
}

function Set-UsageTrayHook {
    param(
        [Parameter(Mandatory = $true)][string]$EventName,
        [string]$Matcher
    )

    $property = $document.hooks.PSObject.Properties[$EventName]
    $existing = if ($property) { @($property.Value) } else { @() }
    $preserved = @(Remove-UsageTrayHandlers -Groups $existing)
    $command = "`"$hookWrapperPath`" $EventName"

    $handler = [ordered]@{
        type = 'command'
        command = $command
        commandWindows = $command
        timeout = 15
        statusMessage = 'Sending Codex activity notification'
    }
    $group = [ordered]@{ hooks = @($handler) }
    if ($Matcher) {
        $group.matcher = $Matcher
    }

    $updated = @($preserved) + @([pscustomobject]$group)
    if ($property) {
        $property.Value = $updated
    }
    else {
        $document.hooks | Add-Member -MemberType NoteProperty -Name $EventName -Value $updated
    }
}

Set-UsageTrayHook -EventName 'UserPromptSubmit'
Set-UsageTrayHook -EventName 'PermissionRequest' -Matcher '*'
Set-UsageTrayHook -EventName 'Stop'

$temporaryPath = "$hooksPath.tmp-$PID"
$json = $document | ConvertTo-Json -Depth 30
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($temporaryPath, $json, $utf8WithoutBom)
Move-Item -LiteralPath $temporaryPath -Destination $hooksPath -Force

Write-Host "Codex 작업 알림 Hook을 병합했습니다: $hooksPath"

$nativeManifest = [ordered]@{
    name = $nativeHostName
    description = 'Codex Usage Tray ChatGPT activity bridge'
    path = $resolvedBridge
    type = 'stdio'
    allowed_origins = @($extensionOrigin)
} | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($nativeManifestPath, $nativeManifest, $utf8WithoutBom)

foreach ($nativeHostKey in @(
        "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeHostName",
        "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeHostName")) {
    New-Item -Path $nativeHostKey -Force | Out-Null
    Set-Item -Path $nativeHostKey -Value $nativeManifestPath
}

Write-Host "ChatGPT 웹 네이티브 연결을 등록했습니다: $nativeManifestPath"
$extensionDirectory = Join-Path $installDirectory 'browser-extension'
if (Test-Path -LiteralPath $extensionDirectory -PathType Container) {
    Write-Host "Chrome/Edge 확장 관리 화면에서 이 폴더를 한 번 로드하세요: $extensionDirectory"
    Write-Host '확장을 처음 로드하거나 새로고침한 뒤, 열려 있던 모든 ChatGPT 탭도 새로고침하세요.'
}
