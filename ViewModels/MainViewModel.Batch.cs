using System.Collections.Specialized;
using System.ComponentModel;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.ViewModels;

// Batch queue management: add/remove, retry, filtering and selection sync.
public sealed partial class MainViewModel
{
    private void RemoveSelectedFile()
    {
        var item = SelectedBatchItem;
        if (item is null || IsBusy)
        {
            return;
        }

        var index = GetVisibleIndex(item);
        var removedOutputPath = item.OutputPath;
        RemoveBatchItem(item);

        if (PathsEqual(LastOutputPath, removedOutputPath))
        {
            LastOutputPath = string.Empty;
        }

        SelectVisibleBatchItem(index);

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
            var removedOutputPath = item.OutputPath;
            RemoveBatchItem(item);
            if (PathsEqual(LastOutputPath, removedOutputPath))
            {
                LastOutputPath = string.Empty;
            }
        }

        if (SelectedBatchItem is null || !BatchItems.Contains(SelectedBatchItem) || !BatchItemsView.Contains(SelectedBatchItem))
        {
            SelectVisibleBatchItem(0);
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
            var previousOutputPath = item.OutputPath;
            var wasSelected = ReferenceEquals(item, SelectedBatchItem);
            if (_batchQueueService.ResetForRetry(item))
            {
                if (PathsEqual(LastOutputPath, previousOutputPath))
                {
                    LastOutputPath = string.Empty;
                }

                if (wasSelected)
                {
                    ComparisonReport = null;
                    ProgressValue = item.Progress;
                }

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
                await AnalyzeBatchItemsAsync(needsAnalysis, CancellationToken.None);
            }
            finally
            {
                IsBusy = false;
            }
        }

        BatchItemsView.Refresh();
        if (SelectedBatchItem is not null && !BatchItemsView.Contains(SelectedBatchItem))
        {
            SelectVisibleBatchItem(0);
        }
        else
        {
            SyncSelectedBatchItem();
        }

        UpdateBatchSummary();
        RaiseCommandStates();
        return preparedItems.Count;
    }

    private bool CanRetrySelectedFile()
    {
        return !IsBusy && _batchQueueService.CanRetry(SelectedBatchItem);
    }

    private bool CanRetryFailedFiles()
    {
        return !IsBusy && _batchQueueService.GetRetryableItems(BatchItems).Count > 0;
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
            if (SelectedBatchItem is not null && !BatchItemsView.Contains(SelectedBatchItem))
            {
                SelectVisibleBatchItem(0);
            }
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
               _batchQueueViewService.MatchesFilter(batchItem, SelectedBatchFilter.Filter);
    }

    private static bool BatchViewNeedsRefresh(string? propertyName)
    {
        return propertyName is null or
            nameof(BatchProcessingItem.Status) or
            nameof(BatchProcessingItem.HasComparisonWarnings) or
            nameof(BatchProcessingItem.ComparisonReport);
    }

    private int GetVisibleIndex(BatchProcessingItem item)
    {
        var visibleItems = BatchItemsView.Cast<BatchProcessingItem>().ToArray();
        var index = Array.IndexOf(visibleItems, item);
        return index < 0 ? 0 : index;
    }

    private void SelectVisibleBatchItem(int preferredIndex)
    {
        SelectedBatchItem = _batchQueueViewService.FindNextVisibleItem(
            BatchItems,
            SelectedBatchFilter.Filter,
            preferredIndex);
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
        BatchSummaryText = _batchQueueViewService.BuildSummaryText(BatchItems);
    }

    private int CountStatus(BatchProcessingStatus status)
    {
        return _batchQueueViewService.CountStatus(BatchItems, status);
    }
}
