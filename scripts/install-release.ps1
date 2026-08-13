[CmdletBinding()]
param(
    [string]$PackageDirectory = $PSScriptRoot,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'CodexUsageTray'),
    [switch]$StartWithWindows,
    [switch]$DoNotLaunch,
    [switch]$SkipCodexHooks
)

$ErrorActionPreference = 'Stop'
$requiredFiles = @(
    'CodexUsageTray.exe',
    'CodexUsageTray.EventBridge.exe',
    'setup-integration.ps1',
    'remove-integration.ps1',
    'install-release.ps1'
)
$resolvedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$resolvedInstall = [System.IO.Path]::GetFullPath($InstallDirectory)
$installParent = Split-Path -Parent $resolvedInstall
$installName = Split-Path -Leaf $resolvedInstall
$stageDirectory = Join-Path $installParent "$installName.update-$PID"
$backupDirectory = Join-Path $installParent "$installName.backup-$PID"
$installedExecutable = Join-Path $resolvedInstall 'CodexUsageTray.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'CodexUsageTray'
$runningProcesses = @()
$replacementActivated = $false
$committed = $false
$startupValueChanged = $false
$previousStartupValue = $null

foreach ($fileName in $requiredFiles) {
    $sourcePath = Join-Path $resolvedPackage $fileName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "배포 패키지에 필요한 파일이 없습니다: $fileName"
    }
}

New-Item -ItemType Directory -Path $installParent -Force | Out-Null
foreach ($temporaryDirectory in @($stageDirectory, $backupDirectory)) {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

if (Test-Path -LiteralPath $resolvedInstall) {
    $runningProcesses = @(Get-Process -Name 'CodexUsageTray' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [string]::Equals(
                [System.IO.Path]::GetFullPath($_.Path),
                $installedExecutable,
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
}

try {
    foreach ($process in $runningProcesses) {
        Stop-Process -Id $process.Id -Force
    }

    if ($runningProcesses.Count -gt 0) {
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        do {
            $remaining = @($runningProcesses | Where-Object {
                Get-Process -Id $_.Id -ErrorAction SilentlyContinue
            })
            if ($remaining.Count -gt 0) {
                Start-Sleep -Milliseconds 100
            }
        } while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

        if ($remaining.Count -gt 0) {
            throw '실행 중인 Codex Usage Tray를 종료하지 못했습니다.'
        }
    }

    New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
    foreach ($fileName in $requiredFiles) {
        Copy-Item `
            -LiteralPath (Join-Path $resolvedPackage $fileName) `
            -Destination (Join-Path $stageDirectory $fileName) `
            -Force
    }

    if (Test-Path -LiteralPath $resolvedInstall) {
        Move-Item -LiteralPath $resolvedInstall -Destination $backupDirectory
    }

    Move-Item -LiteralPath $stageDirectory -Destination $resolvedInstall
    $replacementActivated = $true

    if (-not $SkipCodexHooks) {
        & (Join-Path $resolvedInstall 'setup-integration.ps1') `
            -BridgePath (Join-Path $resolvedInstall 'CodexUsageTray.EventBridge.exe')
    }

    if ($StartWithWindows) {
        try {
            $previousStartupValue = Get-ItemPropertyValue `
                -Path $runKey `
                -Name $runValueName `
                -ErrorAction Stop
        }
        catch [System.Management.Automation.ItemNotFoundException] {
            $previousStartupValue = $null
        }
        catch [System.Management.Automation.PSArgumentException] {
            $previousStartupValue = $null
        }

        New-ItemProperty `
            -Path $runKey `
            -Name $runValueName `
            -Value "`"$installedExecutable`" --startup" `
            -PropertyType String `
            -Force | Out-Null
        $startupValueChanged = $true
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }

    $committed = $true
}
catch {
    $failure = $_
    if ($startupValueChanged) {
        if ($null -eq $previousStartupValue) {
            Remove-ItemProperty -Path $runKey -Name $runValueName -ErrorAction SilentlyContinue
        }
        else {
            New-ItemProperty `
                -Path $runKey `
                -Name $runValueName `
                -Value $previousStartupValue `
                -PropertyType String `
                -Force | Out-Null
        }
    }

    if ($replacementActivated -and (Test-Path -LiteralPath $resolvedInstall)) {
        Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Move-Item -LiteralPath $backupDirectory -Destination $resolvedInstall
    }

    if ($runningProcesses.Count -gt 0 -and (Test-Path -LiteralPath $installedExecutable)) {
        Start-Process -FilePath $installedExecutable
    }

    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}

if (-not $committed) {
    throw 'Codex Usage Tray 설치가 완료되지 않았습니다.'
}

if (-not $DoNotLaunch) {
    Start-Process -FilePath $installedExecutable
}

Write-Host "설치 완료: $installedExecutable"
if (-not $SkipCodexHooks) {
    Write-Host 'Codex를 다시 시작한 뒤 /hooks에서 Codex Usage Tray Hook을 검토하고 신뢰하세요.'
}
