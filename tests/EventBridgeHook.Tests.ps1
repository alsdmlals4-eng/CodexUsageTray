[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BridgePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$setupPath = Join-Path $repositoryRoot 'scripts/setup-integration.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "CodexUsageTray-BridgeHook-$([guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testRoot 'installed folder'
$codexHome = Join-Path $testRoot 'codex-home'
$installedBridge = Join-Path $installDirectory 'CodexUsageTray.EventBridge.exe'
$previousCodexHome = $env:CODEX_HOME
$nativeHostName = 'com.alsdmlals4.codexusagetray'

function Assert-Equal {
    param(
        [AllowNull()][object]$Expected,
        [AllowNull()][object]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "ASSERT EQUAL FAILED: $Message; expected=<$Expected>; actual=<$Actual>"
    }

    Write-Host "PASS $Message"
}

function Invoke-InstalledHookCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Payload,
        [Parameter(Mandatory = $true)][string]$CommandLine
    )

    $inputPath = [System.IO.Path]::GetTempFileName()
    $outputPath = [System.IO.Path]::GetTempFileName()
    $errorPath = [System.IO.Path]::GetTempFileName()
    try {
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($inputPath, $Payload, $utf8WithoutBom)
        $command = "$CommandLine < `"$inputPath`" > `"$outputPath`" 2> `"$errorPath`""
        cmd.exe /D /S /C $command | Out-Null
        $script:LastWrapperExitCode = $LASTEXITCODE
        $script:LastWrapperError = [System.IO.File]::ReadAllText($errorPath)
        return [System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($outputPath))
    }
    finally {
        Remove-Item -LiteralPath $inputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-InstalledHookCommandFromPowerShell {
    param(
        [Parameter(Mandatory = $true)][string]$Payload,
        [Parameter(Mandatory = $true)][string]$CommandLine
    )

    $inputPath = [System.IO.Path]::GetTempFileName()
    $outputPath = [System.IO.Path]::GetTempFileName()
    $errorPath = [System.IO.Path]::GetTempFileName()
    $harnessPath = [System.IO.Path]::ChangeExtension(
        [System.IO.Path]::GetTempFileName(),
        '.ps1')
    try {
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($inputPath, $Payload, $utf8WithoutBom)
        [System.IO.File]::WriteAllText($harnessPath, $CommandLine, $utf8WithoutBom)
        $command = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$harnessPath`" < `"$inputPath`" > `"$outputPath`" 2> `"$errorPath`""
        cmd.exe /D /S /C $command | Out-Null
        $script:LastWrapperExitCode = $LASTEXITCODE
        $script:LastWrapperError = [System.IO.File]::ReadAllText($errorPath)
        return [System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($outputPath))
    }
    finally {
        Remove-Item -LiteralPath $inputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $errorPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $harnessPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-InstalledHookCommand {
    param(
        [Parameter(Mandatory = $true)][object]$Hooks,
        [Parameter(Mandatory = $true)][string]$EventName
    )

    $handlers = @($Hooks.hooks.$EventName | ForEach-Object { $_.hooks } | Where-Object {
        $_.commandWindows -match 'CodexUsageTray'
    })
    return [string]$handlers[0].commandWindows
}

try {
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    $resolvedBridge = (Resolve-Path -LiteralPath $BridgePath).Path
    $bridgeOutputDirectory = Split-Path -Parent $resolvedBridge
    Copy-Item -Path (Join-Path $bridgeOutputDirectory '*') -Destination $installDirectory -Recurse -Force
    $env:CODEX_HOME = $codexHome
    & $setupPath -BridgePath $installedBridge

    $installedHooks = Get-Content -LiteralPath (Join-Path $codexHome 'hooks.json') -Raw | ConvertFrom-Json
    $stopCommand = Get-InstalledHookCommand -Hooks $installedHooks -EventName 'Stop'
    $promptCommand = Get-InstalledHookCommand -Hooks $installedHooks -EventName 'UserPromptSubmit'
    $permissionCommand = Get-InstalledHookCommand -Hooks $installedHooks -EventName 'PermissionRequest'
    $stopPayload = [ordered]@{
        session_id = 'integration-stop'
        turn_id = 'turn-stop'
        cwd = $testRoot
        hook_event_name = 'Stop'
        stop_hook_active = $false
        last_assistant_message = 'Stop hook JSON integration test'
        permission_mode = 'default'
    } | ConvertTo-Json -Compress
    $stopOutputHex = Invoke-InstalledHookCommand -Payload $stopPayload -CommandLine $stopCommand
    Assert-Equal 0 $script:LastWrapperExitCode `
        "installed Stop hook command exits successfully; stderr=<$script:LastWrapperError>"
    Assert-Equal '7B-22-63-6F-6E-74-69-6E-75-65-22-3A-74-72-75-65-7D' $stopOutputHex `
        'installed Stop hook wrapper emits exact JSON bytes without BOM or newline'

    $invalidStopPayload = 'not-json'
    $invalidStopOutputHex = Invoke-InstalledHookCommand `
        -Payload $invalidStopPayload `
        -CommandLine $stopCommand
    Assert-Equal 0 $script:LastWrapperExitCode 'notification parsing failure never fails the Stop hook'
    Assert-Equal '7B-22-63-6F-6E-74-69-6E-75-65-22-3A-74-72-75-65-7D' $invalidStopOutputHex `
        'malformed Stop input still emits exact success JSON bytes'

    $promptPayload = [ordered]@{
        session_id = 'integration-prompt'
        turn_id = 'turn-prompt'
        cwd = $testRoot
        hook_event_name = 'UserPromptSubmit'
        prompt = 'Prompt hook output isolation test'
        permission_mode = 'default'
    } | ConvertTo-Json -Compress
    $promptOutputHex = Invoke-InstalledHookCommand `
        -Payload $promptPayload `
        -CommandLine $promptCommand
    Assert-Equal 0 $script:LastWrapperExitCode `
        "installed prompt hook command exits successfully; stderr=<$script:LastWrapperError>"
    Assert-Equal '' $promptOutputHex 'UserPromptSubmit emits zero stdout bytes'

    $powerShellPromptOutputHex = Invoke-InstalledHookCommandFromPowerShell `
        -Payload $promptPayload `
        -CommandLine $promptCommand
    Assert-Equal 0 $script:LastWrapperExitCode `
        "installed prompt hook command runs from Codex PowerShell; stderr=<$script:LastWrapperError>"
    Assert-Equal '' $powerShellPromptOutputHex `
        'UserPromptSubmit emits zero stdout bytes from Codex PowerShell'

    $unicodeSummary = '한글 알림 🔔'
    $unicodePromptPayload = [ordered]@{
        session_id = 'integration-unicode'
        turn_id = 'turn-unicode'
        cwd = $testRoot
        hook_event_name = 'UserPromptSubmit'
        prompt = $unicodeSummary
        permission_mode = 'default'
    } | ConvertTo-Json -Compress
    $activityPipe = [System.IO.Pipes.NamedPipeServerStream]::new(
        'CodexUsageTray.Activity.v1',
        [System.IO.Pipes.PipeDirection]::In,
        1,
        [System.IO.Pipes.PipeTransmissionMode]::Byte,
        [System.IO.Pipes.PipeOptions]::Asynchronous)
    $reader = $null
    try {
        $connectionTask = $activityPipe.WaitForConnectionAsync()
        $unicodeOutputHex = Invoke-InstalledHookCommandFromPowerShell `
            -Payload $unicodePromptPayload `
            -CommandLine $promptCommand
        Assert-Equal 0 $script:LastWrapperExitCode `
            "Unicode prompt hook exits successfully; stderr=<$script:LastWrapperError>"
        Assert-Equal '' $unicodeOutputHex 'Unicode UserPromptSubmit emits zero stdout bytes'
        Assert-Equal $true $connectionTask.Wait(5000) 'Unicode activity reaches the tray pipe'
        $reader = [System.IO.StreamReader]::new(
            $activityPipe,
            (New-Object System.Text.UTF8Encoding($false)))
        $receivedActivity = $reader.ReadLine() | ConvertFrom-Json
        Assert-Equal $unicodeSummary $receivedActivity.summary `
            'PowerShell wrapper preserves Korean and emoji Hook JSON'
    }
    finally {
        if ($reader) { $reader.Dispose() }
        $activityPipe.Dispose()
    }

    $powerShellStopOutputHex = Invoke-InstalledHookCommandFromPowerShell `
        -Payload $stopPayload `
        -CommandLine $stopCommand
    Assert-Equal 0 $script:LastWrapperExitCode `
        "installed Stop hook command runs from Codex PowerShell; stderr=<$script:LastWrapperError>"
    Assert-Equal '7B-22-63-6F-6E-74-69-6E-75-65-22-3A-74-72-75-65-7D' $powerShellStopOutputHex `
        'Stop emits exact success JSON bytes from Codex PowerShell'

    $spoofedPermissionPayload = $stopPayload
    $permissionOutputHex = Invoke-InstalledHookCommand `
        -Payload $spoofedPermissionPayload `
        -CommandLine $permissionCommand
    Assert-Equal 0 $script:LastWrapperExitCode `
        "installed permission hook command exits successfully; stderr=<$script:LastWrapperError>"
    Assert-Equal '' $permissionOutputHex 'PermissionRequest emits zero stdout bytes even when input claims Stop'

}
finally {
    $env:CODEX_HOME = $previousCodexHome
    foreach ($nativeHostKey in @(
            "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$nativeHostName",
            "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\$nativeHostName")) {
        Remove-Item -LiteralPath $nativeHostKey -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host '16 EventBridge Hook integration assertions passed'
