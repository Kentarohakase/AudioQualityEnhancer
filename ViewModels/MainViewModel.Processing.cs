using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Batch processing run, cancellation, validation and report saving.
public sealed partial class MainViewModel
{
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
                _batchQueueService.MarkProcessingStarted(item);
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
                await TrySaveBatchLogAsync();
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
        _batchQueueService.MarkValidationStarted(item);
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

    /// <summary>
    /// Saves the run log without ever failing the run: the files are already exported,
    /// so a read-only or full output folder is reported as a warning, not as a crash.
    /// </summary>
    private async Task TrySaveBatchLogAsync()
    {
        try
        {
            var logPath = await _logService.SaveAsync(OutputDirectory, "audio-quality-enhancer-batch", CancellationToken.None);
            _logService.Info(LocalizationService.Instance.Format("Log_LogSavedFormat", logPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _logService.Warning(LocalizationService.Instance["Error_LogSaveFailed"]);
        }
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
            if (File.Exists(result.Value))
            {
                LastReportPath = result.Value;
                _logService.Info(LocalizationService.Instance.Format("Log_ReportSavedFormat", result.Value));
                return;
            }

            _logService.Warning(LocalizationService.Instance["Error_ReportSaveFailed"]);
            return;
        }

        _logService.Warning(result.ErrorMessage ?? LocalizationService.Instance["Error_ReportSaveFailed"]);
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
            LoudnessTargetLufs = SelectedLoudnessTarget?.IntegratedLufs,
            EnableNoiseTracking = EnableNoiseTracking,
            EnableSpeechCompression = EnableSpeechCompression,
            EnableSpeechPresenceBoost = EnableSpeechPresenceBoost,
            UseTwoPassLoudness = UseTwoPassLoudness
        };
    }
}
