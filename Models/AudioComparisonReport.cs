namespace AudioQualityEnhancer.Models;

public sealed class AudioComparisonReport
{
    public AudioComparisonReport(
        AudioComparisonStatus status,
        string statusText,
        string summary,
        string outputPath,
        AudioInfo? outputInfo,
        AudioDiagnostics? outputDiagnostics,
        IReadOnlyList<AudioComparisonFinding> findings,
        IReadOnlyList<AudioComparisonMetric> metrics,
        bool outputDiagnosticsSkipped)
    {
        Status = status;
        StatusText = statusText;
        Summary = summary;
        OutputPath = outputPath;
        OutputInfo = outputInfo;
        OutputDiagnostics = outputDiagnostics;
        Findings = findings;
        Metrics = metrics;
        OutputDiagnosticsSkipped = outputDiagnosticsSkipped;
    }

    public AudioComparisonStatus Status { get; }

    public string StatusText { get; }

    public string Summary { get; }

    public string OutputPath { get; }

    public string OutputPathDisplay => string.IsNullOrWhiteSpace(OutputPath) ? "-" : OutputPath;

    public AudioInfo? OutputInfo { get; }

    public AudioDiagnostics? OutputDiagnostics { get; }

    public IReadOnlyList<AudioComparisonFinding> Findings { get; }

    public IReadOnlyList<AudioComparisonMetric> Metrics { get; }

    public bool OutputDiagnosticsSkipped { get; }

    public bool HasFindings => Findings.Count > 0;

    public bool HasMetrics => Metrics.Count > 0;

    public bool HasWarningsOrErrors => Status is AudioComparisonStatus.Warning or AudioComparisonStatus.Critical;
}
