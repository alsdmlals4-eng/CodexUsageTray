[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $repositoryRoot 'scripts/install-release.ps1'
$integrationSetupPath = Join-Path $repositoryRoot 'scripts/setup-integration.ps1'
$onlineInstallerPath = Join-Path $repositoryRoot 'install-online.ps1'
$releaseWorkflowPath = Join-Path $repositoryRoot '.github/workflows/release.yml'
$testCount = 0

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw "ASSERT TRUE FAILED: $Message"
    }

    $script:testCount++
    Write-Host "PASS $Message"
}

function Assert-Equal {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "ASSERT EQUAL FAILED: $Message; expected=<$Expected>; actual=<$Actual>"
    }

    $script:testCount++
    Write-Host "PASS $Message"
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Message
    )

    try {
        & $Action
    }
    catch {
        $script:testCount++
        Write-Host "PASS $Message"
        return
    }

    throw "ASSERT THROWS FAILED: $Message"
}

function Get-PowerShellSourceFiles {
    $files = @()
    foreach ($directory in @('scripts', 'tests')) {
        $path = Join-Path $repositoryRoot $directory
        if (Test-Path -LiteralPath $path) {
            $files += Get-ChildItem -LiteralPath $path -Filter '*.ps1' -File
        }
    }

    $onlineInstaller = Join-Path $repositoryRoot 'install-online.ps1'
    if (Test-Path -LiteralPath $onlineInstaller) {
        $files += Get-Item -LiteralPath $onlineInstaller
    }

    return @($files | Sort-Object FullName -Unique)
}

foreach ($file in Get-PowerShellSourceFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $hasUtf8Bom = $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    $isOnlineInstaller = [string]::Equals(
        $file.FullName,
        $onlineInstallerPath,
        [System.StringComparison]::OrdinalIgnoreCase)
    if ($isOnlineInstaller) {
        Assert-True (-not $hasUtf8Bom) 'online installer is BOM-free for irm pipe execution'
        $nonAsciiByteCount = @($bytes | Where-Object { $_ -gt 0x7F }).Count
        Assert-Equal 0 $nonAsciiByteCount 'online installer is ASCII for Windows PowerShell 5.1'
    }
    else {
        Assert-True $hasUtf8Bom "$($file.Name) uses UTF-8 BOM"
    }

    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$parseErrors)
    Assert-Equal 0 @($parseErrors).Count "$($file.Name) parses without PowerShell syntax errors"
}

Assert-True (Test-Path -LiteralPath $installerPath -PathType Leaf) 'release installer exists'
Assert-True (Test-Path -LiteralPath $onlineInstallerPath -PathType Leaf) 'online installer exists'
Assert-True (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf) 'release workflow exists'
$releaseWorkflow = [System.IO.File]::ReadAllText($releaseWorkflowPath)
foreach ($requiredWorkflowText in @(
        'runs-on: windows-latest',
        'powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass',
        'dotnet run --configuration Release',
        'CodexUsageTray-win-x64.zip',
        'Get-FileHash',
        'gh release create')) {
    Assert-True (
        $releaseWorkflow.Contains($requiredWorkflowText)
    ) "release workflow contains: $requiredWorkflowText"
}
. $onlineInstallerPath -LibraryOnly

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "CodexUsageTray-InstallerTests-$([guid]::NewGuid().ToString('N'))"
$packageDirectory = Join-Path $testRoot 'package'
$installDirectory = Join-Path $testRoot 'installed'
$lockingBridge = $null
$protectedBridge = $null
$rollbackBridge = $null
$unexpectedTrayProcesses = @()

