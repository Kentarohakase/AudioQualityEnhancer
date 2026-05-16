namespace AudioQualityEnhancer.Models;

public sealed record AudioAnalysisRecommendation(
    AudioAnalysisFindingKind Kind,
    string Text);
