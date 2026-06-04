using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class BatchQueueService
{
    private readonly FileNameService _fileNameService;

    public BatchQueueService(FileNameService fileNameService)
    {
        _fileNameService = fileNameService;
    }

    public BatchQueueAddResult CreateItems(IEnumerable<string> paths, IEnumerable<BatchProcessingItem> existingItems)
    {
        var added = new List<BatchProcessingItem>();
        var rejected = new List<string>();
        var knownPaths = existingItems
            .Select(item => Path.GetFullPath(item.SourcePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                rejected.Add(path);
                continue;
            }

            if (!File.Exists(fullPath) || !_fileNameService.IsSupportedInputFile(fullPath) || !knownPaths.Add(fullPath))
            {
                rejected.Add(path);
                continue;
            }

            added.Add(new BatchProcessingItem(fullPath));
        }

        return new BatchQueueAddResult(added, rejected);
    }

    public IReadOnlyList<BatchProcessingItem> GetProcessableItems(IEnumerable<BatchProcessingItem> items)
    {
        return items.Where(item => item.CanProcess).ToArray();
    }

    public IReadOnlyList<BatchProcessingItem> GetFinishedItems(IEnumerable<BatchProcessingItem> items)
    {
        return items.Where(item => item.IsFinished).ToArray();
    }

    public IReadOnlyList<BatchProcessingItem> GetRetryableItems(IEnumerable<BatchProcessingItem> items)
    {
        return items.Where(CanRetry).ToArray();
    }

    public IReadOnlyList<BatchProcessingItem> GetItemsByFilter(
        IEnumerable<BatchProcessingItem> items,
        BatchQueueFilter filter)
    {
        return items.Where(item => MatchesFilter(item, filter)).ToArray();
    }

    public bool CanRetry(BatchProcessingItem? item)
    {
        return item?.Status is BatchProcessingStatus.Failed or BatchProcessingStatus.Cancelled;
    }

    public bool ResetForRetry(BatchProcessingItem item)
    {
        if (!CanRetry(item))
        {
            return false;
        }

        item.OutputPath = string.Empty;
        item.ErrorMessage = string.Empty;
        item.Progress = 0;
        item.SetOutputInfo(null);
        item.SetOutputDiagnostics(null);
        item.SetComparisonReport(null);
        item.Status = item.AudioInfo is null
            ? BatchProcessingStatus.Pending
            : BatchProcessingStatus.Ready;
        return true;
    }

    public void MarkProcessingStarted(BatchProcessingItem item)
    {
        item.Status = BatchProcessingStatus.Processing;
        item.Progress = 0;
        item.ErrorMessage = string.Empty;
    }

    public void MarkValidationStarted(BatchProcessingItem item)
    {
        item.Status = BatchProcessingStatus.Validating;
    }

    public bool MatchesFilter(BatchProcessingItem item, BatchQueueFilter filter)
    {
        return filter switch
        {
            BatchQueueFilter.All => true,
            BatchQueueFilter.Ready => item.Status == BatchProcessingStatus.Ready,
            BatchQueueFilter.Processing => item.Status is BatchProcessingStatus.Analyzing or BatchProcessingStatus.Processing or BatchProcessingStatus.Validating,
            BatchQueueFilter.Done => item.Status == BatchProcessingStatus.Done,
            BatchQueueFilter.Warnings => item.Status == BatchProcessingStatus.Done && item.HasComparisonWarnings,
            BatchQueueFilter.Failed => item.Status == BatchProcessingStatus.Failed,
            BatchQueueFilter.Cancelled => item.Status == BatchProcessingStatus.Cancelled,
            _ => true
        };
    }

    public BatchProcessingItem? FindNextVisibleItem(
        IEnumerable<BatchProcessingItem> items,
        BatchQueueFilter filter,
        int preferredIndex)
    {
        var visibleItems = GetItemsByFilter(items, filter);
        if (visibleItems.Count == 0)
        {
            return null;
        }

        return visibleItems[Math.Clamp(preferredIndex, 0, visibleItems.Count - 1)];
    }

    public BatchQueueSummary BuildSummary(IEnumerable<BatchProcessingItem> items)
    {
        var snapshot = items.ToArray();
        return new BatchQueueSummary(
            snapshot.Length,
            Count(snapshot, BatchProcessingStatus.Pending),
            Count(snapshot, BatchProcessingStatus.Analyzing),
            Count(snapshot, BatchProcessingStatus.Ready),
            Count(snapshot, BatchProcessingStatus.Processing),
            Count(snapshot, BatchProcessingStatus.Validating),
            Count(snapshot, BatchProcessingStatus.Done),
            snapshot.Count(item => item.Status == BatchProcessingStatus.Done && item.ComparisonReport?.HasWarningsOrErrors == true),
            Count(snapshot, BatchProcessingStatus.Failed),
            Count(snapshot, BatchProcessingStatus.Cancelled));
    }

    public static double CalculateOverallProgress(int itemIndex, int totalItems, double itemProgress)
    {
        if (totalItems <= 0)
        {
            return 0;
        }

        var completedBeforeCurrent = Math.Clamp(itemIndex, 0, totalItems);
        var normalizedItemProgress = Math.Clamp(itemProgress, 0, 100) / 100d;
        return Math.Clamp((completedBeforeCurrent + normalizedItemProgress) / totalItems * 100d, 0, 100);
    }

    private static int Count(IEnumerable<BatchProcessingItem> items, BatchProcessingStatus status)
    {
        return items.Count(item => item.Status == status);
    }
}
