using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class BatchQueueViewService
{
    private readonly BatchQueueService _batchQueueService;

    public BatchQueueViewService(BatchQueueService batchQueueService)
    {
        _batchQueueService = batchQueueService;
    }

    public bool MatchesFilter(BatchProcessingItem item, BatchQueueFilter filter)
    {
        return _batchQueueService.MatchesFilter(item, filter);
    }

    public BatchProcessingItem? FindNextVisibleItem(
        IEnumerable<BatchProcessingItem> items,
        BatchQueueFilter filter,
        int preferredIndex)
    {
        return _batchQueueService.FindNextVisibleItem(items, filter, preferredIndex);
    }

    public string BuildSummaryText(IEnumerable<BatchProcessingItem> items)
    {
        var summary = _batchQueueService.BuildSummary(items);
        return summary.HasItems
            ? LocalizationService.Instance.Format(
                "BatchSummary_Format",
                summary.Total,
                summary.Ready,
                summary.Processing,
                summary.Done,
                summary.DoneWithWarnings,
                summary.Failed,
                summary.Cancelled)
            : LocalizationService.Instance["BatchSummary_Empty"];
    }

    public int CountStatus(IEnumerable<BatchProcessingItem> items, BatchProcessingStatus status)
    {
        return items.Count(item => item.Status == status);
    }
}
