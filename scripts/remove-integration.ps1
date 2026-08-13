[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$hooksPath = Join-Path $codexHome 'hooks.json'
$marker = 'CodexUsageTray.EventBridge.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$nativeHostName = 'com.alsdmlals4.codexusagetray'
$installDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

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

try {
    if (-not (Test-Path -LiteralPath $hooksPath)) {
        Write-Host '제거할 Codex Usage Tray Hook이 없습니다.'
    }
    else {
        $backupPath = "$hooksPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $hooksPath -Destination $backupPath -Force
        $document = Get-Content -LiteralPath $hooksPath -Raw | ConvertFrom-Json
        if ($document.PSObject.Properties['hooks']) {
            foreach ($eventName in @('UserPromptSubmit', 'PermissionRequest', 'Stop')) {
                $property = $document.hooks.PSObject.Properties[$eventName]
                if (-not $property) {
                    continue
                }

                $preserved = @(Remove-UsageTrayHandlers -Groups @($property.Value))
                if ($preserved.Count -eq 0) {
                    $document.hooks.PSObject.Properties.Remove($eventName)
                }
                else {
                    $property.Value = $preserved
                }
            }

            $temporaryPath = "$hooksPath.tmp-$PID"
            $json = $document | ConvertTo-Json -Depth 30
            $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($temporaryPath, $json, $utf8WithoutBom)
            Move-Item -LiteralPath $temporaryPath -Destination $hooksPath -Force
            Write-Host "Codex Usage Tray Hook을 제거했습니다. 백업: $backupPath"
        }
        else {
            Write-Host "제거할 Codex Usage Tray Hook이 없습니다. 백업: $backupPath"
        }
    }
}
finally {
    Remove-ItemProperty -Path $runKey -Name 'CodexUsageTray' -ErrorAction SilentlyContinue
    foreach ($nativeHostKey in @(
            "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeHostName",
            "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeHostName")) {
        Remove-Item -LiteralPath $nativeHostKey -Recurse -Force -ErrorAction SilentlyContinue
    }

    Remove-Item `
        -LiteralPath (Join-Path $installDirectory 'chatgpt-native-host.json') `
        -Force `
        -ErrorAction SilentlyContinue
}

Write-Host 'Codex Usage Tray 자동 시작 항목과 ChatGPT 웹 연결을 제거했습니다.'
