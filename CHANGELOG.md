# Changelog

## Unreleased

### Added

- Extended audio analysis with FFmpeg-based loudness, true peak, sample peak, average level and loudness range measurements.
- Analysis warnings for possible clipping, low headroom, low bit rate, low sample rate, very quiet sources and already loud sources.
- A dedicated analysis action in the UI so deeper measurements are optional for long files.
- Quick actions for opening the output folder, opening the latest exported file, copying the log and clearing the log.

## 0.4.1 - 2026-05-16

### Fixed

- Tool status, status text and processing phase now refresh more reliably when the UI language is changed.
- Removed an unnecessary localization event subscription from `ToolStatus`.
- Audio preview now closes the underlying media player on playback errors and detaches media events during cleanup.
- README release examples now use the current version number.
- Local `.claude/` agent settings are ignored by Git.

## 0.4.0 - 2026-05-16

### Added

- Persistent user settings: preset, export format, output directory, log toggle, speech options, two-pass-loudness and noise floor are stored in `%APPDATA%\AudioQualityEnhancer\settings.json` and restored on the next start.
- English UI in addition to German. A language switcher in the header changes the displayed language live without restart; the choice is persisted.
- `LocalizationService` exposing strings through an indexer and raising `Item[]` PropertyChanged when the culture changes, so every WPF binding re-renders.
- Strings resource files (`Resources/Strings.resx` / `Strings.en.resx`) covering window UI text, status messages, log lines, preset and export-format descriptions, audio-info display strings and all service-level error messages.

### Changed

- `AudioPreset`, `ExportFormat`, `AudioInfo` and the new `LanguageOption` implement `INotifyPropertyChanged` and store resource keys instead of literal strings; display properties resolve via `LocalizationService` at read time.
- `AudioInfo.Container` and `AudioInfo.Codec` are now empty when unknown; the view binds to new `ContainerDisplay` / `CodecDisplay` which produce the localized "Unknown".
- Service error/log strings (FFmpeg, FFprobe, AudioProcessing, ToolDiscovery, AudioPreview) come from resources, so errors appear in the user's currently selected language.
- App startup loads the saved language from `settings.json` and applies it before the main window is created; corrupt or missing settings fall back to defaults without crashing.

## 0.3.0 - 2026-05-16

### Added

- Audio preview playback bar with seek slider and elapsed/total time display below the preview buttons.
- `PlaybackEnded` event on `AudioPreviewService`; status automatically resets to "Vorschau beendet" when the audio finishes.
- `Position` (get/set) and `NaturalDuration` on `AudioPreviewService` so the view can read playback state and seek.
- `IsPreviewActive`, `PreviewTimeText`, `PreviewPositionSeconds`, and `PreviewDurationSeconds` on `MainViewModel` for the new playback bar.

### Fixed

- `AudioPreviewService` silently failed when Windows Media Foundation could not play a file. The new `PlaybackFailed` event surfaces the error to the UI instead of leaving the status stuck on "Vorschau läuft".
- `CanStartProcessing` now checks `OutputDirectory`, so the Start button stays disabled when no output folder is set.
- Removed a TOCTOU race in `AudioProcessingService` between `File.Exists` and `File.Move`; the move is now attempted directly and `IOException` is caught when the destination already exists.
- Log filenames now include milliseconds (`yyyyMMdd_HHmmss_fff`) to prevent collisions when two logs are written in the same second.

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
