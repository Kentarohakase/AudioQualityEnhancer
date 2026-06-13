param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",

    [switch]$RequireFFmpegPackage,

    [long]$MinBundledToolBytes = 5MB
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$artifactsDir = Join-Path $root "artifacts"
$plainZip = Join-Path $artifactsDir "AudioQualityEnhancer-$Version-$Runtime.zip"
$ffmpegZip = Join-Path $artifactsDir "AudioQualityEnhancer-$Version-$Runtime-with-ffmpeg.zip"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-ZipChecksum {
    param(
        [string]$ZipPath
    )

    $checksumPath = "$ZipPath.sha256.txt"
    if (-not (Test-Path -LiteralPath $checksumPath)) {
        throw "SHA256 checksum missing: $checksumPath"
    }

    $line = (Get-Content -LiteralPath $checksumPath -ErrorAction Stop | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "SHA256 checksum file is empty: $checksumPath"
    }

    $parts = $line.Trim() -split "\s+", 2
    $expectedHash = $parts[0]
    if ($expectedHash -notmatch "^[a-fA-F0-9]{64}$") {
        throw "SHA256 checksum is invalid in $(Split-Path -Leaf $checksumPath)."
    }

    if ($parts.Count -gt 1 -and $parts[1] -ne (Split-Path -Leaf $ZipPath)) {
        throw "SHA256 checksum references unexpected file in $(Split-Path -Leaf $checksumPath)."
    }

    $actualHash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA256 checksum mismatch for $(Split-Path -Leaf $ZipPath)."
    }
}

function Test-ZipEntries {
    param(
        [string]$ZipPath,
        [string[]]$RequiredEntries,
        [string[]]$ForbiddenEntries = @(),
        [switch]$ValidateVersionFile,
        [long]$MinToolBytes = 0
    )

    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Release package missing: $ZipPath"
    }

    Test-ZipChecksum -ZipPath $ZipPath

    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace("/", "\") })

        foreach ($entry in $RequiredEntries) {
            if ($entries -notcontains $entry) {
                throw "Required entry missing in $(Split-Path -Leaf $ZipPath): $entry"
            }
        }

        foreach ($entry in $ForbiddenEntries) {
            if ($entries -contains $entry) {
                throw "Unexpected entry found in $(Split-Path -Leaf $ZipPath): $entry"
            }
        }

        $noticeEntry = $zip.Entries | Where-Object { $_.FullName.Replace("/", "\") -eq "THIRD_PARTY_NOTICES.md" } | Select-Object -First 1
        if ($null -eq $noticeEntry -or $noticeEntry.Length -le 0) {
            throw "THIRD_PARTY_NOTICES.md is missing or empty in $(Split-Path -Leaf $ZipPath)."
        }

        if ($ValidateVersionFile) {
            $versionEntry = $zip.Entries | Where-Object { $_.FullName.Replace("/", "\") -eq "Tools\FFMPEG_VERSION.txt" } | Select-Object -First 1
            if ($null -eq $versionEntry) {
                throw "Tools\FFMPEG_VERSION.txt missing in $(Split-Path -Leaf $ZipPath)."
            }

            $reader = New-Object System.IO.StreamReader($versionEntry.Open())
            try {
                $content = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }

            if ($content -match "[A-Za-z]:\\") {
                throw "Tools\FFMPEG_VERSION.txt contains a local Windows path."
            }
        }

        if ($MinToolBytes -gt 0) {
            foreach ($toolName in @("Tools\ffmpeg.exe", "Tools\ffprobe.exe")) {
                $toolEntry = $zip.Entries | Where-Object { $_.FullName.Replace("/", "\") -eq $toolName } | Select-Object -First 1
                if ($null -eq $toolEntry) {
                    throw "$toolName missing in $(Split-Path -Leaf $ZipPath)."
                }

                if ($toolEntry.Length -lt $MinToolBytes) {
                    throw "$toolName in $(Split-Path -Leaf $ZipPath) is only $($toolEntry.Length) bytes; expected a real binary of at least $MinToolBytes bytes (a PATH shim was likely bundled instead of FFmpeg)."
                }
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

$baseEntries = @(
    "AudioQualityEnhancer.exe",
    "README.md",
    "LICENSE",
    "CHANGELOG.md",
    "THIRD_PARTY_NOTICES.md",
    "Tools\README.md"
)

Test-ZipEntries `
    -ZipPath $plainZip `
    -RequiredEntries $baseEntries `
    -ForbiddenEntries @("Tools\ffmpeg.exe", "Tools\ffprobe.exe", "Tools\FFMPEG_VERSION.txt")

if ($RequireFFmpegPackage -or (Test-Path -LiteralPath $ffmpegZip)) {
    Test-ZipEntries `
        -ZipPath $ffmpegZip `
        -RequiredEntries ($baseEntries + @("Tools\ffmpeg.exe", "Tools\ffprobe.exe", "Tools\FFMPEG_VERSION.txt")) `
        -ValidateVersionFile `
        -MinToolBytes $MinBundledToolBytes
}

Write-Host "Release package verification passed for version $Version."
