namespace AudioQualityEnhancer.Models;

public sealed record AudioComparisonMetric(
    string Label,
    string SourceValue,
    string OutputValue);
