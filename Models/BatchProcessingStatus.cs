namespace AudioQualityEnhancer.Models;

public enum BatchProcessingStatus
{
    Pending,
    Analyzing,
    Ready,
    Processing,
    Validating,
    Done,
    Failed,
    Cancelled
}
