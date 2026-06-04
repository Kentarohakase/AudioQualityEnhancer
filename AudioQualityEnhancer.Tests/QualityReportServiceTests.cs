using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class QualityReportServiceTests
{
    [Fact]
    public void BuildMarkdown_AddsShortResultSummaryBeforeDetails()
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
            Array.Empty<AudioComparisonFinding>(),
            Array.Empty<AudioComparisonMetric>(),
            outputDiagnosticsSkipped: false));

        var markdown = QualityReportService.BuildMarkdown(
            new[] { item },
            AudioPreset.Speech,
            ExportFormat.Flac);

        Assert.Contains("Short verdict: Warning - The result should be checked before sharing.", markdown);
    }
}
