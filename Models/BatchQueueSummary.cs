namespace AudioQualityEnhancer.Models;

public sealed record BatchQueueSummary(
    int Total,
    int Pending,
    int Analyzing,
    int Ready,
    int Processing,
    int Validating,
    int Done,
    int DoneWithWarnings,
    int Failed,
    int Cancelled)
{
    public int Finished => Done + Failed + Cancelled;

    public bool HasItems => Total > 0;
}
