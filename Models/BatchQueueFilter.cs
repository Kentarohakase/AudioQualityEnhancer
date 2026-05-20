namespace AudioQualityEnhancer.Models;

public enum BatchQueueFilter
{
    All,
    Ready,
    Processing,
    Done,
    Warnings,
    Failed,
    Cancelled
}
