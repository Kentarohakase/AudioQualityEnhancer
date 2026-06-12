# Changelog

## 0.14.0 - 2026-06-12

### Added

- Added an inactivity watchdog: FFmpeg/FFprobe processes that stop responding are terminated and reported instead of hanging the run.
- Added a free disk space check before processing starts, with needed/free numbers in the error message.
- Added folder support for drag and drop; dropped folders contribute their supported audio files recursively.
- Added window size and maximized-state persistence across sessions.
- Added a selectable loudness target (-14 LUFS streaming, -16 LUFS podcast, -23 LUFS EBU R128 broadcast) with the preset default as the automatic fallback; result validation checks against the selected target.
- Added an optional adaptive noise tracking mode for the noise reduction presets for material with varying background noise.
- Added safe cleanup for stale processed-preview WAV files.
- Added tests covering processed-preview cache invalidation for filter options and selected audio streams.
- Added settings persistence tests for corrupt, missing and non-writable settings paths.

### Changed

- Mono sources are now normalized with a dual-mono EBU R128 measurement so they no longer end up about 3 LU too loud.
- Lossy export formats now get extra true-peak headroom (-2.0 dBTP instead of -1.5 dBTP) because lossy encoding can push peaks above the limited value.
- The preset preview now uses the same two-pass linear loudness normalization as the export and renders in 24-bit.
- The preset preview of longer sources now plays the loudest section instead of always the first 20 seconds.
- Adding multiple files now runs the analyses in parallel, and FFmpeg/FFprobe discovery results are cached for faster batch processing.
- The main view model is organized into feature-focused partial classes; log output is batched into fewer UI updates during processing.
- The main window XAML is organized into section UserControls with shared styles in an application-level resource dictionary.
- The in-memory log is now capped so very large batch runs keep the UI responsive; the oldest lines are replaced by a truncation marker.
- Missing output loudness/peak diagnostics now produce a warning for loudness-changing presets.

### Fixed

- Fixed loudness-normalized exports being written at 192 kHz instead of the source sample rate (oversized WAV/FLAC files, 96 kHz AAC).

## 0.13.1 - 2026-06-04

### Fixed

- Stabilized release package verification tests for GitHub Actions by avoiding fragile PowerShell error-message assertions.

## 0.13.0 - 2026-06-04

### Added

- Added an explicit `asInvoker` application manifest so the app does not request administrator privileges.
- Added SHA256 checksum files for release ZIP packages.
- Added release verification for checksum files.
- Added README guidance for normal user execution, SmartScreen warnings and checksum verification.

### Changed

- Release workflow now uploads checksum files alongside ZIP packages.

## 0.12.7 - 2026-06-04

### Added

- Added an output-folder writability check before FFmpeg processing starts.
- Added safe cleanup for stale AudioQualityEnhancer-owned temporary export files.
- Added result status counts to Markdown reports.

### Changed

- Batch warning filters now include every item with result-check warnings or errors, not only completed items.
- Result report findings are ordered by severity and include their localized category.
- The batch grid gives the result-checking status and result status a little more room.

## 0.12.6 - 2026-06-04

### Added

- Added a dedicated batch validation status so queue items visibly move from export to result checking.
- Added localized finding categories for analysis and result-check notes in the UI.
- Added an internal FFmpeg render plan helper to make argument generation easier to test.

### Changed

- Result reports now include a short per-file verdict before detailed findings and metrics.
- Batch processing state transitions for export and validation now live in the queue service instead of the main view model.

## 0.12.5 - 2026-06-04

### Added

- Added a processed preset preview that renders a temporary 20-second WAV so the original and current preset can be compared before export.
- Added clearer analysis findings for mono sources, multichannel sources and lossy transcoding risks.
- Added result-check warnings for unexpected codec, sample-rate and channel-count changes after export.

### Changed

- Centralized archive export format resolution so archive processing and validation consistently use FLAC.
- Improved FFmpeg failure messages for locked files, unavailable paths, unsupported codecs, invalid arguments and unreadable sources.

## 0.12.4 - 2026-06-04

### Added

- Added `Podcast Voice` and `Noisy Speech Cleanup` presets for clearer speech, voiceovers and noisy speech recordings.
- Added de-essing, low-mid cleanup and conservative speech cleanup filter chains for speech-focused exports.

### Changed

- Profile advice now prioritizes the new speech presets for mono, low-bitrate or low-sample-rate speech-like sources.

## 0.12.3 - 2026-06-03

### Added

- Added a shared process runner for FFmpeg, FFprobe and diagnostics execution with focused cancellation and output-capture tests.
- Added CI format verification and release package verification in the GitHub workflows.

### Changed

- Enabled stricter Release build quality checks with .NET analyzers, code-style enforcement and warnings-as-errors.
- Moved preview playback state, shell interactions and batch queue view helpers out of the main view model to improve maintainability without changing the UI behavior.
- Formatted WPF assembly metadata so `dotnet format --verify-no-changes` can run cleanly in CI.

## 0.12.2 - 2026-05-20

### Added

- Added a quick action for opening the folder of the latest result report.

### Fixed

- Improved batch retry cleanup so stale output and result-check details are cleared from the selected file.
- Improved filtered queue selection after retrying or removing entries.
- Strengthened release package verification for empty third-party notices.

## 0.12.1 - 2026-05-20

### Added

- Added a release package verification script for checking required files, third-party notices and bundled FFmpeg metadata in ZIP packages.

## 0.12.0 - 2026-05-20

### Added

