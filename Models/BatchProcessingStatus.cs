namespace AudioQualityEnhancer.Models;

public enum BatchProcessingStatus
{
    Pending,
    Analyzing,
    Ready,
    Processing,
    Done,
    Failed,
    Cancelled
}
