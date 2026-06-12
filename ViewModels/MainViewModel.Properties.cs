using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Bindable properties and command surface of the main view model.
public sealed partial class MainViewModel
{
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

    public ICommand RenderProcessedPreviewCommand => _renderProcessedPreviewCommand;

    public ICommand PlayProcessedPreviewCommand => _playProcessedPreviewCommand;

    public ICommand StopPreviewCommand => _stopPreviewCommand;

    public ICommand OpenOutputFolderCommand => _openOutputFolderCommand;

    public ICommand OpenSelectedOutputCommand => _openSelectedOutputCommand;

    public ICommand OpenSelectedOutputFolderCommand => _openSelectedOutputFolderCommand;

    public ICommand OpenLastOutputCommand => _openLastOutputCommand;

    public ICommand OpenLastReportCommand => _openLastReportCommand;

    public ICommand OpenLastReportFolderCommand => _openLastReportFolderCommand;

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
                InvalidateProcessedPreview();
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
                InvalidateProcessedPreview();
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
                    SelectVisibleBatchItem(0);
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
            if (SetProperty(ref _selectedAudioStream, value))
            {
                InvalidateProcessedPreview();
                if (!_syncingSelectedBatchItem)
                {
                    SelectAudioStreamForCurrentItem(value);
                }

                RaiseCommandStates();
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
                OnPropertyChanged(nameof(IsNoiseTrackingPreset));
                UpdateQualityNotice();
                UpdateFilterDetails();
                InvalidateProcessedPreview();
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
                OnPropertyChanged(nameof(HasReportFolder));
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
                InvalidateProcessedPreview();
                RaiseCommandStates();
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
                InvalidateProcessedPreview();
                RaiseCommandStates();
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

    public ObservableCollection<LoudnessTargetOption> LoudnessTargets { get; }

    public LoudnessTargetOption SelectedLoudnessTarget
    {
        get => _selectedLoudnessTarget;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetProperty(ref _selectedLoudnessTarget, value))
            {
                UpdateQualityNotice();
                UpdateFilterDetails();
                InvalidateProcessedPreview();
                RaiseCommandStates();
            }
        }
    }

    public bool EnableNoiseTracking
    {
        get => _enableNoiseTracking;
        set
        {
            if (SetProperty(ref _enableNoiseTracking, value))
            {
                UpdateFilterDetails();
                InvalidateProcessedPreview();
                RaiseCommandStates();
            }
        }
    }

    public bool IsNoiseTrackingPreset =>
        SelectedPreset?.Id == AudioPreset.NoiseReduction.Id ||
        SelectedPreset?.Id == AudioPreset.NoisySpeechCleanup.Id;

    public int NoiseReductionFloor
    {
        get => _noiseReductionFloor;
        set
        {
            if (SetProperty(ref _noiseReductionFloor, value))
            {
                UpdateFilterDetails();
                InvalidateProcessedPreview();
                RaiseCommandStates();
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
                    _audioPreviewController.Position = TimeSpan.FromSeconds(value);
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

    public bool IsLoudnessPreset =>
        SelectedPreset?.Id == AudioPreset.Music.Id ||
        SelectedPreset?.Id == AudioPreset.Speech.Id ||
        SelectedPreset?.Id == AudioPreset.PodcastVoice.Id ||
        SelectedPreset?.Id == AudioPreset.NoisySpeechCleanup.Id;

    public bool HasOutputPreview => File.Exists(LastOutputPath);

    public bool HasProcessedPreview => File.Exists(_processedPreviewPath);

    public bool HasReportFile => File.Exists(LastReportPath);

    public bool HasReportFolder => GetExistingDirectoryForFile(LastReportPath) is not null;
}
