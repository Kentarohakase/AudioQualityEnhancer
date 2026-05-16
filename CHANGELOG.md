# Changelog

## 0.2.1 - 2026-05-16

### Added

- Added a Premiere Pro export profile.
- Premiere Pro exports use WAV 24 Bit at 48 kHz for editing-friendly compatibility.

### Changed

- Rewrote the README to be shorter, clearer, and less technical.
- Premiere Pro output names now include `premiere_pro`.

## 0.2.0 - 2026-05-16

### Added

- Optional two-pass loudness normalization for music and speech presets.
- Tool discovery status for FFmpeg and FFprobe in the app header.
- Before/after preview controls for source and processed output.
- Processing phase display next to progress percentage.
- Filter details preview for the selected preset and options.
- Portable `Tools/` folder support for `ffmpeg.exe` and `ffprobe.exe`.
- PowerShell release packaging script with optional FFmpeg bundling.
- GitHub Actions workflow for Windows release artifacts.

### Changed

- Audio processing now reports structured phase progress instead of only a percentage.
- UI layout was tightened and expanded with preview, phase, and filter information.
- README now documents portable packaging and FFmpeg bundling.

### Fixed

- Temporary folders are explicitly excluded from project compilation.

## 0.1.0 - 2026-05-16

### Added

- Initial WPF/MVVM app with FFmpeg and FFprobe integration.
- Audio analysis, presets, export formats, logging, cancellation, and safe output naming.
- MIT license.
