namespace AudioQualityEnhancer.Models;

public enum AudioAnalysisFindingKind
{
    NoIssues,
    LossySource,
    LowBitrate,
    LowSampleRate,
    PotentialClipping,
    LowHeadroom,
    VeryQuiet,
    AlreadyLoud,
    AdvancedAnalysisRecommended
}
