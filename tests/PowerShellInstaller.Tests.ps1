[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $repositoryRoot 'scripts/install-release.ps1'
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
        'install-release.ps1')) {
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
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host "$testCount PowerShell installer assertions passed"
