namespace AudioQualityEnhancer.Models;

public sealed record BatchQueueAddResult(
    IReadOnlyList<BatchProcessingItem> AddedItems,
    IReadOnlyList<string> RejectedPaths);
