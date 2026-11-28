<#
.SYNOPSIS
    Downloads the Case 07 Hair Material Ball (.vividhair) LookDev groom assets from GitHub Releases
    into Packages/VividRP/Samples/HairLookDev/Grooms/Case07/ and verifies SHA256 integrity.

.DESCRIPTION
    Large pre-baked .vividhair binaries (~185 MB total) are hosted as GitHub Release assets
    rather than stored in Git history. Run this script after cloning to populate
    Samples/HairLookDev/Grooms/Case07/.

.PARAMETER DestinationDir
    Optional override for the target directory (defaults to Samples/HairLookDev/Grooms/Case07).

.PARAMETER Repo
    GitHub repository in "owner/name" format (defaults to "af8a2a/VividRP").

.PARAMETER ReleaseTag
    GitHub Release tag hosting the Case 07 assets (defaults to "assets-hair-case07").

.PARAMETER BaseUrl
    Optional full base URL override (e.g. a GitHub Release mirror) ending without a trailing slash.

.PARAMETER Force
    Re-download all files even if existing local files already match the expected SHA256 hash.

.PARAMETER VerifyOnly
    Verify existing local files against expected sizes and SHA256 hashes without downloading.

.EXAMPLE
    pwsh -File .\Tools~\Download-HairCase07Assets.ps1
    pwsh -File .\Tools~\Download-HairCase07Assets.ps1 -VerifyOnly
#>
[CmdletBinding()]
param(
    [string]$DestinationDir = "",
    [string]$Repo = "af8a2a/VividRP",
    [string]$ReleaseTag = "assets-hair-case07",
    [string]$BaseUrl = "",
    [switch]$Force,
    [switch]$VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$packageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    $DestinationDir = Join-Path $packageRoot "Samples\HairLookDev\Grooms\Case07"
}

$effectiveBaseUrl = if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    "https://github.com/$Repo/releases/download/$ReleaseTag"
} else {
    $BaseUrl.TrimEnd('/')
}

$Assets = @(
    [ordered]@{
        FileName = "Case07_01_BlackHair.vividhair"
        Bytes    = 37968064
        Sha256   = "FB9E7C1E5359063B9D28342CE5988F3D34D6F284A32C6D1734C13F97E317D2EE"
    },
    [ordered]@{
        FileName = "Case07_02_BrownHair.vividhair"
        Bytes    = 49600064
        Sha256   = "F52DC0E5E46F14742798345A75964BAA220780D9E18D799FB85FE10FF1B5C549"
    },
    [ordered]@{
        FileName = "Case07_03_BlondeHair.vividhair"
        Bytes    = 52080064
        Sha256   = "9878C64CA92145AE0DD737DEB276D14BB3D3F58348EFB7F7CD36C6875F99490B"
    },
    [ordered]@{
        FileName = "Case07_04_WhiteHair.vividhair"
        Bytes    = 54560064
        Sha256   = "1974CBFFC56561242FE0103B1A4E3DA232B78253E7AFD3D510B6C63F1F15E1D6"
    }
)

function Test-AssetIntegrity {
    param(
        [string]$Path,
        [long]$ExpectedBytes,
        [string]$ExpectedSha256
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -ne $ExpectedBytes) {
        return $false
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    return $actual.Equals($ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-AssetDownload {
    param(
        [string]$Url,
        [string]$OutFile
    )

    $tmpFile = "$OutFile.download"
    if (Test-Path -LiteralPath $tmpFile) {
        Remove-Item -LiteralPath $tmpFile -Force
    }

    try {
        Invoke-WebRequest -Uri $Url -OutFile $tmpFile -UseBasicParsing

        # Verify canonical VDHR binary magic header
        $fs = [System.IO.File]::OpenRead($tmpFile)
        try {
            $magic = New-Object byte[] 4
            $read = $fs.Read($magic, 0, 4)
        } finally {
            $fs.Dispose()
        }

        if ($read -lt 4 -or [System.Text.Encoding]::ASCII.GetString($magic) -ne "VDHR") {
            throw "Downloaded response from '$Url' is not a valid VDHR (.vividhair) binary."
        }

        Move-Item -LiteralPath $tmpFile -Destination $OutFile -Force
    } catch {
        if (Test-Path -LiteralPath $tmpFile) {
            Remove-Item -LiteralPath $tmpFile -Force
        }
        throw
    }
}

if (-not $VerifyOnly -and -not (Test-Path -LiteralPath $DestinationDir)) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
}

$failed = 0
foreach ($asset in $Assets) {
    $destPath = Join-Path $DestinationDir $asset.FileName
    $sizeMb = [math]::Round($asset.Bytes / 1MB, 2)

    if (-not $Force -and (Test-AssetIntegrity -Path $destPath -ExpectedBytes $asset.Bytes -ExpectedSha256 $asset.Sha256)) {
        Write-Host "[OK]   $($asset.FileName) ($sizeMb MB) - verified SHA256" -ForegroundColor Green
        continue
    }

    if ($VerifyOnly) {
        Write-Host "[MISS] $($asset.FileName) ($sizeMb MB) - missing or checksum mismatch" -ForegroundColor Yellow
        $failed++
        continue
    }

    $url = "$effectiveBaseUrl/$($asset.FileName)"
    Write-Host "[GET]  $($asset.FileName) ($sizeMb MB) from $url..." -ForegroundColor Cyan
    Invoke-AssetDownload -Url $url -OutFile $destPath

    if (-not (Test-AssetIntegrity -Path $destPath -ExpectedBytes $asset.Bytes -ExpectedSha256 $asset.Sha256)) {
        Write-Host "[FAIL] $($asset.FileName) failed SHA256 verification after download." -ForegroundColor Red
        $failed++
    } else {
        Write-Host "[DONE] $($asset.FileName) verified SHA256." -ForegroundColor Green
    }
}

if ($failed -gt 0) {
    exit 1
}

Write-Host "All Case 07 Hair LookDev assets are ready in $DestinationDir." -ForegroundColor Green
