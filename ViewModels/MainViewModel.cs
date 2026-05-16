using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly AudioProcessingService _audioProcessingService;
    private readonly AsyncRelayCommand _selectFileCommand;
    private readonly RelayCommand _selectOutputFolderCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _playSourceCommand;
    private readonly RelayCommand _playOutputCommand;
    private readonly RelayCommand _stopPreviewCommand;

    private string _inputPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private AudioInfo? _audioInfo;
    private AudioPreset? _selectedPreset;
    private ExportFormat? _selectedExportFormat;
    private string _statusText = "Bereit.";
    private string _qualityNotice = string.Empty;
    private string _logText = string.Empty;
    private string _toolStatusText = "Werkzeuge werden geprüft...";
    private string _processingPhaseText = "Bereit";
    private string _filterDetailsText = string.Empty;
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

    private DispatcherTimer? _previewTimer;
    private bool _updatingPositionFromTimer;
    private double _previewPositionSeconds;
    private double _previewDurationSeconds;
    private bool _isPreviewActive;

    public MainViewModel()
    {
        _fileNameService = new FileNameService();
        _logService = new LogService();
        _toolDiscoveryService = new ToolDiscoveryService();
        _audioPreviewService = new AudioPreviewService();
        _ffmpegService = new FFmpegService(_toolDiscoveryService);
        _ffprobeService = new FFprobeService(_fileNameService, _toolDiscoveryService);
        _audioProcessingService = new AudioProcessingService(_ffmpegService, _ffprobeService, _fileNameService, _logService);

        _logService.LogAdded += OnLogAdded;
        _audioPreviewService.PlaybackFailed += OnPlaybackFailed;
        _audioPreviewService.PlaybackEnded += OnPlaybackEnded;

        Presets = new ObservableCollection<AudioPreset>(AudioPreset.All);
        ExportFormats = new ObservableCollection<ExportFormat>(ExportFormat.All);

        _selectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsBusy);
        _selectOutputFolderCommand = new RelayCommand(SelectOutputFolder, () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessing);
        _cancelCommand = new RelayCommand(CancelProcessing, () => IsBusy);
        _playSourceCommand = new RelayCommand(() => PlayPreview(InputPath, "Original"), () => !IsBusy && File.Exists(InputPath));
        _playOutputCommand = new RelayCommand(() => PlayPreview(LastOutputPath, "Ergebnis"), () => !IsBusy && File.Exists(LastOutputPath));
        _stopPreviewCommand = new RelayCommand(StopPreview);

        SelectedPreset = AudioPreset.Music;
        SelectedExportFormat = ExportFormat.Flac;
        OutputDirectory = GetDefaultOutputDirectory();
        UpdateQualityNotice();
        UpdateFilterDetails();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioPreset> Presets { get; }

    public ObservableCollection<ExportFormat> ExportFormats { get; }

    public ICommand SelectFileCommand => _selectFileCommand;

    public ICommand SelectOutputFolderCommand => _selectOutputFolderCommand;

    public ICommand StartCommand => _startCommand;

    public ICommand CancelCommand => _cancelCommand;

    public ICommand PlaySourceCommand => _playSourceCommand;

    public ICommand PlayOutputCommand => _playOutputCommand;

    public ICommand StopPreviewCommand => _stopPreviewCommand;

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
                UpdateQualityNotice();
                UpdateFilterDetails();
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

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
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
        _logService.Info("Prüfe FFmpeg und FFprobe...");

        var ffmpeg = await _toolDiscoveryService.GetStatusAsync("ffmpeg", CancellationToken.None);
        var ffprobe = await _toolDiscoveryService.GetStatusAsync("ffprobe", CancellationToken.None);

        ToolStatusText = $"{ffmpeg.DisplayText} | {ffprobe.DisplayText}";

        if (ffmpeg.IsAvailable)
        {
            _logService.Info(ffmpeg.VersionLine ?? "FFmpeg wurde gefunden.");
        }
        else
        {
            _logService.Warning(ffmpeg.ErrorMessage ?? "FFmpeg ist nicht verfügbar.");
        }

        if (ffprobe.IsAvailable)
        {
            _logService.Info(ffprobe.VersionLine ?? "FFprobe wurde gefunden.");
        }
        else
        {
            _logService.Warning(ffprobe.ErrorMessage ?? "FFprobe ist nicht verfügbar.");
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
            Title = "Audio- oder Videodatei auswählen",
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
            Description = "Ausgabeordner auswählen",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputDirectory) ? OutputDirectory : GetDefaultOutputDirectory()
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    private async Task AnalyzeSelectedFileAsync()
    {
        _logService.Clear();
        AudioInfo = null;
        ProgressValue = 0;
        ProcessingPhaseText = "Analyse";
        StatusText = "Analysiere Datei...";

        var result = await _ffprobeService.AnalyzeAsync(InputPath, _logService.Info, CancellationToken.None);
        if (result.IsSuccess && result.Value is not null)
        {
            AudioInfo = result.Value;
            ProcessingPhaseText = "Bereit";
            StatusText = "Analyse abgeschlossen.";
            _logService.Info($"Codec: {AudioInfo.CodecDisplay}");
            _logService.Info($"Container: {AudioInfo.Container}");

            if (AudioInfo.IsLikelyLossy)
            {
                _logService.Warning(AudioInfo.LossyWarning);
            }
        }
        else
        {
            ProcessingPhaseText = "Fehler";
            StatusText = result.ErrorMessage ?? "Analyse fehlgeschlagen.";
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
            StatusText = "Bitte Preset und Ausgabeformat wählen.";
            return;
        }

        StopPreview();
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        ProcessingPhaseText = "Start";
        StatusText = "Verarbeitung läuft...";
        _logService.Info("Starte Verarbeitung.");

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
                ProcessingPhaseText = "Fertig";
                LastOutputPath = result.Value.OutputPath ?? string.Empty;
                StatusText = $"Fertig: {result.Value.OutputPath}";

                if (SaveLogFile)
                {
                    var prefix = Path.GetFileNameWithoutExtension(result.Value.OutputPath ?? "audio-quality-enhancer");
                    var logPath = await _logService.SaveAsync(OutputDirectory, prefix, CancellationToken.None);
                    _logService.Info($"Logdatei gespeichert: {logPath}");
                }
            }
            else
            {
                ProcessingPhaseText = "Fehler";
                StatusText = result.ErrorMessage ?? "Verarbeitung fehlgeschlagen.";
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
        StatusText = "Abbruch wird angefordert...";
        ProcessingPhaseText = "Abbruch";
        _processingCancellation?.Cancel();
    }

    private void PlayPreview(string path, string label)
    {
        StopPreviewTimer();
        var result = _audioPreviewService.Play(path);
        if (result.IsSuccess)
        {
            IsPreviewActive = true;
            PreviewDurationSeconds = 0;
            PreviewPositionSeconds = 0;
            StatusText = $"Vorschau läuft: {label}";
            _logService.Info($"Vorschau gestartet: {path}");
            StartPreviewTimer();
            return;
        }

        StatusText = result.ErrorMessage ?? "Vorschau fehlgeschlagen.";
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
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        StatusText = "Vorschau gestoppt.";
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
        ProcessingPhaseText = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Phase
            : $"{progress.Phase} - {progress.Detail}";
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
            "Dieses Tool kann Audio verbessern, normalisieren und restaurieren, aber keine Informationen zurückholen, die durch schlechte Aufnahme oder verlustbehaftete Kompression bereits zerstört wurden."
        };

        if (AudioInfo?.IsLikelyLossy == true)
        {
            parts.Add(AudioInfo.LossyWarning);
        }

        if (!string.IsNullOrWhiteSpace(SelectedPreset?.QualityNote))
        {
            parts.Add(SelectedPreset.QualityNote);
        }

        if (SelectedExportFormat?.IsLossless == true)
        {
            parts.Add("Ein verlustfreies Zielformat vermeidet zusätzliche Exportverluste, macht eine verlustbehaftete Quelle aber nicht besser als sie ist.");
        }

        QualityNotice = string.Join(Environment.NewLine + Environment.NewLine, parts);
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
        StatusText = errorMessage;
        _logService.Error(errorMessage);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        StopPreviewTimer();
        IsPreviewActive = false;
        PreviewPositionSeconds = 0;
        PreviewDurationSeconds = 0;
        StatusText = "Vorschau beendet.";
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        _selectFileCommand.RaiseCanExecuteChanged();
        _selectOutputFolderCommand.RaiseCanExecuteChanged();
        _startCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
        _playSourceCommand.RaiseCanExecuteChanged();
        _playOutputCommand.RaiseCanExecuteChanged();
        _stopPreviewCommand.RaiseCanExecuteChanged();
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
        StopPreviewTimer();
        _processingCancellation?.Dispose();
        _audioPreviewService.Dispose();
    }
}
