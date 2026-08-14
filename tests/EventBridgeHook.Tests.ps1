[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BridgePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$setupPath = Join-Path $repositoryRoot 'scripts/setup-integration.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "CodexUsageTray-BridgeHook-$([guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testRoot 'installed'
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

function Invoke-HookWrapper {
    param(
        [Parameter(Mandatory = $true)][string]$Payload,
        [Parameter(Mandatory = $true)][string]$WrapperPath,
        [Parameter(Mandatory = $true)][string]$EventName
    )

    $inputPath = [System.IO.Path]::GetTempFileName()
    $outputPath = [System.IO.Path]::GetTempFileName()
    try {
        $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($inputPath, $Payload, $utf8WithoutBom)
        $command = "call `"$WrapperPath`" $EventName < `"$inputPath`" > `"$outputPath`""
        cmd.exe /D /S /C $command | Out-Null
        $script:LastWrapperExitCode = $LASTEXITCODE
        return [System.BitConverter]::ToString([System.IO.File]::ReadAllBytes($outputPath))
    }
    finally {
        Remove-Item -LiteralPath $inputPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
    }
}

try {
    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    $resolvedBridge = (Resolve-Path -LiteralPath $BridgePath).Path
    $bridgeOutputDirectory = Split-Path -Parent $resolvedBridge
    Copy-Item -Path (Join-Path $bridgeOutputDirectory '*') -Destination $installDirectory -Recurse -Force
    $env:CODEX_HOME = $codexHome
    & $setupPath -BridgePath $installedBridge

    $wrapperPath = Join-Path $installDirectory 'invoke-codex-hook.cmd'
    $stopPayload = [ordered]@{
        session_id = 'integration-stop'
        turn_id = 'turn-stop'
        cwd = $testRoot
        hook_event_name = 'Stop'
        stop_hook_active = $false
        last_assistant_message = 'Stop hook JSON integration test'
        permission_mode = 'default'
    } | ConvertTo-Json -Compress
    $stopOutputHex = Invoke-HookWrapper -Payload $stopPayload -WrapperPath $wrapperPath -EventName 'Stop'
    Assert-Equal 0 $script:LastWrapperExitCode 'installed Stop hook wrapper exits successfully'
    Assert-Equal '7B-22-63-6F-6E-74-69-6E-75-65-22-3A-74-72-75-65-7D' $stopOutputHex `
        'installed Stop hook wrapper emits exact JSON bytes without BOM or newline'

    $invalidStopPayload = 'not-json'
    $invalidStopOutputHex = Invoke-HookWrapper `
        -Payload $invalidStopPayload `
        -WrapperPath $wrapperPath `
        -EventName 'Stop'
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
    $promptOutputHex = Invoke-HookWrapper `
        -Payload $promptPayload `
        -WrapperPath $wrapperPath `
        -EventName 'UserPromptSubmit'
    Assert-Equal 0 $script:LastWrapperExitCode 'installed prompt hook wrapper exits successfully'
    Assert-Equal '' $promptOutputHex 'UserPromptSubmit emits zero stdout bytes'

    $spoofedPermissionPayload = $stopPayload
    $permissionOutputHex = Invoke-HookWrapper `
        -Payload $spoofedPermissionPayload `
        -WrapperPath $wrapperPath `
        -EventName 'PermissionRequest'
    Assert-Equal 0 $script:LastWrapperExitCode 'installed permission hook wrapper exits successfully'
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

Write-Host '8 EventBridge Hook integration assertions passed'
