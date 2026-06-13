using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Core: dependencies, state, construction, settings, localization and INPC plumbing.
// Feature areas live in the MainViewModel.*.cs partial files.
public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly FileNameService _fileNameService;
    private readonly LogService _logService;
    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly AudioPreviewController _audioPreviewController;
    private readonly FFmpegService _ffmpegService;
    private readonly FFprobeService _ffprobeService;
    private readonly AudioDiagnosticsService _audioDiagnosticsService;
    private readonly AudioAnalysisInsightService _audioAnalysisInsightService;
    private readonly AudioProfileAdvisorService _audioProfileAdvisorService;
    private readonly AudioProcessingService _audioProcessingService;
    private readonly AudioProcessedPreviewService _audioProcessedPreviewService;
    private readonly AudioValidationService _audioValidationService;
    private readonly QualityReportService _qualityReportService;
    private readonly BatchQueueService _batchQueueService;
    private readonly BatchQueueViewService _batchQueueViewService;
    private readonly SettingsService _settingsService;
    private readonly ShellInteractionService _shellInteractionService;
    private readonly YtDlpDownloadService _ytDlpDownloadService;
    private readonly AsyncRelayCommand _selectFileCommand;
    private readonly AsyncRelayCommand _downloadFromUrlCommand;
    private readonly AsyncRelayCommand _analyzeDiagnosticsCommand;
    private readonly RelayCommand _selectOutputFolderCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _removeSelectedFileCommand;
    private readonly RelayCommand _clearFinishedFilesCommand;
    private readonly AsyncRelayCommand _retrySelectedFileCommand;
    private readonly AsyncRelayCommand _retryFailedFilesCommand;
    private readonly RelayCommand _playSourceCommand;
    private readonly RelayCommand _playOutputCommand;
    private readonly AsyncRelayCommand _renderProcessedPreviewCommand;
    private readonly RelayCommand _playProcessedPreviewCommand;
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _openOutputFolderCommand;
    private readonly RelayCommand _openSelectedOutputCommand;
    private readonly RelayCommand _openSelectedOutputFolderCommand;
    private readonly RelayCommand _openLastOutputCommand;
    private readonly RelayCommand _openLastReportCommand;
    private readonly RelayCommand _openLastReportFolderCommand;
    private readonly RelayCommand _copyLogCommand;
    private readonly RelayCommand _clearLogCommand;
    private readonly RelayCommand<AudioProfileSuggestion> _applyProfileSuggestionCommand;

    private ToolStatus? _ffmpegStatus;
    private ToolStatus? _ffprobeStatus;
    private string _inputPath = string.Empty;
    private string _youTubeUrl = string.Empty;
    private string _outputDirectory = string.Empty;
    private AudioInfo? _audioInfo;
    private AudioDiagnostics? _audioDiagnostics;
    private AudioAnalysisReport? _analysisReport;
    private AudioProfileAdvice? _profileAdvice;
    private AudioComparisonReport? _comparisonReport;
    private BatchProcessingItem? _selectedBatchItem;
    private BatchQueueFilterOption _selectedBatchFilter = BatchQueueFilterOption.AllItems;
    private AudioStreamInfo? _selectedAudioStream;
    private AudioPreset? _selectedPreset;
    private ExportFormat? _selectedExportFormat;
    private string _statusText;
    private string _qualityNotice = string.Empty;
    private string _analysisWarningText = string.Empty;
    private string _logText = string.Empty;
    private string _toolStatusText;
    private string _processingPhaseText;
    private string _filterDetailsText = string.Empty;
    private string _batchSummaryText;
    private LanguageOption _selectedLanguage = LanguageOption.German;
    private ThemeOption _selectedTheme = ThemeOption.Light;
    private LoudnessTargetOption _selectedLoudnessTarget = LoudnessTargetOption.Auto;
    private bool _enableNoiseTracking;
    private bool _ytDlpAutoUpdate = true;
    private string _ytDlpLastUpdateCheckUtc = string.Empty;
    private string _lastOutputPath = string.Empty;
    private string _lastReportPath = string.Empty;
    private double _progressValue;
    private double _overallProgressValue;
    private bool _isBusy;
    private bool _saveLogFile = true;
    private bool _saveReportFile = true;
    private bool _enableSpeechCompression;
    private bool _enableSpeechPresenceBoost = true;
    private bool _useTwoPassLoudness = true;
    private int _noiseReductionFloor = -25;
    private bool _initialized;
    private bool _syncingSelectedBatchItem;
    private CancellationTokenSource? _processingCancellation;
    private CancellationTokenSource? _diagnosticsCancellation;
    private bool _hasAnalysisWarnings;

    private bool _updatingPositionFromTimer;
    private double _previewPositionSeconds;
    private double _previewDurationSeconds;
    private bool _isPreviewActive;
    private string? _activePreviewLabelKey;
    private string _processedPreviewPath = string.Empty;
    private string _processedPreviewCacheKey = string.Empty;
    private bool _isProcessedPreviewRendering;
    private string? _statusTextResourceKey = "Status_Ready";
    private object?[] _statusTextArguments = Array.Empty<object?>();
    private string? _processingPhaseResourceKey = "Phase_Ready";
    private object?[] _processingPhaseArguments = Array.Empty<object?>();
    private int _logRefreshQueued;

    public MainViewModel()
    {
        _fileNameService = new FileNameService();
        _logService = new LogService();
        _toolDiscoveryService = new ToolDiscoveryService();
        var processRunner = new ProcessRunner();
        _audioPreviewController = new AudioPreviewController(new AudioPreviewService());
        _ffmpegService = new FFmpegService(_toolDiscoveryService, processRunner);
        _ffprobeService = new FFprobeService(_fileNameService, _toolDiscoveryService, processRunner);
        _audioDiagnosticsService = new AudioDiagnosticsService(_toolDiscoveryService, processRunner);
        _audioAnalysisInsightService = new AudioAnalysisInsightService();
        _audioProfileAdvisorService = new AudioProfileAdvisorService(_fileNameService);
        _audioProcessingService = new AudioProcessingService(_ffmpegService, _ffprobeService, _fileNameService, _logService);
        _audioProcessedPreviewService = new AudioProcessedPreviewService(_ffmpegService);
        _audioValidationService = new AudioValidationService(_ffprobeService, _audioDiagnosticsService, _logService);
        _qualityReportService = new QualityReportService();
        _batchQueueService = new BatchQueueService(_fileNameService);
        _batchQueueViewService = new BatchQueueViewService(_batchQueueService);
        _settingsService = App.SettingsService;
        _shellInteractionService = new ShellInteractionService();
        _ytDlpDownloadService = new YtDlpDownloadService(_toolDiscoveryService, _fileNameService, processRunner);

        _statusText = LocalizationService.Instance["Status_Ready"];
        _toolStatusText = LocalizationService.Instance["Tools_Checking"];
        _processingPhaseText = LocalizationService.Instance["Phase_Ready"];
        _batchSummaryText = LocalizationService.Instance["BatchSummary_Empty"];

        _logService.LogAdded += OnLogAdded;
        _audioPreviewController.Tick += OnPreviewTimerTick;
        _audioPreviewController.PlaybackFailed += OnPlaybackFailed;
        _audioPreviewController.PlaybackEnded += OnPlaybackEnded;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;

        Presets = new ObservableCollection<AudioPreset>(AudioPreset.All);
        ExportFormats = new ObservableCollection<ExportFormat>(ExportFormat.All);
        LoudnessTargets = new ObservableCollection<LoudnessTargetOption>(LoudnessTargetOption.All);
        Languages = new ObservableCollection<LanguageOption>(LanguageOption.All);
        Themes = new ObservableCollection<ThemeOption>(ThemeOption.All);
        BatchFilters = new ObservableCollection<BatchQueueFilterOption>(BatchQueueFilterOption.All);
        BatchItems = new ObservableCollection<BatchProcessingItem>();
        BatchItems.CollectionChanged += OnBatchItemsChanged;
        BatchItemsView = CollectionViewSource.GetDefaultView(BatchItems);
        BatchItemsView.Filter = FilterBatchItem;

        _selectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsBusy);
        _downloadFromUrlCommand = new AsyncRelayCommand(DownloadFromUrlAsync, CanDownloadFromUrl);
        _analyzeDiagnosticsCommand = new AsyncRelayCommand(AnalyzeDiagnosticsAsync, CanAnalyzeDiagnostics);
        _selectOutputFolderCommand = new RelayCommand(SelectOutputFolder, () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessing);
        _cancelCommand = new RelayCommand(CancelProcessing, () => IsBusy);
        _removeSelectedFileCommand = new RelayCommand(RemoveSelectedFile, () => !IsBusy && SelectedBatchItem is not null);
        _clearFinishedFilesCommand = new RelayCommand(ClearFinishedFiles, () => !IsBusy && _batchQueueService.GetFinishedItems(BatchItems).Count > 0);
        _retrySelectedFileCommand = new AsyncRelayCommand(RetrySelectedFileAsync, CanRetrySelectedFile);
        _retryFailedFilesCommand = new AsyncRelayCommand(RetryFailedFilesAsync, CanRetryFailedFiles);
        _playSourceCommand = new RelayCommand(PlaySourcePreview, () => !IsBusy && File.Exists(InputPath));
        _playOutputCommand = new RelayCommand(PlayOutputPreview, () => !IsBusy && File.Exists(LastOutputPath));
        _renderProcessedPreviewCommand = new AsyncRelayCommand(RenderProcessedPreviewAsync, CanRenderProcessedPreview);
        _playProcessedPreviewCommand = new RelayCommand(PlayProcessedPreview, CanPlayProcessedPreview);
        _stopPreviewCommand = new RelayCommand(StopPreview);
        _openOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => Directory.Exists(OutputDirectory));
        _openSelectedOutputCommand = new RelayCommand(OpenSelectedOutput, CanOpenSelectedOutput);
        _openSelectedOutputFolderCommand = new RelayCommand(OpenSelectedOutputFolder, CanOpenSelectedOutputFolder);
        _openLastOutputCommand = new RelayCommand(OpenLastOutput, () => File.Exists(LastOutputPath));
        _openLastReportCommand = new RelayCommand(OpenLastReport, () => File.Exists(LastReportPath));
        _openLastReportFolderCommand = new RelayCommand(OpenLastReportFolder, CanOpenLastReportFolder);
        _copyLogCommand = new RelayCommand(CopyLog, () => !string.IsNullOrWhiteSpace(LogText));
        _clearLogCommand = new RelayCommand(ClearLog, () => !IsBusy && !string.IsNullOrWhiteSpace(LogText));
        _applyProfileSuggestionCommand = new RelayCommand<AudioProfileSuggestion>(ApplyProfileSuggestion, suggestion => !IsBusy && suggestion is not null);

        ApplySettings(_settingsService.Load());
        UpdateQualityNotice();
        UpdateFilterDetails();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ApplySettings(AppSettings settings)
    {
        SelectedLanguage = LanguageOption.All.FirstOrDefault(l => l.Code == settings.Language) ?? LanguageOption.German;
        SelectedTheme = ThemeOption.All.FirstOrDefault(t => t.Theme == ThemeService.Parse(settings.Theme)) ?? ThemeOption.Light;
        SelectedPreset = AudioPreset.All.FirstOrDefault(p => p.Id == settings.PresetId) ?? AudioPreset.Music;
        SelectedExportFormat = ExportFormat.All.FirstOrDefault(f => f.Id == settings.ExportFormatId) ?? ExportFormat.Flac;
        OutputDirectory = !string.IsNullOrWhiteSpace(settings.OutputDirectory) && Directory.Exists(settings.OutputDirectory)
            ? settings.OutputDirectory
            : GetDefaultOutputDirectory();
        SaveLogFile = settings.SaveLogFile;
        SaveReportFile = settings.SaveReportFile;
        EnableSpeechCompression = settings.EnableSpeechCompression;
        EnableSpeechPresenceBoost = settings.EnableSpeechPresenceBoost;
        UseTwoPassLoudness = settings.UseTwoPassLoudness;
        NoiseReductionFloor = settings.NoiseReductionFloor;
        SelectedLoudnessTarget = LoudnessTargetOption.All.FirstOrDefault(t => t.Id == settings.LoudnessTargetId) ?? LoudnessTargetOption.Auto;
        EnableNoiseTracking = settings.EnableNoiseTracking;
        _ytDlpAutoUpdate = settings.YtDlpAutoUpdate;
        _ytDlpLastUpdateCheckUtc = settings.YtDlpLastUpdateCheckUtc;
        WindowWidth = settings.WindowWidth;
        WindowHeight = settings.WindowHeight;
        WindowMaximized = settings.WindowMaximized;
    }

    /// <summary>Window metrics are set by the view on close and applied by it on startup.</summary>
    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public void PersistSettings()
    {
        var settings = new AppSettings
        {
            Language = SelectedLanguage?.Code ?? LanguageOption.German.Code,
            Theme = (SelectedTheme?.Theme ?? AppTheme.Light).ToString(),
            PresetId = SelectedPreset?.Id ?? AudioPreset.Music.Id,
            ExportFormatId = SelectedExportFormat?.Id ?? ExportFormat.Flac.Id,
            OutputDirectory = OutputDirectory,
            SaveLogFile = SaveLogFile,
            SaveReportFile = SaveReportFile,
            EnableSpeechCompression = EnableSpeechCompression,
            EnableSpeechPresenceBoost = EnableSpeechPresenceBoost,
            UseTwoPassLoudness = UseTwoPassLoudness,
            NoiseReductionFloor = NoiseReductionFloor,
            LoudnessTargetId = SelectedLoudnessTarget?.Id ?? LoudnessTargetOption.Auto.Id,
            EnableNoiseTracking = EnableNoiseTracking,
            YtDlpAutoUpdate = _ytDlpAutoUpdate,
            YtDlpLastUpdateCheckUtc = _ytDlpLastUpdateCheckUtc,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            WindowMaximized = WindowMaximized
        };
        _settingsService.Save(settings);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _logService.Info(LocalizationService.Instance["Log_CheckingTools"]);

        _ffmpegStatus = await _toolDiscoveryService.GetStatusAsync("ffmpeg", CancellationToken.None);
        _ffprobeStatus = await _toolDiscoveryService.GetStatusAsync("ffprobe", CancellationToken.None);
        RefreshToolStatusText();

        if (_ffmpegStatus.IsAvailable)
        {
            _logService.Info(_ffmpegStatus.VersionLine ?? LocalizationService.Instance["Log_FFmpegFound"]);
        }
        else
        {
            _logService.Warning(_ffmpegStatus.ErrorMessage ?? LocalizationService.Instance["Log_FFmpegUnavailable"]);
        }

        if (_ffprobeStatus.IsAvailable)
        {
            _logService.Info(_ffprobeStatus.VersionLine ?? LocalizationService.Instance["Log_FFprobeFound"]);
        }
        else
        {
            _logService.Warning(_ffprobeStatus.ErrorMessage ?? LocalizationService.Instance["Log_FFprobeUnavailable"]);
        }

        _ = PrepareYtDlpAsync();
    }

    private void OnLogAdded(object? sender, string line)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            LogText = _logService.CurrentText;
            return;
        }

        // Coalesce bursts of log lines (e.g. FFmpeg progress output) into a single
        // asynchronous UI refresh so the process reader threads never block on the UI.
        if (Interlocked.Exchange(ref _logRefreshQueued, 1) == 0)
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _logRefreshQueued, 0);
                LogText = _logService.CurrentText;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        RefreshLocalizedState();
        foreach (var item in BatchItems)
        {
            item.RefreshLocalizedText();
        }

        UpdateAnalysisWarnings();
        UpdateAnalysisReport();
        UpdateProfileAdvice();
        if (SelectedBatchItem is not null)
        {
            SelectedBatchItem.SetAnalysisReport(AnalysisReport);
        }

        UpdateQualityNotice();
        UpdateFilterDetails();
        UpdateBatchSummary();
        OnPropertyChanged(nameof(AvailableAudioStreams));
        OnPropertyChanged(nameof(SelectedAudioStream));
        OnPropertyChanged(nameof(ComparisonReport));
    }

    private void RefreshLocalizedState()
    {
        RefreshToolStatusText();

        if (_activePreviewLabelKey is not null && IsPreviewActive)
        {
            SetStatus("Status_PreviewPlayingFormat", LocalizationService.Instance[_activePreviewLabelKey]);
        }
        else if (_statusTextResourceKey is not null)
        {
            StatusText = FormatLocalized(_statusTextResourceKey, _statusTextArguments);
        }

        if (_processingPhaseResourceKey is not null)
        {
            ProcessingPhaseText = FormatLocalized(_processingPhaseResourceKey, _processingPhaseArguments);
        }
    }

    private void RefreshToolStatusText()
    {
        ToolStatusText = _ffmpegStatus is null || _ffprobeStatus is null
            ? LocalizationService.Instance["Tools_Checking"]
            : $"{_ffmpegStatus.DisplayText} | {_ffprobeStatus.DisplayText}";
    }

    private void SetStatus(string resourceKey, params object?[] arguments)
    {
        _statusTextResourceKey = resourceKey;
        _statusTextArguments = arguments;
        StatusText = FormatLocalized(resourceKey, arguments);
    }

    private void SetStatusRaw(string text)
    {
        _statusTextResourceKey = null;
        _statusTextArguments = Array.Empty<object?>();
        StatusText = text;
    }

    private void SetProcessingPhase(string resourceKey, params object?[] arguments)
    {
        _processingPhaseResourceKey = resourceKey;
        _processingPhaseArguments = arguments;
        ProcessingPhaseText = FormatLocalized(resourceKey, arguments);
    }

    private void SetProcessingPhaseRaw(string text)
    {
        _processingPhaseResourceKey = null;
        _processingPhaseArguments = Array.Empty<object?>();
        ProcessingPhaseText = text;
    }

    private static string FormatLocalized(string resourceKey, object?[] arguments)
    {
        return arguments.Length == 0
            ? LocalizationService.Instance[resourceKey]
            : LocalizationService.Instance.Format(resourceKey, arguments);
    }

    private void RaiseCommandStates()
    {
        _selectFileCommand.RaiseCanExecuteChanged();
        _downloadFromUrlCommand.RaiseCanExecuteChanged();
        _analyzeDiagnosticsCommand.RaiseCanExecuteChanged();
        _selectOutputFolderCommand.RaiseCanExecuteChanged();
        _startCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _removeSelectedFileCommand.RaiseCanExecuteChanged();
        _clearFinishedFilesCommand.RaiseCanExecuteChanged();
        _retrySelectedFileCommand.RaiseCanExecuteChanged();
        _retryFailedFilesCommand.RaiseCanExecuteChanged();
        _playSourceCommand.RaiseCanExecuteChanged();
        _playOutputCommand.RaiseCanExecuteChanged();
        _renderProcessedPreviewCommand.RaiseCanExecuteChanged();
        _playProcessedPreviewCommand.RaiseCanExecuteChanged();
        _stopPreviewCommand.RaiseCanExecuteChanged();
        _openOutputFolderCommand.RaiseCanExecuteChanged();
        _openSelectedOutputCommand.RaiseCanExecuteChanged();
        _openSelectedOutputFolderCommand.RaiseCanExecuteChanged();
        _openLastOutputCommand.RaiseCanExecuteChanged();
        _openLastReportCommand.RaiseCanExecuteChanged();
        _openLastReportFolderCommand.RaiseCanExecuteChanged();
        _copyLogCommand.RaiseCanExecuteChanged();
        _clearLogCommand.RaiseCanExecuteChanged();
        _applyProfileSuggestionCommand.RaiseCanExecuteChanged();
    }

    private static string GetDefaultOutputDirectory()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Directory.Exists(music) ? music : Environment.CurrentDirectory;
    }

    private static string? GetExistingDirectoryForFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
            ? null
            : directory;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _audioPreviewController.Tick -= OnPreviewTimerTick;
        _audioPreviewController.PlaybackFailed -= OnPlaybackFailed;
        _audioPreviewController.PlaybackEnded -= OnPlaybackEnded;
        BatchItems.CollectionChanged -= OnBatchItemsChanged;
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        _processingCancellation?.Dispose();
        _diagnosticsCancellation?.Dispose();
        _audioPreviewController.Dispose();
        InvalidateProcessedPreview();
        foreach (var item in BatchItems)
        {
            item.PropertyChanged -= OnBatchItemPropertyChanged;
            item.Dispose();
        }
    }
}
