using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class QualityReportServiceTests
{
    [Fact]
    public void BuildMarkdown_AddsShortResultSummaryCountsAndPrioritizedFindings()
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
        using var item = new BatchProcessingItem(@"C:\audio\voice.mp3")
        {
            Status = BatchProcessingStatus.Done,
            OutputPath = @"C:\audio\voice_speech.flac"
        };
        item.SetComparisonReport(new AudioComparisonReport(
            AudioComparisonStatus.Warning,
            "Warning",
            "The result should be checked before sharing.",
            item.OutputPath,
            outputInfo: null,
            outputDiagnostics: null,
            new[]
            {
                new AudioComparisonFinding(AudioComparisonFindingKind.LowHeadroom, AudioInsightSeverity.Warning, "Warning", "Low headroom", "The result is close to the ceiling."),
                new AudioComparisonFinding(AudioComparisonFindingKind.PotentialClipping, AudioInsightSeverity.Critical, "Critical", "Possible clipping", "The result may be clipped.")
            },
            Array.Empty<AudioComparisonMetric>(),
            outputDiagnosticsSkipped: false));
        using var missingReportItem = new BatchProcessingItem(@"C:\audio\missing.mp3")
        {
            Status = BatchProcessingStatus.Failed
        };

        var markdown = QualityReportService.BuildMarkdown(
            new[] { item, missingReportItem },
            AudioPreset.Speech,
            ExportFormat.Flac);

        var lines = markdown.Split(Environment.NewLine);
        Assert.Contains(lines, line => line.Contains(": 1", StringComparison.Ordinal) &&
                                      (line.Contains("Ergebnis mit Warnung", StringComparison.OrdinalIgnoreCase) ||
                                       line.Contains("Result with warning", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(lines, line => line.Contains(": 1", StringComparison.Ordinal) &&
                                      (line.Contains("Ohne Ergebnisprüfung", StringComparison.OrdinalIgnoreCase) ||
                                       line.Contains("Without result check", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Warning - The result should be checked before sharing.", markdown);
        Assert.True(markdown.IndexOf("Possible clipping", StringComparison.Ordinal) < markdown.IndexOf("Low headroom", StringComparison.Ordinal));
    }
}
