using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;
using Forms = System.Windows.Forms;

namespace AudioQualityEnhancer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly FileNameService _fileNameService;
    private readonly LogService _logService;
    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly AudioPreviewService _audioPreviewService;
    private readonly FFmpegService _ffmpegService;
    private readonly FFprobeService _ffprobeService;
    private readonly AudioDiagnosticsService _audioDiagnosticsService;
    private readonly AudioAnalysisInsightService _audioAnalysisInsightService;
    private readonly AudioProfileAdvisorService _audioProfileAdvisorService;
    private readonly AudioProcessingService _audioProcessingService;
    private readonly AudioValidationService _audioValidationService;
    private readonly QualityReportService _qualityReportService;
    private readonly BatchQueueService _batchQueueService;
    private readonly SettingsService _settingsService;
    private readonly AsyncRelayCommand _selectFileCommand;
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
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _openOutputFolderCommand;
    private readonly RelayCommand _openSelectedOutputCommand;
    private readonly RelayCommand _openSelectedOutputFolderCommand;
    private readonly RelayCommand _openLastOutputCommand;
    private readonly RelayCommand _openLastReportCommand;
    private readonly RelayCommand _copyLogCommand;
    private readonly RelayCommand _clearLogCommand;
    private readonly RelayCommand<AudioProfileSuggestion> _applyProfileSuggestionCommand;

    private ToolStatus? _ffmpegStatus;
    private ToolStatus? _ffprobeStatus;
    private string _inputPath = string.Empty;
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

    private DispatcherTimer? _previewTimer;
    private bool _updatingPositionFromTimer;
    private double _previewPositionSeconds;
    private double _previewDurationSeconds;
    private bool _isPreviewActive;
    private string? _activePreviewLabelKey;
    private string? _statusTextResourceKey = "Status_Ready";
    private object?[] _statusTextArguments = Array.Empty<object?>();
    private string? _processingPhaseResourceKey = "Phase_Ready";
    private object?[] _processingPhaseArguments = Array.Empty<object?>();

    public MainViewModel()
    {
        _fileNameService = new FileNameService();
        _logService = new LogService();
        _toolDiscoveryService = new ToolDiscoveryService();
        _audioPreviewService = new AudioPreviewService();
        _ffmpegService = new FFmpegService(_toolDiscoveryService);
        _ffprobeService = new FFprobeService(_fileNameService, _toolDiscoveryService);
        _audioDiagnosticsService = new AudioDiagnosticsService(_toolDiscoveryService);
        _audioAnalysisInsightService = new AudioAnalysisInsightService();
        _audioProfileAdvisorService = new AudioProfileAdvisorService(_fileNameService);
        _audioProcessingService = new AudioProcessingService(_ffmpegService, _ffprobeService, _fileNameService, _logService);
        _audioValidationService = new AudioValidationService(_ffprobeService, _audioDiagnosticsService, _logService);
        _qualityReportService = new QualityReportService();
        _batchQueueService = new BatchQueueService(_fileNameService);
        _settingsService = App.SettingsService;

        _statusText = LocalizationService.Instance["Status_Ready"];
        _toolStatusText = LocalizationService.Instance["Tools_Checking"];
        _processingPhaseText = LocalizationService.Instance["Phase_Ready"];
        _batchSummaryText = LocalizationService.Instance["BatchSummary_Empty"];

        _logService.LogAdded += OnLogAdded;
        _audioPreviewService.PlaybackFailed += OnPlaybackFailed;
        _audioPreviewService.PlaybackEnded += OnPlaybackEnded;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;

        Presets = new ObservableCollection<AudioPreset>(AudioPreset.All);
        ExportFormats = new ObservableCollection<ExportFormat>(ExportFormat.All);
        Languages = new ObservableCollection<LanguageOption>(LanguageOption.All);
        Themes = new ObservableCollection<ThemeOption>(ThemeOption.All);
        BatchFilters = new ObservableCollection<BatchQueueFilterOption>(BatchQueueFilterOption.All);
        BatchItems = new ObservableCollection<BatchProcessingItem>();
        BatchItems.CollectionChanged += OnBatchItemsChanged;
        BatchItemsView = CollectionViewSource.GetDefaultView(BatchItems);
        BatchItemsView.Filter = FilterBatchItem;

        _selectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsBusy);
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
        _stopPreviewCommand = new RelayCommand(StopPreview);
        _openOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => Directory.Exists(OutputDirectory));
        _openSelectedOutputCommand = new RelayCommand(OpenSelectedOutput, CanOpenSelectedOutput);
        _openSelectedOutputFolderCommand = new RelayCommand(OpenSelectedOutputFolder, CanOpenSelectedOutputFolder);
        _openLastOutputCommand = new RelayCommand(OpenLastOutput, () => File.Exists(LastOutputPath));
        _openLastReportCommand = new RelayCommand(OpenLastReport, () => File.Exists(LastReportPath));
        _copyLogCommand = new RelayCommand(CopyLog, () => !string.IsNullOrWhiteSpace(LogText));
        _clearLogCommand = new RelayCommand(ClearLog, () => !IsBusy && !string.IsNullOrWhiteSpace(LogText));
        _applyProfileSuggestionCommand = new RelayCommand<AudioProfileSuggestion>(ApplyProfileSuggestion, suggestion => !IsBusy && suggestion is not null);

        ApplySettings(_settingsService.Load());
        UpdateQualityNotice();
        UpdateFilterDetails();
    }

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
    }

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
            NoiseReductionFloor = NoiseReductionFloor
        };
        _settingsService.Save(settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioPreset> Presets { get; }

    public ObservableCollection<ExportFormat> ExportFormats { get; }

    public ObservableCollection<LanguageOption> Languages { get; }

    public ObservableCollection<BatchQueueFilterOption> BatchFilters { get; }

    public ObservableCollection<BatchProcessingItem> BatchItems { get; }

    public ICollectionView BatchItemsView { get; }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedLanguage, value))
            {
                LocalizationService.Instance.Culture = new CultureInfo(value.Code);
            }
        }
    }

    public ObservableCollection<ThemeOption> Themes { get; }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedTheme, value))
            {
                ThemeService.Instance.Apply(value.Theme);
            }
        }
    }

    public ICommand SelectFileCommand => _selectFileCommand;

    public ICommand AnalyzeDiagnosticsCommand => _analyzeDiagnosticsCommand;

    public ICommand SelectOutputFolderCommand => _selectOutputFolderCommand;

    public ICommand StartCommand => _startCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand RemoveSelectedFileCommand => _removeSelectedFileCommand;

    public ICommand ClearFinishedFilesCommand => _clearFinishedFilesCommand;

    public ICommand RetrySelectedFileCommand => _retrySelectedFileCommand;

    public ICommand RetryFailedFilesCommand => _retryFailedFilesCommand;

    public ICommand PlaySourceCommand => _playSourceCommand;

    public ICommand PlayOutputCommand => _playOutputCommand;

    public ICommand StopPreviewCommand => _stopPreviewCommand;

    public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;

    public ICommand OpenSelectedOutputCommand => _openSelectedOutputCommand;

    public ICommand OpenSelectedOutputFolderCommand => _openSelectedOutputFolderCommand;

    public ICommand OpenLastOutputCommand => _openLastOutputCommand;

    public ICommand OpenLastReportCommand => _openLastReportCommand;

    public ICommand CopyLogCommand => _copyLogCommand;

    public ICommand ClearLogCommand => _clearLogCommand;

    public ICommand ApplyProfileSuggestionCommand => _applyProfileSuggestionCommand;

    public string InputPath
    {
        get => _inputPath;
        set
        {
            if (SetProperty(ref _inputPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public AudioInfo? AudioInfo
    {
        get => _audioInfo;
        private set
        {
            if (SetProperty(ref _audioInfo, value))
            {
                OnPropertyChanged(nameof(AvailableAudioStreams));
                OnPropertyChanged(nameof(HasMultipleAudioStreams));
                UpdateAnalysisWarnings();
                UpdateAnalysisReport();
                UpdateProfileAdvice();
                UpdateQualityNotice();
                UpdateFilterDetails();
            }
        }
    }

    public AudioDiagnostics? AudioDiagnostics
    {
        get => _audioDiagnostics;
        private set
        {
            if (SetProperty(ref _audioDiagnostics, value))
            {
                UpdateAnalysisWarnings();
                UpdateAnalysisReport();
                UpdateProfileAdvice();
                UpdateQualityNotice();
            }
        }
    }

    public AudioAnalysisReport? AnalysisReport
    {
        get => _analysisReport;
        private set
        {
            if (SetProperty(ref _analysisReport, value))
            {
                OnPropertyChanged(nameof(HasAnalysisReport));
            }
        }
    }

    public bool HasAnalysisReport => AnalysisReport is not null;

    public AudioProfileAdvice? ProfileAdvice
    {
        get => _profileAdvice;
        private set
        {
            if (SetProperty(ref _profileAdvice, value))
            {
                OnPropertyChanged(nameof(HasProfileAdvice));
            }
        }
    }

    public bool HasProfileAdvice => ProfileAdvice?.HasSuggestions == true;

    public AudioComparisonReport? ComparisonReport
    {
        get => _comparisonReport;
        private set
        {
            if (SetProperty(ref _comparisonReport, value))
            {
                OnPropertyChanged(nameof(HasComparisonReport));
            }
        }
    }

    public bool HasComparisonReport => ComparisonReport is not null;

    public BatchProcessingItem? SelectedBatchItem
    {
        get => _selectedBatchItem;
        set
        {
            if (SetProperty(ref _selectedBatchItem, value))
            {
                SyncSelectedBatchItem();
                RaiseCommandStates();
            }
        }
    }

    public BatchQueueFilterOption SelectedBatchFilter
    {
        get => _selectedBatchFilter;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedBatchFilter, value))
            {
                BatchItemsView.Refresh();
                if (SelectedBatchItem is not null && !BatchItemsView.Contains(SelectedBatchItem))
                {
                    SelectedBatchItem = BatchItemsView.Cast<BatchProcessingItem>().FirstOrDefault();
                }
            }
        }
    }

    public bool HasBatchItems => BatchItems.Count > 0;

    public IReadOnlyList<AudioStreamInfo> AvailableAudioStreams => AudioInfo?.AudioStreams ?? Array.Empty<AudioStreamInfo>();

    public AudioStreamInfo? SelectedAudioStream
    {
        get => _selectedAudioStream;
        set
        {
            if (SetProperty(ref _selectedAudioStream, value) && !_syncingSelectedBatchItem)
            {
                SelectAudioStreamForCurrentItem(value);
            }
        }
    }

    public bool HasMultipleAudioStreams => AudioInfo?.HasMultipleAudioStreams == true;

    public AudioPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedPreset, value))
            {
                ApplyPresetDefaults(value);
                OnPropertyChanged(nameof(IsSpeechPreset));
                OnPropertyChanged(nameof(IsNoisePreset));
                OnPropertyChanged(nameof(IsLoudnessPreset));
                UpdateQualityNotice();
                UpdateFilterDetails();
                RaiseCommandStates();
            }
        }
    }

    public ExportFormat? SelectedExportFormat
    {
        get => _selectedExportFormat;
        set
        {
            if (value is not null && SetProperty(ref _selectedExportFormat, value))
            {
                UpdateQualityNotice();
                UpdateFilterDetails();
                RaiseCommandStates();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string QualityNotice
    {
        get => _qualityNotice;
        private set => SetProperty(ref _qualityNotice, value);
    }

    public string AnalysisWarningText
    {
        get => _analysisWarningText;
        private set => SetProperty(ref _analysisWarningText, value);
    }

    public bool HasAnalysisWarnings
    {
        get => _hasAnalysisWarnings;
        private set => SetProperty(ref _hasAnalysisWarnings, value);
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (SetProperty(ref _logText, value))
            {
                _copyLogCommand.RaiseCanExecuteChanged();
                _clearLogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ToolStatusText
    {
        get => _toolStatusText;
        private set => SetProperty(ref _toolStatusText, value);
    }

    public string ProcessingPhaseText
    {
        get => _processingPhaseText;
        private set => SetProperty(ref _processingPhaseText, value);
    }

    public string FilterDetailsText
    {
        get => _filterDetailsText;
        private set => SetProperty(ref _filterDetailsText, value);
    }

    public string BatchSummaryText
    {
        get => _batchSummaryText;
        private set => SetProperty(ref _batchSummaryText, value);
    }

    public string LastOutputPath
    {
        get => _lastOutputPath;
        private set
        {
            if (SetProperty(ref _lastOutputPath, value))
            {
                OnPropertyChanged(nameof(HasOutputPreview));
                RaiseCommandStates();
            }
        }
    }

    public string LastReportPath
    {
        get => _lastReportPath;
        private set
        {
            if (SetProperty(ref _lastReportPath, value))
            {
                OnPropertyChanged(nameof(HasReportFile));
                RaiseCommandStates();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double OverallProgressValue
    {
        get => _overallProgressValue;
        private set => SetProperty(ref _overallProgressValue, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool SaveLogFile
    {
        get => _saveLogFile;
        set => SetProperty(ref _saveLogFile, value);
    }

    public bool SaveReportFile
    {
        get => _saveReportFile;
        set => SetProperty(ref _saveReportFile, value);
    }

    public bool EnableSpeechCompression
    {
        get => _enableSpeechCompression;
        set
        {
            if (SetProperty(ref _enableSpeechCompression, value))
            {
                UpdateFilterDetails();
            }
        }
    }

    public bool EnableSpeechPresenceBoost
    {
        get => _enableSpeechPresenceBoost;
        set
        {
            if (SetProperty(ref _enableSpeechPresenceBoost, value))
            {
                UpdateFilterDetails();
            }
        }
    }

    public bool UseTwoPassLoudness
    {
        get => _useTwoPassLoudness;
        set
        {
            if (SetProperty(ref _useTwoPassLoudness, value))
            {
                UpdateFilterDetails();
            }
        }
    }

    public int NoiseReductionFloor
    {
        get => _noiseReductionFloor;
        set
        {
            if (SetProperty(ref _noiseReductionFloor, value))
            {
                UpdateFilterDetails();
            }
        }
    }

    public double PreviewPositionSeconds
    {
        get => _previewPositionSeconds;
        set
        {
            if (SetProperty(ref _previewPositionSeconds, value))
            {
                if (!_updatingPositionFromTimer)
                {
                    _audioPreviewService.Position = TimeSpan.FromSeconds(value);
                }

                OnPropertyChanged(nameof(PreviewTimeText));
            }
        }
    }

    public double PreviewDurationSeconds
    {
        get => _previewDurationSeconds;
        private set => SetProperty(ref _previewDurationSeconds, value);
    }

    public bool IsPreviewActive
    {
        get => _isPreviewActive;
        private set => SetProperty(ref _isPreviewActive, value);
    }

    public string PreviewTimeText
    {
        get
        {
            var pos = TimeSpan.FromSeconds(_previewPositionSeconds);
            var dur = TimeSpan.FromSeconds(_previewDurationSeconds);
            var fmt = dur.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss";
            return $"{pos.ToString(fmt, CultureInfo.InvariantCulture)} / {dur.ToString(fmt, CultureInfo.InvariantCulture)}";
        }
    }

    public bool IsSpeechPreset => SelectedPreset?.Id == AudioPreset.Speech.Id;

    public bool IsNoisePreset => SelectedPreset?.Id == AudioPreset.NoiseReduction.Id;

    public bool IsLoudnessPreset => SelectedPreset?.Id == AudioPreset.Music.Id || SelectedPreset?.Id == AudioPreset.Speech.Id;

    public bool HasOutputPreview => File.Exists(LastOutputPath);

    public bool HasReportFile => File.Exists(LastReportPath);

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
    }

    public async Task LoadInputFileAsync(string path)
    {
        await LoadInputFilesAsync(new[] { path });
    }

    public async Task LoadInputFilesAsync(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }

        StopPreview();
        var wasEmpty = BatchItems.Count == 0;
        if (wasEmpty)
        {
            _logService.Clear();
            LastOutputPath = string.Empty;
            LastReportPath = string.Empty;
            OverallProgressValue = 0;
        }

        var addResult = _batchQueueService.CreateItems(paths, BatchItems);
        foreach (var item in addResult.AddedItems)
        {
            AddBatchItem(item);
        }

        foreach (var rejectedPath in addResult.RejectedPaths)
        {
            _logService.Warning(LocalizationService.Instance.Format("Log_BatchSkippedFormat", rejectedPath));
        }

        if (addResult.AddedItems.Count == 0)
        {
            SetStatus("Status_NoValidFilesAdded");
            return;
        }

        if (SelectedBatchItem is null)
        {
            SelectedBatchItem = addResult.AddedItems[0];
        }

        if (wasEmpty)
        {
            var directory = Path.GetDirectoryName(addResult.AddedItems[0].SourcePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                OutputDirectory = directory;
            }
        }

        _logService.Info(LocalizationService.Instance.Format("Log_BatchAddedFilesFormat", addResult.AddedItems.Count));
        foreach (var item in addResult.AddedItems)
        {
            await AnalyzeBatchItemAsync(item, CancellationToken.None);
        }

        SetStatus("Status_BatchReadyFormat", _batchQueueService.GetProcessableItems(BatchItems).Count);
    }

    private async Task SelectFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance["Dialog_SelectFile_Title"],
            Filter = _fileNameService.BuildOpenDialogFilter(),
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadInputFilesAsync(dialog.FileNames);
        }
    }

    private void SelectOutputFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Instance["Dialog_SelectFolder_Title"],
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputDirectory) ? OutputDirectory : GetDefaultOutputDirectory()
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    private async Task AnalyzeDiagnosticsAsync()
    {
        var item = SelectedBatchItem;
        if (item?.AudioInfo is null || string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
        {
            SetStatus("Status_AnalysisFailed");
            return;
        }

        StopPreview();
        _diagnosticsCancellation?.Dispose();
        _diagnosticsCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        SetProcessingPhase("Phase_AdvancedAnalysis");
        SetStatus("Status_AdvancedAnalysisRunning");

        try
        {
            var result = await _audioDiagnosticsService.AnalyzeAsync(
                item.SourcePath,
                item.AudioInfo.Duration,
                _logService.Info,
                value => ProgressValue = value,
                _diagnosticsCancellation.Token,
                item.SelectedAudioStream);

            if (result.IsSuccess && result.Value is not null)
            {
                item.SetAudioDiagnostics(result.Value);
                item.SetAnalysisReport(_audioAnalysisInsightService.BuildReport(item.AudioInfo, result.Value));
                AudioDiagnostics = item.AudioDiagnostics;
                AnalysisReport = item.AnalysisReport;
                ProgressValue = 100;
                SetProcessingPhase("Phase_Ready");
                SetStatus("Status_AdvancedAnalysisDone");
            }
            else
            {
                SetProcessingPhase("Phase_Error");
                SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_AdvancedAnalysisFailed"]);
                _logService.Error(StatusText);

                if (result.Exception is not null)
                {
                    _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
            _diagnosticsCancellation.Dispose();
            _diagnosticsCancellation = null;
        }
    }

    private async Task AnalyzeBatchItemAsync(BatchProcessingItem item, CancellationToken cancellationToken)
    {
        item.Status = BatchProcessingStatus.Analyzing;
        item.ErrorMessage = string.Empty;
        item.Progress = 0;

        if (ReferenceEquals(item, SelectedBatchItem))
        {
            AudioInfo = null;
            AudioDiagnostics = null;
            ComparisonReport = null;
            ProgressValue = 0;
        }

        SetProcessingPhase("Phase_Analysis");
        SetStatus("Status_Analyzing");

        var result = await _ffprobeService.AnalyzeAsync(item.SourcePath, _logService.Info, cancellationToken);
        if (result.IsSuccess && result.Value is not null)
        {
            item.SetAudioInfo(result.Value);
            item.SetAudioDiagnostics(null);
            item.SetAnalysisReport(_audioAnalysisInsightService.BuildReport(result.Value, diagnostics: null));
            item.Progress = 100;
            item.Status = BatchProcessingStatus.Ready;

            if (ReferenceEquals(item, SelectedBatchItem))
            {
                AudioInfo = item.AudioInfo;
                AudioDiagnostics = item.AudioDiagnostics;
                AnalysisReport = item.AnalysisReport;
                ComparisonReport = item.ComparisonReport;
                SelectedAudioStream = item.SelectedAudioStream;
            }

            SetProcessingPhase("Phase_Ready");
            SetStatus("Status_AnalysisDone");
            _logService.Info(LocalizationService.Instance.Format("Log_CodecFormat", result.Value.CodecDisplay));
            _logService.Info(LocalizationService.Instance.Format("Log_ContainerFormat", result.Value.ContainerDisplay));

            if (result.Value.IsLikelyLossy)
            {
                _logService.Warning(result.Value.LossyWarning);
            }
        }
        else
        {
            item.Status = BatchProcessingStatus.Failed;
            item.ErrorMessage = result.ErrorMessage ?? LocalizationService.Instance["Status_AnalysisFailed"];
            item.Progress = 0;

            if (ReferenceEquals(item, SelectedBatchItem))
            {
                AudioInfo = item.AudioInfo;
                AudioDiagnostics = item.AudioDiagnostics;
                AnalysisReport = item.AnalysisReport;
                ComparisonReport = item.ComparisonReport;
                SelectedAudioStream = item.SelectedAudioStream;
            }

            SetProcessingPhase("Phase_Error");
            SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_AnalysisFailed"]);
            _logService.Error(StatusText);

            if (result.Exception is not null)
            {
                _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
            }
        }

        RaiseCommandStates();
    }

    private async Task StartProcessingAsync()
    {
        if (SelectedPreset is null || SelectedExportFormat is null)
        {
            SetStatus("Status_SelectPresetAndFormat");
            return;
        }

        var processableItems = _batchQueueService.GetProcessableItems(BatchItems);
        if (processableItems.Count == 0)
        {
            SetStatus("Status_NoReadyFiles");
            return;
        }

        StopPreview();
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        OverallProgressValue = 0;
        SetProcessingPhase("Phase_Start");
        SetStatus("Status_Processing");
        _logService.Info(LocalizationService.Instance.Format("Log_BatchStartingFormat", processableItems.Count));

        try
        {
            for (var i = 0; i < processableItems.Count; i++)
            {
                var item = processableItems[i];
                if (_processingCancellation.IsCancellationRequested)
                {
                    break;
                }

                SelectedBatchItem = item;
                item.Status = BatchProcessingStatus.Processing;
                item.Progress = 0;
                item.ErrorMessage = string.Empty;
                ProgressValue = 0;
                OverallProgressValue = BatchQueueService.CalculateOverallProgress(i, processableItems.Count, 0);
                SetStatus("Status_BatchProcessingFormat", i + 1, processableItems.Count, item.FileName);

                var result = await _audioProcessingService.ProcessAsync(
                    BuildOptionsForItem(item),
                    new Progress<ProcessingProgress>(progress => UpdateProcessingProgress(item, i, processableItems.Count, progress)),
                    _processingCancellation.Token);

                if (result.IsSuccess && result.Value is not null)
                {
                    item.OutputPath = result.Value.OutputPath ?? string.Empty;
                    LastOutputPath = item.OutputPath;
                    var validationSucceeded = await ValidateProcessedItemAsync(item, _processingCancellation.Token);

                    if (_processingCancellation.IsCancellationRequested)
                    {
                        item.Status = BatchProcessingStatus.Cancelled;
                        item.ErrorMessage = LocalizationService.Instance["Error_ProcessingCancelled"];
                        SetProcessingPhase("Phase_Cancel");
                        SetStatus("Status_Cancelling");
                        _logService.Warning(item.ErrorMessage);
                        break;
                    }

                    if (!validationSucceeded)
                    {
                        item.Status = BatchProcessingStatus.Failed;
                        item.Progress = 0;
                        SetProcessingPhase("Phase_Error");
                        continue;
                    }

                    item.Progress = 100;
                    item.Status = BatchProcessingStatus.Done;
                    ComparisonReport = item.ComparisonReport;
                    _logService.Info(LocalizationService.Instance.Format("Log_BatchItemDoneFormat", item.FileName));
                    continue;
                }

                if (result.Value?.WasCancelled == true || _processingCancellation.IsCancellationRequested)
                {
                    item.Status = BatchProcessingStatus.Cancelled;
                    item.ErrorMessage = result.ErrorMessage ?? LocalizationService.Instance["Error_ProcessingCancelled"];
                    item.Progress = 0;
                    SetProcessingPhase("Phase_Cancel");
                    SetStatus("Status_Cancelling");
                    _logService.Warning(item.ErrorMessage);
                    break;
                }

                item.Status = BatchProcessingStatus.Failed;
                item.ErrorMessage = result.ErrorMessage ?? LocalizationService.Instance["Status_ProcessingFailed"];
                item.Progress = 0;
                SetProcessingPhase("Phase_Error");
                _logService.Error(LocalizationService.Instance.Format("Log_BatchItemFailedFormat", item.FileName, item.ErrorMessage));

                if (result.Exception is not null)
                {
                    _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
                }
            }

            OverallProgressValue = 100;
            ProgressValue = 100;
            SetProcessingPhase("Phase_Done");
            SetStatus("Status_BatchDoneFormat", CountStatus(BatchProcessingStatus.Done), CountStatus(BatchProcessingStatus.Failed), CountStatus(BatchProcessingStatus.Cancelled));

            if (SaveLogFile)
            {
                var logPath = await _logService.SaveAsync(OutputDirectory, "audio-quality-enhancer-batch", CancellationToken.None);
                _logService.Info(LocalizationService.Instance.Format("Log_LogSavedFormat", logPath));
            }

            if (SaveReportFile)
            {
                await SaveQualityReportAsync(CancellationToken.None);
            }
        }
        finally
        {
            IsBusy = false;
            _processingCancellation.Dispose();
            _processingCancellation = null;
            UpdateBatchSummary();
        }
    }

    private void CancelProcessing()
    {
        SetStatus("Status_Cancelling");
        SetProcessingPhase("Phase_Cancel");
        _processingCancellation?.Cancel();
        _diagnosticsCancellation?.Cancel();
    }

    private async Task<bool> ValidateProcessedItemAsync(BatchProcessingItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.OutputPath))
        {
            item.ErrorMessage = LocalizationService.Instance["Error_OutputFileMissingValidation"];
            return false;
        }

        SetProcessingPhase("Phase_ResultValidation");
        SetStatus("Status_ResultValidationRunning");
        _logService.Info(LocalizationService.Instance.Format("Log_ValidationQueueItemFormat", item.FileName));

        var result = await _audioValidationService.ValidateAsync(
            BuildOptionsForItem(item),
            item.OutputPath,
            item.AudioDiagnostics,
            cancellationToken);

        var report = result.Value;
        if (report is not null)
        {
            item.SetOutputInfo(report.OutputInfo);
            item.SetOutputDiagnostics(report.OutputDiagnostics);
            item.SetComparisonReport(report);

            if (ReferenceEquals(item, SelectedBatchItem))
            {
                ComparisonReport = item.ComparisonReport;
            }

            if (report.HasWarningsOrErrors)
            {
                item.ErrorMessage = report.StatusText;
                _logService.Warning(LocalizationService.Instance.Format("Log_ValidationWarningsFormat", item.FileName, report.StatusText));
            }
        }

        if (result.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            item.ErrorMessage = result.ErrorMessage ?? LocalizationService.Instance["Status_ResultValidationFailed"];
            _logService.Warning(LocalizationService.Instance.Format("Log_ValidationFailedFormat", item.FileName, item.ErrorMessage));
            return false;
        }

        return true;
    }

    private async Task SaveQualityReportAsync(CancellationToken cancellationToken)
    {
        if (SelectedPreset is null || SelectedExportFormat is null)
        {
            return;
        }

        var result = await _qualityReportService.SaveBatchReportAsync(
            OutputDirectory,
            BatchItems,
            SelectedPreset,
            SelectedExportFormat,
            cancellationToken);

        if (result.IsSuccess && result.Value is not null)
        {
            LastReportPath = result.Value;
            _logService.Info(LocalizationService.Instance.Format("Log_ReportSavedFormat", result.Value));
            return;
        }

        _logService.Warning(result.ErrorMessage ?? LocalizationService.Instance["Error_ReportSaveFailed"]);
    }

    private void RemoveSelectedFile()
    {
        var item = SelectedBatchItem;
        if (item is null || IsBusy)
        {
            return;
        }

        var index = BatchItems.IndexOf(item);
        RemoveBatchItem(item);

        if (BatchItems.Count == 0)
        {
            SelectedBatchItem = null;
        }
        else
        {
            SelectedBatchItem = BatchItems[Math.Clamp(index, 0, BatchItems.Count - 1)];
        }

        SetStatus("Status_BatchItemRemoved");
    }

    private void ClearFinishedFiles()
    {
        if (IsBusy)
        {
            return;
        }

        var items = _batchQueueService.GetFinishedItems(BatchItems);
        foreach (var item in items)
        {
            RemoveBatchItem(item);
        }

        if (SelectedBatchItem is null || !BatchItems.Contains(SelectedBatchItem))
        {
            SelectedBatchItem = BatchItems.FirstOrDefault();
        }

        SetStatus("Status_BatchFinishedCleared");
    }

    private async Task RetrySelectedFileAsync()
    {
        var item = SelectedBatchItem;
        if (item is null)
        {
            return;
        }

        var preparedCount = await PrepareRetryItemsAsync(new[] { item });
        if (preparedCount > 0)
        {
            SetStatus("Status_BatchItemRetryReady");
        }
    }

    private async Task RetryFailedFilesAsync()
    {
        var items = _batchQueueService.GetRetryableItems(BatchItems);
        if (items.Count == 0)
        {
            SetStatus("Status_BatchNoRetryableFiles");
            return;
        }

        var preparedCount = await PrepareRetryItemsAsync(items);
        SetStatus("Status_BatchRetryReadyFormat", preparedCount);
    }

    private async Task<int> PrepareRetryItemsAsync(IReadOnlyList<BatchProcessingItem> items)
    {
        var preparedItems = new List<BatchProcessingItem>();
        foreach (var item in items)
        {
            if (_batchQueueService.ResetForRetry(item))
            {
                preparedItems.Add(item);
            }
        }

        if (preparedItems.Count == 0)
        {
            return 0;
        }

        SelectedBatchItem = preparedItems[0];
        var needsAnalysis = preparedItems.Where(item => item.AudioInfo is null).ToArray();
        if (needsAnalysis.Length > 0)
        {
            IsBusy = true;
            try
            {
                foreach (var item in needsAnalysis)
                {
                    await AnalyzeBatchItemAsync(item, CancellationToken.None);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        BatchItemsView.Refresh();
        if (SelectedBatchItem is not null && !BatchItemsView.Contains(SelectedBatchItem))
        {
            SelectedBatchItem = BatchItemsView.Cast<BatchProcessingItem>().FirstOrDefault();
        }

        UpdateBatchSummary();
        RaiseCommandStates();
        return preparedItems.Count;
    }

    private void PlaySourcePreview() =>
        PlayPreview(InputPath, "Button_PlaySource");

    private void PlayOutputPreview() =>
        PlayPreview(LastOutputPath, "Button_PlayOutput");

    private void PlayPreview(string path, string labelKey)
    {
        StopPreviewTimer();
        var result = _audioPreviewService.Play(path);
        if (result.IsSuccess)
        {
            _activePreviewLabelKey = labelKey;
            IsPreviewActive = true;
            PreviewDurationSeconds = 0;
            PreviewPositionSeconds = 0;
            SetStatus("Status_PreviewPlayingFormat", LocalizationService.Instance[labelKey]);
            _logService.Info(LocalizationService.Instance.Format("Log_PreviewStartedFormat", path));
            StartPreviewTimer();
            return;
        }

        _activePreviewLabelKey = null;
        SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_PreviewFailed"]);
        _logService.Error(StatusText);

        if (result.Exception is not null)
        {
            _logService.Error($"{result.Exception.GetType().Name}: {result.Exception.Message}");
        }
    }

    private void StopPreview()
    {
        StopPreviewTimer();
        _audioPreviewService.Stop();
        IsPreviewActive = false;
        _activePreviewLabelKey = null;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        SetStatus("Status_PreviewStopped");
    }

    private void OpenOutputFolder()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            SetStatus("Status_OutputFolderMissing");
            return;
        }

        OpenPath(OutputDirectory, "Status_OutputFolderOpened");
    }

    private void OpenSelectedOutput()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            SetStatus("Status_SelectedOutputFileMissing");
            return;
        }

        OpenPath(outputPath, "Status_SelectedOutputFileOpened");
    }

    private void OpenSelectedOutputFolder()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        var directory = string.IsNullOrWhiteSpace(outputPath)
            ? null
            : Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            SetStatus("Status_SelectedOutputFolderMissing");
            return;
        }

        OpenPath(directory, "Status_SelectedOutputFolderOpened");
    }

    private void OpenLastOutput()
    {
        if (!File.Exists(LastOutputPath))
        {
            SetStatus("Status_OutputFileMissing");
            return;
        }

        OpenPath(LastOutputPath, "Status_OutputFileOpened");
    }

    private void OpenLastReport()
    {
        if (!File.Exists(LastReportPath))
        {
            SetStatus("Status_ReportFileMissing");
            return;
        }

        OpenPath(LastReportPath, "Status_ReportFileOpened");
    }

    private void CopyLog()
    {
        if (string.IsNullOrWhiteSpace(LogText))
        {
            SetStatus("Status_LogEmpty");
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(LogText);
            SetStatus("Status_LogCopied");
        }
        catch (Exception ex)
        {
            SetStatusRaw(LocalizationService.Instance.Format("Error_ClipboardFailedFormat", ex.Message));
        }
    }

    private void ClearLog()
    {
        if (IsBusy)
        {
            return;
        }

        _logService.Clear();
        SetStatus("Status_LogCleared");
    }

    private void OpenPath(string path, string successStatusKey)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            SetStatus(successStatusKey);
        }
        catch (Exception ex)
        {
            SetStatusRaw(LocalizationService.Instance.Format("Error_OpenPathFailedFormat", ex.Message));
        }
    }

    private void StartPreviewTimer()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += OnPreviewTimerTick;
        _previewTimer.Start();
    }

    private void StopPreviewTimer()
    {
        if (_previewTimer is null)
        {
            return;
        }

        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewTimerTick;
        _previewTimer = null;
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        if (_audioPreviewService.NaturalDuration.HasValue && PreviewDurationSeconds == 0)
        {
            PreviewDurationSeconds = _audioPreviewService.NaturalDuration.Value.TotalSeconds;
        }

        _updatingPositionFromTimer = true;
        PreviewPositionSeconds = _audioPreviewService.Position.TotalSeconds;
        _updatingPositionFromTimer = false;
    }

    private void UpdateProcessingProgress(BatchProcessingItem item, int itemIndex, int totalItems, ProcessingProgress progress)
    {
        ProgressValue = progress.Percentage;
        item.Progress = progress.Percentage;
        OverallProgressValue = BatchQueueService.CalculateOverallProgress(itemIndex, totalItems, progress.Percentage);
        SetProcessingPhaseRaw(string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Phase
            : $"{progress.Phase} - {progress.Detail}");
    }

    private bool CanStartProcessing()
    {
        return !IsBusy &&
               _batchQueueService.GetProcessableItems(BatchItems).Count > 0 &&
               !string.IsNullOrWhiteSpace(OutputDirectory) &&
               SelectedPreset is not null &&
               SelectedExportFormat is not null;
    }

    private bool CanAnalyzeDiagnostics()
    {
        return !IsBusy &&
               SelectedBatchItem?.AudioInfo is not null &&
               !string.IsNullOrWhiteSpace(SelectedBatchItem.SourcePath) &&
               File.Exists(SelectedBatchItem.SourcePath);
    }

    private bool CanRetrySelectedFile()
    {
        return !IsBusy && _batchQueueService.CanRetry(SelectedBatchItem);
    }

    private bool CanRetryFailedFiles()
    {
        return !IsBusy && _batchQueueService.GetRetryableItems(BatchItems).Count > 0;
    }

    private bool CanOpenSelectedOutput()
    {
        return !IsBusy && File.Exists(SelectedBatchItem?.OutputPath);
    }

    private bool CanOpenSelectedOutputFolder()
    {
        var outputPath = SelectedBatchItem?.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(outputPath);
        return !IsBusy && !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    private void ApplyPresetDefaults(AudioPreset preset)
    {
        if (preset.IsArchiveExport)
        {
            SelectedExportFormat = ExportFormat.Flac;
        }
        else if (preset.IsEverydayExport && SelectedExportFormat?.IsLossless == true)
        {
            SelectedExportFormat = ExportFormat.Aac_256;
        }
    }

    private void UpdateQualityNotice()
    {
        var parts = new List<string>
        {
            LocalizationService.Instance["Quality_GeneralNote"]
        };

        if (AudioInfo?.IsLikelyLossy == true)
        {
            parts.Add(AudioInfo.LossyWarning);
        }

        if (!string.IsNullOrWhiteSpace(AnalysisWarningText))
        {
            parts.Add(AnalysisWarningText);
        }

        if (!string.IsNullOrWhiteSpace(SelectedPreset?.QualityNote))
        {
            parts.Add(SelectedPreset.QualityNote);
        }

        if (SelectedExportFormat?.IsLossless == true)
        {
            parts.Add(LocalizationService.Instance["Quality_LosslessTarget"]);
        }

        QualityNotice = string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private void UpdateAnalysisWarnings()
    {
        var warnings = new List<string>();

        if (AudioInfo?.IsLikelyLossy == true && AudioInfo.BitRate is > 0)
        {
            var lowBitrateThreshold = AudioInfo.Channels == 1 ? 96_000 : 128_000;
            if (AudioInfo.BitRate.Value < lowBitrateThreshold)
            {
                warnings.Add(LocalizationService.Instance.Format("Warning_LowBitrateFormat", AudioInfo.BitRateDisplay));
            }
        }

        if (AudioInfo?.SampleRate is > 0 and < 32000)
        {
            warnings.Add(LocalizationService.Instance.Format("Warning_LowSampleRateFormat", AudioInfo.SampleRateDisplay));
        }

        var diagnostics = AudioDiagnostics;
        if (diagnostics is not null)
        {
            if (diagnostics.HasPotentialClipping)
            {
                warnings.Add(LocalizationService.Instance["Warning_PotentialClipping"]);
            }
            else if ((diagnostics.TruePeakDb ?? diagnostics.MaxVolumeDb) is >= -1.0)
            {
                warnings.Add(LocalizationService.Instance["Warning_LowHeadroom"]);
            }

            if (diagnostics.IntegratedLoudnessLufs is < -28)
            {
                warnings.Add(LocalizationService.Instance["Warning_VeryQuiet"]);
            }
            else if (diagnostics.IntegratedLoudnessLufs is > -9)
            {
                warnings.Add(LocalizationService.Instance["Warning_AlreadyLoud"]);
            }
        }

        AnalysisWarningText = string.Join(Environment.NewLine + Environment.NewLine, warnings);
        HasAnalysisWarnings = warnings.Count > 0;
    }

    private void UpdateAnalysisReport()
    {
        AnalysisReport = AudioInfo is null
            ? null
            : _audioAnalysisInsightService.BuildReport(AudioInfo, AudioDiagnostics);
    }

    private void UpdateProfileAdvice()
    {
        ProfileAdvice = _audioProfileAdvisorService.BuildAdvice(AudioInfo, AudioDiagnostics);
    }

    private void ApplyProfileSuggestion(AudioProfileSuggestion? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        SelectedPreset = suggestion.Preset;
        if (suggestion.ExportFormat is not null)
        {
            SelectedExportFormat = suggestion.ExportFormat;
        }

        SetStatus("Status_ProfileAdviceAppliedFormat", suggestion.Title);
    }

    private void UpdateFilterDetails()
    {
        if (SelectedPreset is null || SelectedExportFormat is null)
        {
            FilterDetailsText = string.Empty;
            return;
        }

        FilterDetailsText = AudioProcessingService.BuildFilterPreview(BuildOptionsForItem(SelectedBatchItem));
    }

    private ProcessingOptions BuildOptionsForItem(BatchProcessingItem? item)
    {
        return new ProcessingOptions
        {
            InputPath = item?.SourcePath ?? InputPath,
            OutputDirectory = OutputDirectory,
            Preset = SelectedPreset ?? AudioPreset.Music,
            ExportFormat = SelectedExportFormat ?? ExportFormat.Flac,
            SourceInfo = item?.AudioInfo ?? AudioInfo,
            AudioStream = item?.SelectedAudioStream ?? SelectedAudioStream,
            NoiseReductionFloor = NoiseReductionFloor,
            EnableSpeechCompression = EnableSpeechCompression,
            EnableSpeechPresenceBoost = EnableSpeechPresenceBoost,
            UseTwoPassLoudness = UseTwoPassLoudness
        };
    }

    private void SelectAudioStreamForCurrentItem(AudioStreamInfo? audioStream)
    {
        var item = SelectedBatchItem;
        if (item?.AudioInfo is null || audioStream is null)
        {
            return;
        }

        item.SelectAudioStream(audioStream);
        if (item.AudioInfo is not null)
        {
            item.SetAnalysisReport(_audioAnalysisInsightService.BuildReport(item.AudioInfo, diagnostics: null));
        }

        AudioInfo = item.AudioInfo;
        AudioDiagnostics = item.AudioDiagnostics;
        AnalysisReport = item.AnalysisReport;
        ComparisonReport = item.ComparisonReport;
        UpdateFilterDetails();
        SetStatus("Status_AudioStreamSelectedFormat", audioStream.DisplayName);
    }

    private void AddBatchItem(BatchProcessingItem item)
    {
        item.PropertyChanged += OnBatchItemPropertyChanged;
        BatchItems.Add(item);
    }

    private void RemoveBatchItem(BatchProcessingItem item)
    {
        item.PropertyChanged -= OnBatchItemPropertyChanged;
        BatchItems.Remove(item);
        if (ReferenceEquals(SelectedBatchItem, item))
        {
            SelectedBatchItem = null;
        }

        item.Dispose();
    }

    private void OnBatchItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasBatchItems));
        BatchItemsView.Refresh();
        UpdateBatchSummary();
        RaiseCommandStates();
    }

    private void OnBatchItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (BatchViewNeedsRefresh(e.PropertyName))
        {
            BatchItemsView.Refresh();
        }

        UpdateBatchSummary();
        RaiseCommandStates();

        if (ReferenceEquals(sender, SelectedBatchItem))
        {
            OnPropertyChanged(nameof(SelectedBatchItem));
        }
    }

    private bool FilterBatchItem(object item)
    {
        return item is BatchProcessingItem batchItem &&
               _batchQueueService.MatchesFilter(batchItem, SelectedBatchFilter.Filter);
    }

    private static bool BatchViewNeedsRefresh(string? propertyName)
    {
        return propertyName is null or
            nameof(BatchProcessingItem.Status) or
            nameof(BatchProcessingItem.HasComparisonWarnings) or
            nameof(BatchProcessingItem.ComparisonReport);
    }

    private void SyncSelectedBatchItem()
    {
        var item = SelectedBatchItem;
        _syncingSelectedBatchItem = true;
        try
        {
            InputPath = item?.SourcePath ?? string.Empty;
            AudioInfo = item?.AudioInfo;
            AudioDiagnostics = item?.AudioDiagnostics;
            AnalysisReport = item?.AnalysisReport;
            ComparisonReport = item?.ComparisonReport;
            SelectedAudioStream = item?.SelectedAudioStream;
            ProgressValue = item?.Progress ?? 0;
        }
        finally
        {
            _syncingSelectedBatchItem = false;
        }

        if (item is not null && File.Exists(item.OutputPath))
        {
            LastOutputPath = item.OutputPath;
        }
    }

    private void UpdateBatchSummary()
    {
        var summary = _batchQueueService.BuildSummary(BatchItems);
        BatchSummaryText = summary.HasItems
            ? LocalizationService.Instance.Format(
                "BatchSummary_Format",
                summary.Total,
                summary.Ready,
                summary.Processing,
                summary.Done,
                summary.DoneWithWarnings,
                summary.Failed,
                summary.Cancelled)
            : LocalizationService.Instance["BatchSummary_Empty"];
    }

    private int CountStatus(BatchProcessingStatus status)
    {
        return BatchItems.Count(item => item.Status == status);
    }

    private void OnLogAdded(object? sender, string line)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            LogText = _logService.CurrentText;
        }
        else
        {
            dispatcher.Invoke(() => LogText = _logService.CurrentText);
        }
    }

    private void OnPlaybackFailed(object? sender, string errorMessage)
    {
        StopPreviewTimer();
        IsPreviewActive = false;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        _activePreviewLabelKey = null;
        SetStatusRaw(errorMessage);
        _logService.Error(errorMessage);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        StopPreviewTimer();
        IsPreviewActive = false;
        _activePreviewLabelKey = null;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        SetStatus("Status_PreviewEnded");
        RaiseCommandStates();
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
        _stopPreviewCommand.RaiseCanExecuteChanged();
        _openOutputFolderCommand.RaiseCanExecuteChanged();
        _openSelectedOutputCommand.RaiseCanExecuteChanged();
        _openSelectedOutputFolderCommand.RaiseCanExecuteChanged();
        _openLastOutputCommand.RaiseCanExecuteChanged();
        _openLastReportCommand.RaiseCanExecuteChanged();
        _copyLogCommand.RaiseCanExecuteChanged();
        _clearLogCommand.RaiseCanExecuteChanged();
        _applyProfileSuggestionCommand.RaiseCanExecuteChanged();
    }

    private static string GetDefaultOutputDirectory()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Directory.Exists(music) ? music : Environment.CurrentDirectory;
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
        _audioPreviewService.PlaybackFailed -= OnPlaybackFailed;
        _audioPreviewService.PlaybackEnded -= OnPlaybackEnded;
        BatchItems.CollectionChanged -= OnBatchItemsChanged;
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        StopPreviewTimer();
        _processingCancellation?.Dispose();
        _diagnosticsCancellation?.Dispose();
        _audioPreviewService.Dispose();
        foreach (var item in BatchItems)
        {
            item.PropertyChanged -= OnBatchItemPropertyChanged;
            item.Dispose();
        }
    }
}
