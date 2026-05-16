namespace AudioQualityEnhancer.Models;

public sealed record AudioAnalysisFinding(
    AudioAnalysisFindingKind Kind,
    AudioInsightSeverity Severity,
    string SeverityDisplay,
    string Title,
    string Message);
