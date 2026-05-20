param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",

    [switch]$RequireFFmpegPackage
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$artifactsDir = Join-Path $root "artifacts"
$plainZip = Join-Path $artifactsDir "AudioQualityEnhancer-$Version-$Runtime.zip"
$ffmpegZip = Join-Path $artifactsDir "AudioQualityEnhancer-$Version-$Runtime-with-ffmpeg.zip"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-ZipEntries {
    param(
        [string]$ZipPath,
        [string[]]$RequiredEntries,
        [string[]]$ForbiddenEntries = @(),
        [switch]$ValidateVersionFile
    )

    if (-not (Test-Path -LiteralPath $ZipPath)) {
        throw "Release package missing: $ZipPath"
    }

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
        -ValidateVersionFile
}

Write-Host "Release package verification passed for version $Version."
