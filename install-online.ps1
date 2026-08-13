[CmdletBinding()]
param(
    [switch]$LibraryOnly
)

$ErrorActionPreference = 'Stop'
$releaseBaseUrl = 'https://github.com/alsdmlals4-eng/CodexUsageTray/releases/latest/download'
$archiveName = 'CodexUsageTray-win-x64.zip'

function Read-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $content = [System.IO.File]::ReadAllText($resolvedPath).Trim()
    $token = @($content -split '\s+')[0]

    if ($token -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "올바르지 않은 SHA-256 체크섬 파일입니다: $resolvedPath"
    }

    return $token.ToUpperInvariant()
}

function Assert-ArchiveSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256
    )

    if ($ExpectedSha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw '비교할 SHA-256 체크섬의 형식이 올바르지 않습니다.'
    }

    $actualSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actualSha256,
            $ExpectedSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "다운로드 파일의 체크섬이 일치하지 않습니다. 예상: $ExpectedSha256, 실제: $actualSha256"
    }

    return $true
}

function Install-CodexUsageTrayOnline {
    $previousSecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol
    [System.Net.ServicePointManager]::SecurityProtocol = `
        $previousSecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12

    $temporaryDirectory = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        "CodexUsageTray-$([guid]::NewGuid().ToString('N'))"
    $archivePath = Join-Path $temporaryDirectory $archiveName
    $checksumPath = Join-Path $temporaryDirectory "$archiveName.sha256"
    $expandedDirectory = Join-Path $temporaryDirectory 'package'

    try {
        New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

        Write-Host 'Codex Usage Tray 최신 버전을 다운로드합니다...'
        Invoke-WebRequest `
            -Uri "$releaseBaseUrl/$archiveName" `
            -OutFile $archivePath `
            -UseBasicParsing
        Invoke-WebRequest `
            -Uri "$releaseBaseUrl/$archiveName.sha256" `
            -OutFile $checksumPath `
            -UseBasicParsing

        $expectedSha256 = Read-ExpectedSha256 -Path $checksumPath
        [void](Assert-ArchiveSha256 `
            -ArchivePath $archivePath `
            -ExpectedSha256 $expectedSha256)
        Write-Host '다운로드 파일 검증을 완료했습니다.'

        Expand-Archive `
            -LiteralPath $archivePath `
            -DestinationPath $expandedDirectory `
            -Force

        $releaseInstaller = Join-Path $expandedDirectory 'install-release.ps1'
        if (-not (Test-Path -LiteralPath $releaseInstaller -PathType Leaf)) {
            throw '배포 파일에서 install-release.ps1을 찾을 수 없습니다.'
        }

        & $releaseInstaller `
            -PackageDirectory $expandedDirectory `
            -StartWithWindows
    }
    finally {
        [System.Net.ServicePointManager]::SecurityProtocol = $previousSecurityProtocol
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

if (-not $LibraryOnly) {
    Install-CodexUsageTrayOnline
}
