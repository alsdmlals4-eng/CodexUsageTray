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
$requiredDirectories = @('browser-extension')
$resolvedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$resolvedInstall = [System.IO.Path]::GetFullPath($InstallDirectory)
$installParent = Split-Path -Parent $resolvedInstall
$installName = Split-Path -Leaf $resolvedInstall
$stageDirectory = Join-Path $installParent "$installName.update-$PID"
$backupDirectory = Join-Path $installParent "$installName.backup-$PID"
$installedExecutable = Join-Path $resolvedInstall 'CodexUsageTray.exe'
$installedBridge = Join-Path $resolvedInstall 'CodexUsageTray.EventBridge.exe'
$installedProcessPaths = @($installedExecutable, $installedBridge)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'CodexUsageTray'
$trayWasRunning = $false
$replacementActivated = $false
$committed = $false
$startupValueChanged = $false
$previousStartupValue = $null
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$hooksPath = Join-Path $codexHome 'hooks.json'
$hooksBackupPath = Join-Path $installParent "$installName.hooks-backup-$PID.json"
$hooksSnapshotTaken = $false
$hooksPreviouslyExisted = $false

function Get-InstalledProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ExecutablePaths
    )

    $normalizedPaths = @($ExecutablePaths | ForEach-Object {
        [System.IO.Path]::GetFullPath($_)
    })
    $processNames = @($normalizedPaths | ForEach-Object {
        [System.IO.Path]::GetFileNameWithoutExtension($_)
    })

    return @(Get-Process -Name $processNames -ErrorAction SilentlyContinue | Where-Object {
        try {
            $processPath = [System.IO.Path]::GetFullPath($_.Path)
            @($normalizedPaths | Where-Object {
                [string]::Equals(
                    $_,
                    $processPath,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }).Count -gt 0
        }
        catch {
            $false
        }
    })
}

function Stop-InstalledProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ExecutablePaths,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $processes = @(Get-InstalledProcesses -ExecutablePaths $ExecutablePaths)
        foreach ($process in $processes) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }

        if ($processes.Count -gt 0) {
            Start-Sleep -Milliseconds 100
        }
        $remaining = @(Get-InstalledProcesses -ExecutablePaths $ExecutablePaths)
    } while ($remaining.Count -gt 0 -and [DateTime]::UtcNow -lt $deadline)

    if ($remaining.Count -gt 0) {
        throw '실행 중인 Codex Usage Tray 구성 요소를 종료하지 못했습니다.'
    }
}

function Move-InstallDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string[]]$ExecutablePaths,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            Move-Item -LiteralPath $Source -Destination $Destination
            return
        }
        catch {
            $isRetryable = $_.Exception -is [System.IO.IOException] -or
                $_.Exception -is [System.UnauthorizedAccessException]
            if (-not $isRetryable -or [DateTime]::UtcNow -ge $deadline) {
                throw
            }

            Stop-InstalledProcesses -ExecutablePaths $ExecutablePaths -TimeoutSeconds 1
            Start-Sleep -Milliseconds 100
        }
    }
}

function Remove-InstallDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$ExecutablePaths,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (Test-Path -LiteralPath $Path) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force
            return
        }
        catch {
            $isRetryable = $_.Exception -is [System.IO.IOException] -or
                $_.Exception -is [System.UnauthorizedAccessException]
            if (-not $isRetryable -or [DateTime]::UtcNow -ge $deadline) {
                throw
            }

            Stop-InstalledProcesses -ExecutablePaths $ExecutablePaths -TimeoutSeconds 1
            Start-Sleep -Milliseconds 100
        }
    }
}

foreach ($fileName in $requiredFiles) {
    $sourcePath = Join-Path $resolvedPackage $fileName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "배포 패키지에 필요한 파일이 없습니다: $fileName"
    }
}
foreach ($directoryName in $requiredDirectories) {
    $sourcePath = Join-Path $resolvedPackage $directoryName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
        throw "배포 패키지에 필요한 폴더가 없습니다: $directoryName"
    }
}

New-Item -ItemType Directory -Path $installParent -Force | Out-Null
foreach ($temporaryDirectory in @($stageDirectory, $backupDirectory)) {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

try {
    New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null
    foreach ($fileName in $requiredFiles) {
        Copy-Item `
            -LiteralPath (Join-Path $resolvedPackage $fileName) `
            -Destination (Join-Path $stageDirectory $fileName) `
            -Force
    }
    foreach ($directoryName in $requiredDirectories) {
        Copy-Item `
            -LiteralPath (Join-Path $resolvedPackage $directoryName) `
            -Destination (Join-Path $stageDirectory $directoryName) `
            -Recurse `
            -Force
    }

    if (Test-Path -LiteralPath $resolvedInstall) {
        $runningProcesses = @(Get-InstalledProcesses -ExecutablePaths $installedProcessPaths)
        $trayWasRunning = @($runningProcesses | Where-Object {
            try {
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($_.Path),
                    $installedExecutable,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                $false
            }
        }).Count -gt 0
        Stop-InstalledProcesses -ExecutablePaths $installedProcessPaths
        Move-InstallDirectory `
            -Source $resolvedInstall `
            -Destination $backupDirectory `
            -ExecutablePaths $installedProcessPaths
    }

    Move-Item -LiteralPath $stageDirectory -Destination $resolvedInstall
    $replacementActivated = $true

    if (-not $SkipCodexHooks) {
        $hooksPreviouslyExisted = Test-Path -LiteralPath $hooksPath -PathType Leaf
        if ($hooksPreviouslyExisted) {
            Copy-Item -LiteralPath $hooksPath -Destination $hooksBackupPath -Force
        }
        $hooksSnapshotTaken = $true
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
    Stop-InstalledProcesses -ExecutablePaths $installedProcessPaths
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

    if ($hooksSnapshotTaken) {
        if ($hooksPreviouslyExisted -and (Test-Path -LiteralPath $hooksBackupPath -PathType Leaf)) {
            New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
            Copy-Item -LiteralPath $hooksBackupPath -Destination $hooksPath -Force
        }
        elseif (-not $hooksPreviouslyExisted) {
            Remove-Item -LiteralPath $hooksPath -Force -ErrorAction SilentlyContinue
        }
    }

    if ($replacementActivated -and (Test-Path -LiteralPath $resolvedInstall)) {
        Remove-InstallDirectory `
            -Path $resolvedInstall `
            -ExecutablePaths $installedProcessPaths
    }

    if (Test-Path -LiteralPath $backupDirectory) {
        Move-Item -LiteralPath $backupDirectory -Destination $resolvedInstall
    }

    if ($trayWasRunning -and (Test-Path -LiteralPath $installedExecutable)) {
        Start-Process -FilePath $installedExecutable
    }

    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
    Remove-Item -LiteralPath $hooksBackupPath -Force -ErrorAction SilentlyContinue
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
    Write-Host '웹 ChatGPT 알림은 Chrome/Edge 확장 관리 화면에서 설치 폴더의 browser-extension을 로드하거나 기존 확장을 새로고침한 뒤, 열려 있던 모든 ChatGPT 탭도 새로고침하세요.'
}
