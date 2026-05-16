using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    private readonly AudioProcessingService _audioProcessingService;
    private readonly SettingsService _settingsService;
    private readonly AsyncRelayCommand _selectFileCommand;
    private readonly AsyncRelayCommand _analyzeDiagnosticsCommand;
    private readonly RelayCommand _selectOutputFolderCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _playSourceCommand;
    private readonly RelayCommand _playOutputCommand;
    private readonly RelayCommand _stopPreviewCommand;
    private readonly RelayCommand _openOutputFolderCommand;
    private readonly RelayCommand _openLastOutputCommand;
    private readonly RelayCommand _copyLogCommand;
    private readonly RelayCommand _clearLogCommand;

    private ToolStatus? _ffmpegStatus;
    private ToolStatus? _ffprobeStatus;
    private string _inputPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private AudioInfo? _audioInfo;
    private AudioDiagnostics? _audioDiagnostics;
    private AudioPreset? _selectedPreset;
    private ExportFormat? _selectedExportFormat;
    private string _statusText;
    private string _qualityNotice = string.Empty;
    private string _analysisWarningText = string.Empty;
    private string _logText = string.Empty;
    private string _toolStatusText;
    private string _processingPhaseText;
    private string _filterDetailsText = string.Empty;
    private LanguageOption _selectedLanguage = LanguageOption.German;
    private string _lastOutputPath = string.Empty;
    private double _progressValue;
    private bool _isBusy;
    private bool _saveLogFile = true;
    private bool _enableSpeechCompression;
    private bool _enableSpeechPresenceBoost = true;
    private bool _useTwoPassLoudness = true;
    private int _noiseReductionFloor = -25;
    private bool _initialized;
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
        _audioProcessingService = new AudioProcessingService(_ffmpegService, _ffprobeService, _fileNameService, _logService);
        _settingsService = App.SettingsService;

        _statusText = LocalizationService.Instance["Status_Ready"];
        _toolStatusText = LocalizationService.Instance["Tools_Checking"];
        _processingPhaseText = LocalizationService.Instance["Phase_Ready"];

        _logService.LogAdded += OnLogAdded;
        _audioPreviewService.PlaybackFailed += OnPlaybackFailed;
        _audioPreviewService.PlaybackEnded += OnPlaybackEnded;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;

        Presets = new ObservableCollection<AudioPreset>(AudioPreset.All);
        ExportFormats = new ObservableCollection<ExportFormat>(ExportFormat.All);
        Languages = new ObservableCollection<LanguageOption>(LanguageOption.All);

        _selectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsBusy);
        _analyzeDiagnosticsCommand = new AsyncRelayCommand(AnalyzeDiagnosticsAsync, CanAnalyzeDiagnostics);
        _selectOutputFolderCommand = new RelayCommand(SelectOutputFolder, () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessing);
        _cancelCommand = new RelayCommand(CancelProcessing, () => IsBusy);
        _playSourceCommand = new RelayCommand(PlaySourcePreview, () => !IsBusy && File.Exists(InputPath));
        _playOutputCommand = new RelayCommand(PlayOutputPreview, () => !IsBusy && File.Exists(LastOutputPath));
        _stopPreviewCommand = new RelayCommand(StopPreview);
        _openOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => Directory.Exists(OutputDirectory));
        _openLastOutputCommand = new RelayCommand(OpenLastOutput, () => File.Exists(LastOutputPath));
        _copyLogCommand = new RelayCommand(CopyLog, () => !string.IsNullOrWhiteSpace(LogText));
        _clearLogCommand = new RelayCommand(ClearLog, () => !IsBusy && !string.IsNullOrWhiteSpace(LogText));

        ApplySettings(_settingsService.Load());
        UpdateQualityNotice();
        UpdateFilterDetails();
    }

    private void ApplySettings(AppSettings settings)
    {
        SelectedLanguage = LanguageOption.All.FirstOrDefault(l => l.Code == settings.Language) ?? LanguageOption.German;
        SelectedPreset = AudioPreset.All.FirstOrDefault(p => p.Id == settings.PresetId) ?? AudioPreset.Music;
        SelectedExportFormat = ExportFormat.All.FirstOrDefault(f => f.Id == settings.ExportFormatId) ?? ExportFormat.Flac;
        OutputDirectory = !string.IsNullOrWhiteSpace(settings.OutputDirectory) && Directory.Exists(settings.OutputDirectory)
            ? settings.OutputDirectory
            : GetDefaultOutputDirectory();
        SaveLogFile = settings.SaveLogFile;
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
            PresetId = SelectedPreset?.Id ?? AudioPreset.Music.Id,
            ExportFormatId = SelectedExportFormat?.Id ?? ExportFormat.Flac.Id,
            OutputDirectory = OutputDirectory,
            SaveLogFile = SaveLogFile,
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

    public ICommand SelectFileCommand => _selectFileCommand;

    public ICommand AnalyzeDiagnosticsCommand => _analyzeDiagnosticsCommand;

    public ICommand SelectOutputFolderCommand => _selectOutputFolderCommand;

    public ICommand StartCommand => _startCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand PlaySourceCommand => _playSourceCommand;

    public ICommand PlayOutputCommand => _playOutputCommand;

    public ICommand StopPreviewCommand => _stopPreviewCommand;

    public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;

    public ICommand OpenLastOutputCommand => _openLastOutputCommand;

    public ICommand CopyLogCommand => _copyLogCommand;

    public ICommand ClearLogCommand => _clearLogCommand;

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
            var old = _audioInfo;
            if (SetProperty(ref _audioInfo, value))
            {
                old?.Dispose();
                UpdateAnalysisWarnings();
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
            var old = _audioDiagnostics;
            if (SetProperty(ref _audioDiagnostics, value))
            {
                old?.Dispose();
                UpdateAnalysisWarnings();
                UpdateQualityNotice();
            }
        }
    }

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

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
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
        if (IsBusy)
        {
            return;
        }

        StopPreview();
        InputPath = path;
        LastOutputPath = string.Empty;
        AudioDiagnostics = null;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            OutputDirectory = directory;
        }

        await AnalyzeSelectedFileAsync();
    }

    private async Task SelectFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance["Dialog_SelectFile_Title"],
            Filter = _fileNameService.BuildOpenDialogFilter(),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadInputFileAsync(dialog.FileName);
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
        if (AudioInfo is null || string.IsNullOrWhiteSpace(InputPath) || !File.Exists(InputPath))
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
                InputPath,
                AudioInfo.Duration,
                _logService.Info,
                value => ProgressValue = value,
                _diagnosticsCancellation.Token);

            if (result.IsSuccess && result.Value is not null)
            {
                AudioDiagnostics = result.Value;
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

    private async Task AnalyzeSelectedFileAsync()
    {
        _logService.Clear();
        AudioInfo = null;
        ProgressValue = 0;
        SetProcessingPhase("Phase_Analysis");
        SetStatus("Status_Analyzing");

        var result = await _ffprobeService.AnalyzeAsync(InputPath, _logService.Info, CancellationToken.None);
        if (result.IsSuccess && result.Value is not null)
        {
            AudioInfo = result.Value;
            SetProcessingPhase("Phase_Ready");
            SetStatus("Status_AnalysisDone");
            _logService.Info(LocalizationService.Instance.Format("Log_CodecFormat", AudioInfo.CodecDisplay));
            _logService.Info(LocalizationService.Instance.Format("Log_ContainerFormat", AudioInfo.ContainerDisplay));

            if (AudioInfo.IsLikelyLossy)
            {
                _logService.Warning(AudioInfo.LossyWarning);
            }
        }
        else
        {
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

        StopPreview();
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        SetProcessingPhase("Phase_Start");
        SetStatus("Status_Processing");
        _logService.Info(LocalizationService.Instance["Log_StartingProcessing"]);

        try
        {
            var options = BuildCurrentOptions();
            var result = await _audioProcessingService.ProcessAsync(
                options,
                new Progress<ProcessingProgress>(UpdateProcessingProgress),
                _processingCancellation.Token);

            if (result.IsSuccess && result.Value is not null)
            {
                ProgressValue = 100;
                SetProcessingPhase("Phase_Done");
                LastOutputPath = result.Value.OutputPath ?? string.Empty;
                SetStatus("Status_DoneFormat", result.Value.OutputPath ?? string.Empty);

                if (SaveLogFile)
                {
                    var prefix = Path.GetFileNameWithoutExtension(result.Value.OutputPath ?? "audio-quality-enhancer");
                    var logPath = await _logService.SaveAsync(OutputDirectory, prefix, CancellationToken.None);
                    _logService.Info(LocalizationService.Instance.Format("Log_LogSavedFormat", logPath));
                }
            }
            else
            {
                SetProcessingPhase("Phase_Error");
                SetStatusRaw(result.ErrorMessage ?? LocalizationService.Instance["Status_ProcessingFailed"]);
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
            _processingCancellation.Dispose();
            _processingCancellation = null;
        }
    }

    private void CancelProcessing()
    {
        SetStatus("Status_Cancelling");
        SetProcessingPhase("Phase_Cancel");
        _processingCancellation?.Cancel();
        _diagnosticsCancellation?.Cancel();
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

    private void OpenLastOutput()
    {
        if (!File.Exists(LastOutputPath))
        {
            SetStatus("Status_OutputFileMissing");
            return;
        }

        OpenPath(LastOutputPath, "Status_OutputFileOpened");
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

    private void UpdateProcessingProgress(ProcessingProgress progress)
    {
        ProgressValue = progress.Percentage;
        SetProcessingPhaseRaw(string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Phase
            : $"{progress.Phase} - {progress.Detail}");
    }

    private bool CanStartProcessing()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(InputPath) &&
               File.Exists(InputPath) &&
               !string.IsNullOrWhiteSpace(OutputDirectory) &&
               SelectedPreset is not null &&
               SelectedExportFormat is not null;
    }

    private bool CanAnalyzeDiagnostics()
    {
        return !IsBusy &&
               AudioInfo is not null &&
               !string.IsNullOrWhiteSpace(InputPath) &&
               File.Exists(InputPath);
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

    private void UpdateFilterDetails()
    {
        if (SelectedPreset is null || SelectedExportFormat is null)
        {
            FilterDetailsText = string.Empty;
            return;
        }

        FilterDetailsText = AudioProcessingService.BuildFilterPreview(BuildCurrentOptions());
    }

    private ProcessingOptions BuildCurrentOptions()
    {
        return new ProcessingOptions
        {
            InputPath = InputPath,
            OutputDirectory = OutputDirectory,
            Preset = SelectedPreset ?? AudioPreset.Music,
            ExportFormat = SelectedExportFormat ?? ExportFormat.Flac,
            SourceInfo = AudioInfo,
            NoiseReductionFloor = NoiseReductionFloor,
            EnableSpeechCompression = EnableSpeechCompression,
            EnableSpeechPresenceBoost = EnableSpeechPresenceBoost,
            UseTwoPassLoudness = UseTwoPassLoudness
        };
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
        UpdateAnalysisWarnings();
        UpdateQualityNotice();
        UpdateFilterDetails();
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
        _playSourceCommand.RaiseCanExecuteChanged();
        _playOutputCommand.RaiseCanExecuteChanged();
        _stopPreviewCommand.RaiseCanExecuteChanged();
        _openOutputFolderCommand.RaiseCanExecuteChanged();
        _openLastOutputCommand.RaiseCanExecuteChanged();
        _copyLogCommand.RaiseCanExecuteChanged();
        _clearLogCommand.RaiseCanExecuteChanged();
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
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;
        StopPreviewTimer();
        _processingCancellation?.Dispose();
        _diagnosticsCancellation?.Dispose();
        _audioPreviewService.Dispose();
        _audioInfo?.Dispose();
        _audioDiagnostics?.Dispose();
    }
}
