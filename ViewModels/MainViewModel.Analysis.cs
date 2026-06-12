using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;
using Forms = System.Windows.Forms;

namespace AudioQualityEnhancer.ViewModels;

// File selection and (batch) source analysis.
public sealed partial class MainViewModel
{
    private static readonly int MaxParallelBatchAnalyses = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

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
        await AnalyzeBatchItemsAsync(addResult.AddedItems, CancellationToken.None);

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

    private async Task AnalyzeBatchItemsAsync(IReadOnlyList<BatchProcessingItem> items, CancellationToken cancellationToken)
    {
        if (items.Count == 1)
        {
            await AnalyzeBatchItemAsync(items[0], cancellationToken);
            return;
        }

        // The ffprobe runs may overlap; all view model state is still mutated on the
        // UI thread because every continuation resumes on the dispatcher.
        using var throttle = new SemaphoreSlim(MaxParallelBatchAnalyses);
        await Task.WhenAll(items.Select(async item =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                await AnalyzeBatchItemAsync(item, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        }));
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
}
