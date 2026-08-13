[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BridgePath
)

$ErrorActionPreference = 'Stop'
$resolvedBridge = (Resolve-Path -LiteralPath $BridgePath).Path
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$hooksPath = Join-Path $codexHome 'hooks.json'
$marker = 'CodexUsageTray.EventBridge.exe'
$command = "`"$resolvedBridge`" --hook"

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
            ($_ | ConvertTo-Json -Depth 20 -Compress) -notmatch [regex]::Escape($marker)
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

    $handler = [ordered]@{
        type = 'command'
        command = $command
        commandWindows = $command
        timeout = 3
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
