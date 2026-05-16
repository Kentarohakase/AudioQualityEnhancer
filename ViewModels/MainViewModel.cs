using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;
using Forms = System.Windows.Forms;

namespace AudioQualityEnhancer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FileNameService _fileNameService;
    private readonly LogService _logService;
    private readonly FFmpegService _ffmpegService;
    private readonly FFprobeService _ffprobeService;
    private readonly AudioProcessingService _audioProcessingService;
    private readonly AsyncRelayCommand _selectFileCommand;
    private readonly RelayCommand _selectOutputFolderCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly RelayCommand _cancelCommand;

    private string _inputPath = string.Empty;
    private string _outputDirectory = string.Empty;
    private AudioInfo? _audioInfo;
    private AudioPreset? _selectedPreset;
    private ExportFormat? _selectedExportFormat;
    private string _statusText = "Bereit.";
    private string _qualityNotice = string.Empty;
    private string _logText = string.Empty;
    private double _progressValue;
    private bool _isBusy;
    private bool _saveLogFile = true;
    private bool _enableSpeechCompression;
    private bool _enableSpeechPresenceBoost = true;
    private int _noiseReductionFloor = -25;
    private bool _initialized;
    private CancellationTokenSource? _processingCancellation;

    public MainViewModel()
    {
        _fileNameService = new FileNameService();
        _logService = new LogService();
        _ffmpegService = new FFmpegService();
        _ffprobeService = new FFprobeService(_fileNameService);
        _audioProcessingService = new AudioProcessingService(_ffmpegService, _ffprobeService, _fileNameService, _logService);

        _logService.LogAdded += OnLogAdded;

        Presets = new ObservableCollection<AudioPreset>(AudioPreset.All);
        ExportFormats = new ObservableCollection<ExportFormat>(ExportFormat.All);

        _selectFileCommand = new AsyncRelayCommand(SelectFileAsync, () => !IsBusy);
        _selectOutputFolderCommand = new RelayCommand(SelectOutputFolder, () => !IsBusy);
        _startCommand = new AsyncRelayCommand(StartProcessingAsync, CanStartProcessing);
        _cancelCommand = new RelayCommand(CancelProcessing, () => IsBusy);

        SelectedPreset = AudioPreset.Music;
        SelectedExportFormat = ExportFormat.Flac;
        OutputDirectory = GetDefaultOutputDirectory();
        UpdateQualityNotice();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioPreset> Presets { get; }

    public ObservableCollection<ExportFormat> ExportFormats { get; }

    public ICommand SelectFileCommand => _selectFileCommand;

    public ICommand SelectOutputFolderCommand => _selectOutputFolderCommand;

    public ICommand StartCommand => _startCommand;

    public ICommand CancelCommand => _cancelCommand;

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
                UpdateQualityNotice();
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
        set => SetProperty(ref _enableSpeechCompression, value);
    }

    public bool EnableSpeechPresenceBoost
    {
        get => _enableSpeechPresenceBoost;
        set => SetProperty(ref _enableSpeechPresenceBoost, value);
    }

    public int NoiseReductionFloor
    {
        get => _noiseReductionFloor;
        set => SetProperty(ref _noiseReductionFloor, value);
    }

    public bool IsSpeechPreset => SelectedPreset?.Id == AudioPreset.Speech.Id;

    public bool IsNoisePreset => SelectedPreset?.Id == AudioPreset.NoiseReduction.Id;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _logService.Info("Prüfe FFmpeg und FFprobe...");

        var ffmpeg = await _ffmpegService.CheckAvailabilityAsync(CancellationToken.None);
        if (ffmpeg.IsSuccess)
        {
            _logService.Info("FFmpeg wurde gefunden.");
        }
        else
        {
            _logService.Warning(ffmpeg.ErrorMessage ?? "FFmpeg ist nicht verfügbar.");
        }

        var ffprobe = await _ffprobeService.CheckAvailabilityAsync(CancellationToken.None);
        if (ffprobe.IsSuccess)
        {
            _logService.Info("FFprobe wurde gefunden.");
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

        InputPath = path;

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
        StatusText = "Analysiere Datei...";

        var result = await _ffprobeService.AnalyzeAsync(InputPath, _logService.Info, CancellationToken.None);
        if (result.IsSuccess && result.Value is not null)
        {
            AudioInfo = result.Value;
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
            StatusText = result.ErrorMessage ?? "Analyse fehlgeschlagen.";
            _logService.Error(StatusText);
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

        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        IsBusy = true;
        ProgressValue = 0;
        StatusText = "Verarbeitung läuft...";
        _logService.Info("Starte Verarbeitung.");

        try
        {
            var options = new ProcessingOptions
            {
                InputPath = InputPath,
                OutputDirectory = OutputDirectory,
                Preset = SelectedPreset,
                ExportFormat = SelectedExportFormat,
                SourceInfo = AudioInfo,
                NoiseReductionFloor = NoiseReductionFloor,
                EnableSpeechCompression = EnableSpeechCompression,
                EnableSpeechPresenceBoost = EnableSpeechPresenceBoost
            };

            var result = await _audioProcessingService.ProcessAsync(
                options,
                new Progress<double>(value => ProgressValue = value),
                _processingCancellation.Token);

            if (result.IsSuccess && result.Value is not null)
            {
                ProgressValue = 100;
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
                StatusText = result.ErrorMessage ?? "Verarbeitung fehlgeschlagen.";
                _logService.Error(StatusText);
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
        _processingCancellation?.Cancel();
    }

    private bool CanStartProcessing()
    {
        return !IsBusy &&
               !string.IsNullOrWhiteSpace(InputPath) &&
               File.Exists(InputPath) &&
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

    private void RaiseCommandStates()
    {
        _selectFileCommand.RaiseCanExecuteChanged();
        _selectOutputFolderCommand.RaiseCanExecuteChanged();
        _startCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
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
}
