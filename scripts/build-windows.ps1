[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$powershellTests = Join-Path $projectRoot 'tests/PowerShellInstaller.Tests.ps1'
$testProject = Join-Path $projectRoot 'tests/CodexUsageTray.Core.Tests/CodexUsageTray.Core.Tests.csproj'
$appProject = Join-Path $projectRoot 'src/CodexUsageTray/CodexUsageTray.csproj'
$bridgeProject = Join-Path $projectRoot 'src/CodexUsageTray.EventBridge/CodexUsageTray.EventBridge.csproj'
$outputDirectory = Join-Path $projectRoot 'artifacts/win-x64'

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw '.NET 8 SDK를 찾을 수 없습니다. https://dotnet.microsoft.com/download/dotnet/8.0 에서 SDK를 설치하세요.'
}

$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or [int]($sdkVersion.Split('.')[0]) -lt 8) {
    throw ".NET 8 이상 SDK가 필요합니다. 현재 버전: $sdkVersion"
}

Write-Host '[1/4] PowerShell 설치·호환성 테스트'
& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $powershellTests
if ($LASTEXITCODE -ne 0) {
    throw 'PowerShell 설치·호환성 테스트가 실패했습니다.'
}

Write-Host '[2/4] 핵심 로직 테스트'
& dotnet run --configuration Release --project $testProject
if ($LASTEXITCODE -ne 0) {
    throw '테스트가 실패하여 Windows 실행 파일을 만들지 않았습니다.'
}

Write-Host '[3/4] Windows 트레이 앱 게시'
& dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    --output $outputDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Windows 실행 파일 게시에 실패했습니다.'
}

Write-Host '[4/4] Codex Hook 이벤트 브리지 게시'
& dotnet publish $bridgeProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    --output $outputDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Codex Hook 이벤트 브리지 게시에 실패했습니다.'
}

$executable = Join-Path $outputDirectory 'CodexUsageTray.exe'
$bridgeExecutable = Join-Path $outputDirectory 'CodexUsageTray.EventBridge.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $bridgeExecutable -PathType Leaf)) {
    throw "게시 명령은 성공했지만 필요한 실행 파일 두 개가 모두 생성되지 않았습니다: $outputDirectory"
}

Get-Item -LiteralPath $executable, $bridgeExecutable | ForEach-Object {
    $sizeInMegabytes = [math]::Round($_.Length / 1048576, 1)
    Write-Host "완료: $($_.FullName) ($sizeInMegabytes MB)"
}
