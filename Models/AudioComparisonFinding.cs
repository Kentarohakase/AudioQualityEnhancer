namespace AudioQualityEnhancer.Models;

public sealed record AudioComparisonFinding(
    AudioComparisonFindingKind Kind,
    AudioInsightSeverity Severity,
    string SeverityDisplay,
    string Title,
    string Message);