try {
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

    $archiveFixture = Join-Path $testRoot 'archive.zip'
    $checksumFixture = Join-Path $testRoot 'archive.zip.sha256'
    $malformedChecksumFixture = Join-Path $testRoot 'malformed.sha256'
    [System.IO.File]::WriteAllText(
        $archiveFixture,
        'archive-fixture',
        [System.Text.Encoding]::ASCII)
    $actualSha256 = (Get-FileHash -LiteralPath $archiveFixture -Algorithm SHA256).Hash
    [System.IO.File]::WriteAllText(
        $checksumFixture,
        "$($actualSha256.ToLowerInvariant())  CodexUsageTray-win-x64.zip",
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        $malformedChecksumFixture,
        'not-a-checksum  CodexUsageTray-win-x64.zip',
        [System.Text.Encoding]::ASCII)

    Assert-Equal $actualSha256 (
        Read-ExpectedSha256 -Path $checksumFixture
    ) 'valid checksum file is parsed'
    Assert-Throws {
        Read-ExpectedSha256 -Path $malformedChecksumFixture
    } 'malformed checksum is rejected'
    $incorrectSha256 = '0' * 64
    Assert-Throws {
        Assert-ArchiveSha256 -ArchivePath $archiveFixture -ExpectedSha256 $incorrectSha256
    } 'mismatched archive checksum is rejected'
    Assert-True (
        Assert-ArchiveSha256 -ArchivePath $archiveFixture -ExpectedSha256 $actualSha256
    ) 'matching archive checksum is accepted'

    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.exe'),
        'version-1',
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.EventBridge.exe'),
        'bridge-1',
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'setup-integration.ps1'),
        '# setup fixture',
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'remove-integration.ps1'),
        '# remove fixture',
        [System.Text.Encoding]::ASCII)
    $extensionFixture = Join-Path $packageDirectory 'browser-extension'
    New-Item -ItemType Directory -Path $extensionFixture -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $extensionFixture 'manifest.json'),
        '{"manifest_version":3}',
        [System.Text.Encoding]::ASCII)
    Copy-Item -LiteralPath $installerPath -Destination (Join-Path $packageDirectory 'install-release.ps1')

    & $installerPath `
        -PackageDirectory $packageDirectory `
        -InstallDirectory $installDirectory `
        -DoNotLaunch `
        -SkipCodexHooks

    Assert-Equal 'version-1' (
        [System.IO.File]::ReadAllText((Join-Path $installDirectory 'CodexUsageTray.exe'))
    ) 'first install copies the tray executable'
    Assert-Equal 'bridge-1' (
        [System.IO.File]::ReadAllText((Join-Path $installDirectory 'CodexUsageTray.EventBridge.exe'))
    ) 'first install copies the event bridge'
    foreach ($requiredName in @(
        'setup-integration.ps1',
        'remove-integration.ps1',
        'install-release.ps1',
        'browser-extension\manifest.json')) {
        Assert-True (
            Test-Path -LiteralPath (Join-Path $installDirectory $requiredName) -PathType Leaf
        ) "first install copies $requiredName"
    }

    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.exe'),
        'version-2',
        [System.Text.Encoding]::ASCII)
    & $installerPath `
        -PackageDirectory $packageDirectory `
        -InstallDirectory $installDirectory `
        -DoNotLaunch `
        -SkipCodexHooks

    Assert-Equal 'version-2' (
        [System.IO.File]::ReadAllText((Join-Path $installDirectory 'CodexUsageTray.exe'))
    ) 'update replaces the tray executable'
    Assert-Equal 0 @(
        Get-ChildItem -LiteralPath $testRoot -Directory |
            Where-Object { $_.Name -like 'installed.update-*' -or $_.Name -like 'installed.backup-*' }
    ).Count 'update leaves no staging or backup directory'

    $lockHolderSource = @'
using System.Threading;

