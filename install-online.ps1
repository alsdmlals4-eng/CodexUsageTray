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
        throw "Invalid SHA-256 checksum file: $resolvedPath"
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
        throw 'Expected SHA-256 checksum format is invalid.'
    }

    $actualSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actualSha256,
            $ExpectedSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Downloaded archive checksum mismatch. Expected: $ExpectedSha256, actual: $actualSha256"
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

        Write-Host 'Downloading the latest Codex Usage Tray release...'
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
        Write-Host 'Download verification completed.'

        Expand-Archive `
            -LiteralPath $archivePath `
            -DestinationPath $expandedDirectory `
            -Force

        $releaseInstaller = Join-Path $expandedDirectory 'install-release.ps1'
        if (-not (Test-Path -LiteralPath $releaseInstaller -PathType Leaf)) {
            throw 'install-release.ps1 was not found in the release archive.'
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