- Added batch review filters for ready, running, finished, warning, failed and cancelled queue entries.
- Added retry actions for selected failed/cancelled entries and all failed/cancelled entries.
- Added quick actions for opening the selected output file or its output folder.
- Added third-party notices for FFmpeg/FFprobe and included those notices in release packages.

## 0.11.1 - 2026-05-17

### Fixed

- Fixed dark-mode text color for recommendation cards, analysis findings and result-check headings.

## 0.11.0 - 2026-05-17

### Added

- Added recommended settings based on source analysis, with clear preset/export suggestions that can be applied manually.
- Added guidance for speech-like sources, video sources, stream-copy candidates, low-headroom material and compact everyday exports.

## 0.10.1 - 2026-05-17

### Fixed

- Fixed ComboBox text and dropdown colors in dark mode so selected values and menu entries stay readable.

## 0.10.0 - 2026-05-17

### Added

- Dark mode: a theme toggle in the header switches the UI between Light and Dark; the choice persists in `settings.json` and is restored on the next start.
- New theme system with two resource dictionaries (`Resources/Themes/LightTheme.xaml` and `DarkTheme.xaml`); all UI brushes are now `DynamicResource`-bound for live switching without restart.
- `ThemeService` singleton with `Apply()` and `Current`, applied at app startup before the main window is created.
- `ThemeOption` model (analog to `LanguageOption`) bound to a `Themes` collection in `MainViewModel`.
- Added release and smoke-test documentation for repeatable manual verification.
- Added a repository hygiene test for public source text.

### Changed

- Reworked main window layout into a cleaner card-based dashboard: each section now has an icon header, larger corner radius, more breathing room and a consistent typography scale (label / value / section-header styles).
- Header redesigned as a slim app bar with title, tool status, theme toggle and language switcher; the status text moved to a bottom row for less clutter.
- All previously inline hex color values are centralized in the theme dictionaries; DataGrid, ProgressBar, ComboBox, TextBox, Buttons and severity badges follow the active theme.
- Severity badges (analysis findings, result validation findings) now use pill-shaped backgrounds with theme-aware foreground/background combinations.
- Expanded `.gitignore` for local workspace, coverage and test output files.

## 0.9.0 - 2026-05-17

### Added

- Added post-export result validation with before/after technical measurements for codec, container, duration, sample rate, channels, file size, loudness and peaks.
- Added warnings for missing, empty or unreadable output files, duration mismatches, possible clipping, low headroom and missed loudness targets.
- Added optional Markdown result reports in the output folder with per-file findings and measured values.

### Fixed

- Output files that cannot be validated are now marked as failed instead of being reported as finished.
- Matroska audio (`.mka`) is accepted for analysis so lossless stream-copy exports for AC3/EAC3/DTS can be checked correctly.

## 0.8.8 - 2026-05-17

### Added

- Added audio track detection and per-file audio track selection for video sources.

## 0.8.1 - 2026-05-16

### Fixed

- `MainWindow.OnClosed` now wraps `PersistSettings()` in a try-finally block so `MainViewModel.Dispose()` always runs even if saving settings throws.
- Drag-and-drop file loading no longer throws `NullReferenceException` when `IDataObject.GetData(FileDrop)` returns null (defensive `as string[]` cast in both `OnDrop` and `GetDroppedFiles`).

## 0.8.0 - 2026-05-16

### Added

- Added a batch queue for adding multiple files, reviewing their analysis and processing them sequentially with shared export settings.

## 0.7.0 - 2026-05-16

### Added

- Added a clearer analysis report with score, findings and practical recommendations based on the existing source and diagnostics data.

### Changed

- Updated CI and release workflows for the current GitHub Actions runtimes and the upcoming Windows runner image migration.

## 0.6.1 - 2026-05-16

### Fixed

- Fixed release packaging after adding the automated test project, so the Windows ZIP packages are built from the main app project again.

## 0.6.0 - 2026-05-16

### Added

- Added automated tests for FFprobe parsing, FFmpeg diagnostics parsing, file naming, export profiles and resource key parity.

## 0.5.1 - 2026-05-16

### Fixed

- Added missing German translations for all v0.5.0 resource keys (button labels, field names, status messages, log lines, error messages and analysis warnings were showing as `!key!` placeholders when the UI language was set to German).
- `ToolStatus.DisplayText` was hardcoded German (`gefunden` / `nicht gefunden`); it now uses `LocalizationService` and live-refreshes when the language is switched.
- File-dialog title (`Audio- oder Videodatei auswählen`) and folder-dialog title (`Ausgabeordner auswählen`) were hardcoded German; both now resolve from resources.
- Open-file-dialog filter labels were hardcoded German; they now come from `Dialog_FilterAudio` / `Dialog_FilterAll`.
- `AudioInfo` accumulated `LocalizationService.PropertyChanged` subscriptions on every file load (memory leak); `AudioInfo` now implements `IDisposable` and `MainViewModel` disposes the previous instance before replacing it.
- Crash dialog in `App.OnDispatcherUnhandledException` was hardcoded German; it now reads `Error_AppCrash` / `Error_CrashLogSaved` from resources.
- `SuggestCopyOutput` reason strings in `FileNameService` were hardcoded German; they now resolve through `CopyReason_*` resource keys.
- `App` and `MainViewModel` each created a separate `SettingsService` instance and loaded `settings.json` twice; `App.SettingsService` is now a static singleton shared by both.
- `AudioPreviewService`: media-event handlers now guard with `ReferenceEquals` against stale sender references; `Stop()` nulls `_player` first and detaches all event handlers before closing, preventing callbacks from firing on a disposed player.

## 0.5.0 - 2026-05-16

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
- Local workspace settings are ignored by Git.

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