public static class ProcessLockHolder
{
    public static void Main()
    {
        Thread.Sleep(30000);
    }
}
'@
    $lockHolderTemplate = Join-Path $testRoot 'ProcessLockHolder.exe'
    Add-Type `
        -TypeDefinition $lockHolderSource `
        -Language CSharp `
        -OutputAssembly $lockHolderTemplate `
        -OutputType ConsoleApplication
    $protectedDirectory = Join-Path $testRoot 'protected-other-install'
    New-Item -ItemType Directory -Path $protectedDirectory -Force | Out-Null
    $protectedBridgePath = Join-Path $protectedDirectory 'CodexUsageTray.EventBridge.exe'
    Copy-Item -LiteralPath $lockHolderTemplate -Destination $protectedBridgePath
    $protectedBridge = Start-Process -FilePath $protectedBridgePath -PassThru
    $installedBridge = Join-Path $installDirectory 'CodexUsageTray.EventBridge.exe'
    Remove-Item -LiteralPath $installedBridge -Force
    Copy-Item -LiteralPath $lockHolderTemplate -Destination $installedBridge
    $lockingBridge = Start-Process -FilePath $installedBridge -PassThru
    Start-Sleep -Milliseconds 300
    Assert-True (-not $lockingBridge.HasExited) 'EventBridge lock fixture is running before update'

    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.exe'),
        'version-3',
        [System.Text.Encoding]::ASCII)
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.EventBridge.exe'),
        'bridge-3',
        [System.Text.Encoding]::ASCII)
    & $installerPath `
        -PackageDirectory $packageDirectory `
        -InstallDirectory $installDirectory `
        -DoNotLaunch `
        -SkipCodexHooks

    $lockingBridge.Refresh()
    $protectedBridge.Refresh()
    Assert-True $lockingBridge.HasExited 'update stops the installed EventBridge process'
    Assert-True (-not $protectedBridge.HasExited) 'update preserves same-name EventBridge outside the install path'
    Assert-Equal 'version-3' (
        [System.IO.File]::ReadAllText((Join-Path $installDirectory 'CodexUsageTray.exe'))
    ) 'update succeeds while the installed EventBridge is running'

    $installedTray = Join-Path $installDirectory 'CodexUsageTray.exe'
    Remove-Item -LiteralPath $installedTray -Force
    Remove-Item -LiteralPath $installedBridge -Force
    Copy-Item -LiteralPath $lockHolderTemplate -Destination $installedTray
    Copy-Item -LiteralPath $lockHolderTemplate -Destination $installedBridge
    $restoredTrayHash = (Get-FileHash -LiteralPath $installedTray -Algorithm SHA256).Hash
    $rollbackBridge = Start-Process -FilePath $installedBridge -PassThru
    Start-Sleep -Milliseconds 300
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'CodexUsageTray.exe'),
        'failed-version',
        [System.Text.Encoding]::ASCII)
    Copy-Item `
        -LiteralPath $lockHolderTemplate `
        -Destination (Join-Path $packageDirectory 'CodexUsageTray.EventBridge.exe') `
        -Force
    [System.IO.File]::WriteAllText(
        (Join-Path $packageDirectory 'setup-integration.ps1'),
        @'
$startedBridge = Start-Process `
    -FilePath (Join-Path $PSScriptRoot 'CodexUsageTray.EventBridge.exe') `
    -PassThru
Start-Sleep -Milliseconds 300
throw "forced integration failure after bridge start: $($startedBridge.Id)"
'@,
        [System.Text.Encoding]::ASCII)

    Assert-Throws {
        & $installerPath `
            -PackageDirectory $packageDirectory `
            -InstallDirectory $installDirectory `
            -DoNotLaunch
    } 'failed integration rolls the update back'

    Assert-Equal $restoredTrayHash (
        (Get-FileHash -LiteralPath $installedTray -Algorithm SHA256).Hash
    ) 'failed update restores the previous tray executable'
    Start-Sleep -Milliseconds 300
    $unexpectedTrayProcesses = @(Get-Process -Name 'CodexUsageTray' -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and [string]::Equals(
                [System.IO.Path]::GetFullPath($_.Path),
                $installedTray,
                [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    })
    Assert-Equal 0 $unexpectedTrayProcesses.Count 'EventBridge-only rollback does not start the tray'
}
finally {
    foreach ($testProcess in @($lockingBridge, $protectedBridge, $rollbackBridge) + @($unexpectedTrayProcesses)) {
        if ($null -ne $testProcess -and -not $testProcess.HasExited) {
            Stop-Process -Id $testProcess.Id -Force -ErrorAction SilentlyContinue
            [void]$testProcess.WaitForExit(5000)
        }
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

if ($env:OS -eq 'Windows_NT') {
    $integrationRoot = Join-Path ([System.IO.Path]::GetTempPath()) "CodexUsageTray-HookTests-$([guid]::NewGuid().ToString('N'))"
    $integrationInstallDirectory = Join-Path $integrationRoot 'installed'
    $integrationCodexHome = Join-Path $integrationRoot 'codex-home'
    $integrationBridgePath = Join-Path $integrationInstallDirectory 'CodexUsageTray.EventBridge.exe'
    $previousCodexHome = $env:CODEX_HOME
    $nativeHostName = 'com.alsdmlals4.codexusagetray'

    try {
        New-Item -ItemType Directory -Path $integrationInstallDirectory -Force | Out-Null
        New-Item -ItemType Directory -Path $integrationCodexHome -Force | Out-Null
        [System.IO.File]::WriteAllText(
            $integrationBridgePath,
            'bridge-fixture',
            [System.Text.Encoding]::ASCII)

        $existingHooks = [ordered]@{
            hooks = [ordered]@{
                Stop = @(
                    [ordered]@{
                        hooks = @(
                            [ordered]@{
                                type = 'command'
                                commandWindows = 'user-stop-handler.exe'
                                timeout = 9
                            }
                        )
                    },
                    [ordered]@{
                        hooks = @(
                            [ordered]@{
                                type = 'command'
                                commandWindows = '"C:\old\CodexUsageTray.EventBridge.exe" --hook'
                                timeout = 3
                            }
                        )
                    }
                )
            }
        } | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText(
            (Join-Path $integrationCodexHome 'hooks.json'),
            $existingHooks,
            (New-Object System.Text.UTF8Encoding($false)))

        $env:CODEX_HOME = $integrationCodexHome
        & $integrationSetupPath -BridgePath $integrationBridgePath

        $installedHooks = Get-Content -LiteralPath (Join-Path $integrationCodexHome 'hooks.json') -Raw | ConvertFrom-Json
        foreach ($eventName in @('UserPromptSubmit', 'PermissionRequest', 'Stop')) {
            $usageTrayHandlers = @($installedHooks.hooks.$eventName | ForEach-Object { $_.hooks } | Where-Object {
                $_.commandWindows -match 'invoke-codex-hook\.ps1'
            })
            Assert-Equal 1 $usageTrayHandlers.Count "$eventName installs exactly one resilient hook wrapper"
            Assert-Equal 15 $usageTrayHandlers[0].timeout "$eventName allows bridge cold start time"
            Assert-True $usageTrayHandlers[0].commandWindows.EndsWith(" $eventName") `
                "$eventName passes its trusted event name outside the Hook payload"
            Assert-True $usageTrayHandlers[0].commandWindows.StartsWith('powershell.exe ') `
                "$eventName uses a command line accepted by both PowerShell and cmd"
        }

        $preservedUserHandlers = @($installedHooks.hooks.Stop | ForEach-Object { $_.hooks } | Where-Object {
            $_.commandWindows -eq 'user-stop-handler.exe'
        })
        Assert-Equal 1 $preservedUserHandlers.Count 'setup preserves unrelated user Stop hooks'

        $wrapperPath = Join-Path $integrationInstallDirectory 'invoke-codex-hook.ps1'
        Assert-True (Test-Path -LiteralPath $wrapperPath -PathType Leaf) 'setup creates the Codex hook wrapper'
        $wrapperBytes = [System.IO.File]::ReadAllBytes($wrapperPath)
        Assert-Equal 0 @($wrapperBytes | Where-Object { $_ -gt 0x7F }).Count 'Codex hook wrapper is ASCII'
        $wrapperText = [System.IO.File]::ReadAllText($wrapperPath)
        Assert-True $wrapperText.Contains("Join-Path `$PSScriptRoot 'CodexUsageTray.EventBridge.exe'") 'hook wrapper launches its installed event bridge'
        Assert-True $wrapperText.Contains('--hook $EventName') 'hook wrapper forwards the trusted configured event name'
        Assert-True $wrapperText.Contains('UTF8Encoding($false)') 'hook wrapper preserves Unicode Hook JSON input'
        Assert-True $wrapperText.Contains('exit 0') 'hook wrapper never reports notification delivery as a Codex failure'
    }
    finally {
        $env:CODEX_HOME = $previousCodexHome
        foreach ($nativeHostKey in @(
                "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeHostName",
                "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeHostName")) {
            Remove-Item -LiteralPath $nativeHostKey -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $integrationRoot) {
            Remove-Item -LiteralPath $integrationRoot -Recurse -Force
        }
    }
}

Write-Host "$testCount PowerShell installer assertions passed"
