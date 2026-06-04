using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed record AudioAnalysisFinding(
    AudioAnalysisFindingKind Kind,
    AudioInsightSeverity Severity,
    string SeverityDisplay,
    string Title,
    string Message)
{
    public string CategoryDisplay => LocalizationService.Instance[GetCategoryKey(Kind)];

    private static string GetCategoryKey(AudioAnalysisFindingKind kind)
    {
        return kind switch
        {
            AudioAnalysisFindingKind.PotentialClipping or
            AudioAnalysisFindingKind.LowHeadroom or
            AudioAnalysisFindingKind.VeryQuiet or
            AudioAnalysisFindingKind.AlreadyLoud => "AnalysisCategory_Level",
            AudioAnalysisFindingKind.LowSampleRate or
            AudioAnalysisFindingKind.MonoSource or
            AudioAnalysisFindingKind.MultichannelSource => "AnalysisCategory_Format",
            AudioAnalysisFindingKind.LossyTranscodingRisk or
            AudioAnalysisFindingKind.AdvancedAnalysisRecommended => "AnalysisCategory_Processing",
            _ => "AnalysisCategory_Source"
        };
    }
}
