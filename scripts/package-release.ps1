param(
    [string]$Version = "0.2.0",
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

function Assert-InRoot {
    param([string]$PathToCheck)

    $resolvedRoot = (Resolve-Path -LiteralPath $root).Path
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to touch path outside repository: $fullPath"
    }
}

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null

dotnet publish -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
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

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageDir "Tools") | Out-Null

Copy-Item -LiteralPath (Join-Path $publishDir "AudioQualityEnhancer.exe") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "RELEASE_NOTES.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $root "Tools\README.md") -Destination (Join-Path $stageDir "Tools\README.md")

if ($IncludeFFmpeg) {
    $ffmpeg = Get-Command "ffmpeg.exe" -ErrorAction Stop
    $ffprobe = Get-Command "ffprobe.exe" -ErrorAction Stop

    Copy-Item -LiteralPath $ffmpeg.Source -Destination (Join-Path $stageDir "Tools\ffmpeg.exe")
    Copy-Item -LiteralPath $ffprobe.Source -Destination (Join-Path $stageDir "Tools\ffprobe.exe")
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -Force

Write-Host "Release package created:"
Write-Host $zipPath

if ($IncludeFFmpeg) {
    Write-Host "FFmpeg and FFprobe were included in Tools/."
}
else {
    Write-Host "FFmpeg and FFprobe were not bundled. Install them or place them in Tools/."
}
