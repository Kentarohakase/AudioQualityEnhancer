using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed record AudioComparisonFinding(
    AudioComparisonFindingKind Kind,
    AudioInsightSeverity Severity,
    string SeverityDisplay,
    string Title,
    string Message)
{
    public string CategoryDisplay => LocalizationService.Instance[GetCategoryKey(Kind)];

    private static string GetCategoryKey(AudioComparisonFindingKind kind)
    {
        return kind switch
        {
            AudioComparisonFindingKind.OutputMissing or
            AudioComparisonFindingKind.OutputEmpty or
            AudioComparisonFindingKind.OutputUnreadable or
            AudioComparisonFindingKind.DurationMismatch => "ValidationCategory_File",
            AudioComparisonFindingKind.CodecMismatch or
            AudioComparisonFindingKind.SampleRateMismatch or
            AudioComparisonFindingKind.ChannelCountChanged => "ValidationCategory_Format",
            AudioComparisonFindingKind.PotentialClipping or
            AudioComparisonFindingKind.LowHeadroom or
            AudioComparisonFindingKind.LoudnessOffTarget => "ValidationCategory_Level",
            _ => "ValidationCategory_Processing"
        };
    }
}
