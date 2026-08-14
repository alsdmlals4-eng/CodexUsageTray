[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $repositoryRoot 'scripts/shortcut-registration.ps1'
$installerPath = Join-Path $repositoryRoot 'scripts/install-release.ps1'

if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "Desktop shortcut helper is missing: $helperPath"
}

$installerText = [System.IO.File]::ReadAllText($installerPath)
foreach ($requiredInstallerTerm in @(
        "'shortcut-registration.ps1'",
        'New-CodexUsageTrayShortcut',
        "Join-Path `$env:LOCALAPPDATA 'CodexUsageTray'",
        'Write-Warning')) {
    if (-not $installerText.Contains($requiredInstallerTerm)) {
        throw "Release installer is missing desktop shortcut wiring: $requiredInstallerTerm"
    }
}
Write-Host 'PASS release installer includes canonical, best-effort desktop shortcut wiring'

. $helperPath -LibraryOnly

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "CodexUsageTray-ShortcutTests-$([guid]::NewGuid().ToString('N'))"
$shortcutDirectory = Join-Path $testRoot 'Desktop'
$executablePath = Join-Path $testRoot 'CodexUsageTray.exe'

try {
    New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $executablePath,
        'shortcut-target-fixture',
        [System.Text.Encoding]::ASCII)

    $shortcutPath = New-CodexUsageTrayShortcut `
        -ExecutablePath $executablePath `
        -ShortcutDirectory $shortcutDirectory

    if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) {
        throw "Desktop shortcut was not created: $shortcutPath"
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $expectedExecutable = [System.IO.Path]::GetFullPath($executablePath)
    $expectedWorkingDirectory = Split-Path -Parent $expectedExecutable

    if (-not [string]::Equals(
            [System.IO.Path]::GetFullPath($shortcut.TargetPath),
            $expectedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Shortcut target mismatch. Expected: $expectedExecutable; actual: $($shortcut.TargetPath)"
    }

    if (-not [string]::Equals(
            [System.IO.Path]::GetFullPath($shortcut.WorkingDirectory),
            $expectedWorkingDirectory,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Shortcut working directory mismatch. Expected: $expectedWorkingDirectory; actual: $($shortcut.WorkingDirectory)"
    }

    if (-not $shortcut.IconLocation.StartsWith(
            $expectedExecutable,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Shortcut icon must use the tray executable. Actual: $($shortcut.IconLocation)"
    }

    Write-Host 'PASS desktop shortcut target, working directory, and icon are correct'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
