param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Runtime = "win-x64",
    [switch]$IncludeFFmpeg
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$publishDir = Join-Path $root "publish\$Runtime"
$artifactsDir = Join-Path $root "artifacts"
$packageSuffix = if ($IncludeFFmpeg) { "$Runtime-with-ffmpeg" } else { $Runtime }
$packageName = "AudioQualityEnhancer-$Version-$packageSuffix"
$stageDir = Join-Path $artifactsDir $packageName
$zipPath = Join-Path $artifactsDir "$packageName.zip"
$checksumPath = "$zipPath.sha256.txt"

function Assert-InRoot {
    param([string]$PathToCheck)

    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch path outside repository: $fullPath"
    }
}

function Resolve-ToolBinary {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [long]$MinBytes = 5MB
    )

    $command = Get-Command $Name -ErrorAction Stop
    $resolved = Get-Item -LiteralPath $command.Source

    # A real, statically linked binary (for example an FFmpeg build on PATH) is large
    # enough to bundle as-is.
    if ($resolved.Length -ge $MinBytes) {
        return $resolved.FullName
    }

    # Otherwise the command on PATH is almost certainly a small redirector shim (such as
    # the Chocolatey stub in chocolatey\bin). Bundling that shim produces a package that
    # cannot run on another machine, so locate the real binary in the Chocolatey library.
    $libRoots = @()
    if ($env:ChocolateyInstall) {
        $libRoots += (Join-Path $env:ChocolateyInstall "lib")
    }
    $libRoots += "C:\ProgramData\chocolatey\lib"

    foreach ($libRoot in ($libRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $libRoot)) {
            continue
        }

        $candidate = Get-ChildItem -LiteralPath $libRoot -Recurse -Filter $Name -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Length -ge $MinBytes } |
            Sort-Object Length -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) {
            return $candidate.FullName
        }
    }

    throw "Could not resolve a real '$Name' binary. Only a shim ($($resolved.Length) bytes) was found on PATH and no full binary was located under the Chocolatey library."
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

dotnet publish AudioQualityEnhancer.csproj -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:SatelliteResourceLanguages="de%3Ben" `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path -LiteralPath $stageDir) {
    Assert-InRoot $stageDir
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Assert-InRoot $zipPath
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path -LiteralPath $checksumPath) {
    Assert-InRoot $checksumPath
    Remove-Item -LiteralPath $checksumPath -Force
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageDir "Tools") | Out-Null

Copy-Item -LiteralPath (Join-Path $publishDir "AudioQualityEnhancer.exe") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "Tools\README.md") -Destination (Join-Path $stageDir "Tools\README.md")

if ($IncludeFFmpeg) {
    $ffmpegSource = Resolve-ToolBinary -Name "ffmpeg.exe"
    $ffprobeSource = Resolve-ToolBinary -Name "ffprobe.exe"

    Copy-Item -LiteralPath $ffmpegSource -Destination (Join-Path $stageDir "Tools\ffmpeg.exe")
    Copy-Item -LiteralPath $ffprobeSource -Destination (Join-Path $stageDir "Tools\ffprobe.exe")

    $versionFile = Join-Path $stageDir "Tools\FFMPEG_VERSION.txt"
    $packageDate = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $ffmpegVersion = & $ffmpegSource -version
    $ffprobeVersion = & $ffprobeSource -version

    @(
        "FFmpeg/FFprobe bundle information"
        "Package version: $Version"
        "Package date (UTC): $packageDate"
        ""
        "This package contains ffmpeg.exe and ffprobe.exe as external command-line tools."
        "FFmpeg project: https://ffmpeg.org/"
        "License information: https://ffmpeg.org/legal.html"
        "Source code: https://ffmpeg.org/download.html"
        "Git mirror: https://github.com/FFmpeg/FFmpeg"
        ""
        "ffmpeg -version"
        "---------------"
        $ffmpegVersion
        ""
        "ffprobe -version"
        "----------------"
        $ffprobeVersion
    ) |
        Where-Object { $_ -notmatch "[A-Za-z]:\\" } |
        Set-Content -LiteralPath $versionFile -Encoding UTF8

    # Bundle yt-dlp for the URL download feature; the app keeps it up to date in a
    # writable per-user folder at runtime.
    $ytDlpPath = Join-Path $stageDir "Tools\yt-dlp.exe"
    Invoke-WebRequest -Uri "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" -OutFile $ytDlpPath -UseBasicParsing
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -Force

$checksum = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($checksum.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $checksumPath -Encoding UTF8

Write-Host "Release package created:"
Write-Host $zipPath
Write-Host "SHA256 checksum created:"
Write-Host $checksumPath

if ($IncludeFFmpeg) {
    Write-Host "FFmpeg and FFprobe were included in Tools/."
}
else {
    Write-Host "FFmpeg and FFprobe were not bundled. Install them or place them in Tools/."
}
