using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class BatchProcessingItem : INotifyPropertyChanged, IDisposable
{
    private BatchProcessingStatus _status = BatchProcessingStatus.Pending;
    private AudioInfo? _audioInfo;
    private AudioDiagnostics? _audioDiagnostics;
    private AudioAnalysisReport? _analysisReport;
    private AudioInfo? _outputInfo;
    private AudioDiagnostics? _outputDiagnostics;
    private AudioComparisonReport? _comparisonReport;
    private AudioStreamInfo? _selectedAudioStream;
    private string _outputPath = string.Empty;
    private string _errorMessage = string.Empty;
    private double _progress;

    public BatchProcessingItem(string sourcePath)
    {
        SourcePath = sourcePath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    public BatchProcessingStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(CanProcess));
                OnPropertyChanged(nameof(IsFinished));
            }
        }
    }

    public string StatusDisplay => LocalizationService.Instance[$"BatchStatus_{Status}"];

    public AudioInfo? AudioInfo
    {
        get => _audioInfo;
        private set => SetProperty(ref _audioInfo, value);
    }

    public AudioDiagnostics? AudioDiagnostics
    {
        get => _audioDiagnostics;
        private set => SetProperty(ref _audioDiagnostics, value);
    }

    public AudioAnalysisReport? AnalysisReport
    {
        get => _analysisReport;
        private set
        {
            if (SetProperty(ref _analysisReport, value))
            {
                OnPropertyChanged(nameof(ScoreDisplay));
            }
        }
    }

    public string ScoreDisplay => AnalysisReport?.ScoreDisplay ?? "-";

    public AudioInfo? OutputInfo
    {
        get => _outputInfo;
        private set => SetProperty(ref _outputInfo, value);
    }

    public AudioDiagnostics? OutputDiagnostics
    {
        get => _outputDiagnostics;
        private set => SetProperty(ref _outputDiagnostics, value);
    }

    public AudioComparisonReport? ComparisonReport
    {
        get => _comparisonReport;
        private set
        {
            if (SetProperty(ref _comparisonReport, value))
            {
                OnPropertyChanged(nameof(HasComparisonReport));
                OnPropertyChanged(nameof(ValidationStatusDisplay));
                OnPropertyChanged(nameof(HasComparisonWarnings));
            }
        }
    }

    public bool HasComparisonReport => ComparisonReport is not null;

    public bool HasComparisonWarnings => ComparisonReport?.HasWarningsOrErrors == true;

    public string ValidationStatusDisplay => ComparisonReport?.StatusText ?? "-";

    public AudioStreamInfo? SelectedAudioStream
    {
        get => _selectedAudioStream;
        private set => SetProperty(ref _selectedAudioStream, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (SetProperty(ref _outputPath, value))
            {
                OnPropertyChanged(nameof(HasOutput));
            }
        }
    }

    public bool HasOutput => File.Exists(OutputPath);

    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(ProgressDisplay));
            }
        }
    }

    public string ProgressDisplay => $"{Progress:0}%";

    public bool CanProcess => Status == BatchProcessingStatus.Ready;

    public bool IsFinished => Status is BatchProcessingStatus.Done or BatchProcessingStatus.Failed or BatchProcessingStatus.Cancelled;

    public void SetAudioInfo(AudioInfo? value)
    {
        if (ReferenceEquals(_audioInfo, value))
        {
            return;
        }

        _audioInfo?.Dispose();
        AudioInfo = value;
        SelectedAudioStream = value?.SelectedAudioStream;
        OnPropertyChanged(nameof(HasMultipleAudioStreams));
    }

    public void SetAudioDiagnostics(AudioDiagnostics? value)
    {
        if (ReferenceEquals(_audioDiagnostics, value))
        {
            return;
        }

        _audioDiagnostics?.Dispose();
        AudioDiagnostics = value;
    }

    public void SetAnalysisReport(AudioAnalysisReport? value)
    {
        AnalysisReport = value;
    }

    public void SetOutputInfo(AudioInfo? value)
    {
        if (ReferenceEquals(_outputInfo, value))
        {
            return;
        }

        _outputInfo?.Dispose();
        OutputInfo = value;
    }

    public void SetOutputDiagnostics(AudioDiagnostics? value)
    {
        if (ReferenceEquals(_outputDiagnostics, value))
        {
            return;
        }

        _outputDiagnostics?.Dispose();
        OutputDiagnostics = value;
    }

    public void SetComparisonReport(AudioComparisonReport? value)
    {
        ComparisonReport = value;
    }

    public bool HasMultipleAudioStreams => AudioInfo?.HasMultipleAudioStreams == true;

    public void SelectAudioStream(AudioStreamInfo? audioStream)
    {
        if (AudioInfo is null || audioStream is null)
        {
            return;
        }

        var selectedInfo = AudioInfo.WithSelectedAudioStream(audioStream);
        SetAudioInfo(selectedInfo);
        SetAudioDiagnostics(null);
        SetAnalysisReport(null);
        SetOutputInfo(null);
        SetOutputDiagnostics(null);
        SetComparisonReport(null);
    }

    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(ValidationStatusDisplay));
    }

    public void Dispose()
    {
        _audioInfo?.Dispose();
        _audioDiagnostics?.Dispose();
        _outputInfo?.Dispose();
        _outputDiagnostics?.Dispose();
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
