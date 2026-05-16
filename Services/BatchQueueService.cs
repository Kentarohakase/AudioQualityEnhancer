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

    public BatchQueueSummary BuildSummary(IEnumerable<BatchProcessingItem> items)
    {
        var snapshot = items.ToArray();
        return new BatchQueueSummary(
            snapshot.Length,
            Count(snapshot, BatchProcessingStatus.Pending),
            Count(snapshot, BatchProcessingStatus.Analyzing),
            Count(snapshot, BatchProcessingStatus.Ready),
            Count(snapshot, BatchProcessingStatus.Processing),
            Count(snapshot, BatchProcessingStatus.Done),
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
