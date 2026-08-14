[CmdletBinding()]
param(
    [switch]$LibraryOnly
)

$ErrorActionPreference = 'Stop'

function New-CodexUsageTrayShortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,
        [string]$ShortcutDirectory = [Environment]::GetFolderPath('DesktopDirectory')
    )

    $resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
        throw "Codex Usage Tray executable was not found: $resolvedExecutable"
    }

    if ([string]::IsNullOrWhiteSpace($ShortcutDirectory)) {
        throw 'Desktop shortcut directory could not be resolved.'
    }

    $resolvedShortcutDirectory = [System.IO.Path]::GetFullPath($ShortcutDirectory)
    New-Item -ItemType Directory -Path $resolvedShortcutDirectory -Force | Out-Null
    $shortcutPath = Join-Path $resolvedShortcutDirectory 'Codex Usage Tray.lnk'

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $resolvedExecutable
        $shortcut.WorkingDirectory = Split-Path -Parent $resolvedExecutable
        $shortcut.IconLocation = "$resolvedExecutable,0"
        $shortcut.Description = 'Codex Usage Tray 실행'
        $shortcut.Save()
    }
    finally {
        if ($null -ne $shell -and [System.Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    return $shortcutPath
}

if (-not $LibraryOnly) {
    $installedExecutable = Join-Path $PSScriptRoot 'CodexUsageTray.exe'
    [void](New-CodexUsageTrayShortcut -ExecutablePath $installedExecutable)
}
