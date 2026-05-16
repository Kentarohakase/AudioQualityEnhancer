# AudioQualityEnhancer 0.2.1

Release date: 2026-05-16

## Highlights

- New Premiere Pro export profile.
- Premiere Pro export creates WAV 24 Bit / 48 kHz for editing workflows.
- README was rewritten to be shorter and easier to read.

## Also included from 0.2.0

- Music and speech presets can now use two-pass `loudnorm`.
- The app shows where FFmpeg and FFprobe were found.
- Original and processed audio can be previewed directly in the UI.
- The progress area now shows the active phase.
- The selected FFmpeg filter chain is visible before processing.
- A release package can be created with `scripts/package-release.ps1`.

## Portable FFmpeg

The application searches for tools in this order:

1. Next to `AudioQualityEnhancer.exe`
2. In `Tools/`
3. In the Windows `PATH`

Create a portable ZIP without FFmpeg:

```powershell
.\scripts\package-release.ps1 -Version 0.2.1
```

Create a portable ZIP with FFmpeg and FFprobe copied into `Tools/`:

```powershell
.\scripts\package-release.ps1 -Version 0.2.1 -IncludeFFmpeg
```

The bundled package uses the suffix `win-x64-with-ffmpeg`.

FFmpeg binaries are not committed to this repository.

## Notes

Two-pass loudness normalization improves target accuracy, but it still does not restore information that was already lost through bad recording, clipping, or lossy compression.
