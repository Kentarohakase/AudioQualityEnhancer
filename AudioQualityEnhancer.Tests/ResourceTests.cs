using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed partial class ResourceTests
{
    [Fact]
    public void GermanAndEnglishResources_ExposeTheSameKeys()
    {
        var germanKeys = LoadResourceKeys("Strings.resx");
        var englishKeys = LoadResourceKeys("Strings.en.resx");

        Assert.Empty(germanKeys.Except(englishKeys));
        Assert.Empty(englishKeys.Except(germanKeys));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    public void VisibleModelTexts_DoNotContainMissingResourceMarkers(string cultureName)
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo(cultureName);

        var values = new List<string>
        {
            new FileNameService().BuildOpenDialogFilter(),
            LocalizationService.Instance["Button_RenderProcessedPreview"],
            LocalizationService.Instance["Button_PlayProcessedPreview"],
            LocalizationService.Instance["Status_ProcessedPreviewRendering"],
            LocalizationService.Instance.Format("Status_ProcessedPreviewReadyFormat", "preview.wav"),
            LocalizationService.Instance.Format("Log_ProcessedPreviewStartingFormat", 20),
            LocalizationService.Instance.Format("Log_ProcessedPreviewReadyFormat", "preview.wav"),
            LocalizationService.Instance["Error_ProcessedPreviewNoFilters"],
            LocalizationService.Instance["Error_ProcessedPreviewFailed"],
            LocalizationService.Instance["Error_ProcessedPreviewMissingOutput"],
            AudioPreset.Music.Name,
            AudioPreset.Music.Description,
            AudioPreset.Music.QualityNote,
            AudioPreset.Speech.Name,
            AudioPreset.PodcastVoice.Name,
            AudioPreset.PodcastVoice.Description,
            AudioPreset.PodcastVoice.QualityNote,
            AudioPreset.NoisySpeechCleanup.Name,
            AudioPreset.NoisySpeechCleanup.Description,
            AudioPreset.NoisySpeechCleanup.QualityNote,
            AudioPreset.NoiseReduction.QualityNote,
            AudioPreset.ArchiveExport.QualityNote,
            ExportFormat.Flac.DisplayName,
            ExportFormat.Flac.Description,
            ExportFormat.PremierePro.DisplayName,
            ExportFormat.PremierePro.Description
        };

        using var unknownInfo = new AudioInfo();
        values.Add(unknownInfo.CodecDisplay);
        values.Add(unknownInfo.ContainerDisplay);
        values.Add(unknownInfo.BitRateDisplay);
        values.Add(unknownInfo.SampleRateDisplay);
        values.Add(unknownInfo.ChannelsDisplay);
        values.Add(unknownInfo.DurationDisplay);
        values.Add(unknownInfo.FileSizeDisplay);
        values.Add(unknownInfo.LossyDisplay);

        var streamInfo = new AudioStreamInfo(2, 1, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(20), "eng", "English", string.Empty);
        values.Add(streamInfo.DisplayName);
        values.Add(streamInfo.BitRateDisplay);
        values.Add(streamInfo.SampleRateDisplay);
        values.Add(streamInfo.ChannelsDisplay);
        values.Add(streamInfo.DurationDisplay);

        using var reportInfo = new AudioInfo
        {
            Codec = "mp3",
            IsLikelyLossy = true,
            BitRate = 96_000,
            SampleRate = 22_050,
            Channels = 2,
            FileSizeBytes = 1024
        };
        var report = new AudioAnalysisInsightService().BuildReport(reportInfo, diagnostics: null);
        values.Add(report.StatusText);
        values.Add(report.Summary);
        values.AddRange(report.Findings.SelectMany(f => new[] { f.SeverityDisplay, f.Title, f.Message }));
        values.AddRange(report.Recommendations.Select(r => r.Text));

        var profileAdvice = new AudioProfileAdvisorService(new FileNameService()).BuildAdvice(reportInfo, diagnostics: null);
        values.Add(profileAdvice.Note);
        values.AddRange(profileAdvice.Suggestions.SelectMany(s => new[] { s.Title, s.TargetDisplay, s.Reason, s.Note }));

        using var outputInfo = new AudioInfo
        {
            Codec = "flac",
            BitRate = 900_000,
            SampleRate = 48_000,
            Channels = 2,
            Duration = TimeSpan.FromSeconds(30),
            Container = "flac",
            FileSizeBytes = 2048
        };
        using var outputDiagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -1.5,
            MaxVolumeDb = -1.6
        };
        var comparisonReport = AudioValidationService.BuildReport(
            new ProcessingOptions
            {
                SourceInfo = reportInfo,
                Preset = AudioPreset.Music,
                ExportFormat = ExportFormat.Flac
            },
            reportInfo,
            outputInfo,
            sourceDiagnostics: null,
            outputDiagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: "output.flac");
        values.Add(comparisonReport.StatusText);
        values.Add(comparisonReport.Summary);
        values.AddRange(comparisonReport.Findings.SelectMany(f => new[] { f.SeverityDisplay, f.Title, f.Message }));
        values.AddRange(comparisonReport.Metrics.SelectMany(m => new[] { m.Label, m.SourceValue, m.OutputValue }));

        using var batchItem = new BatchProcessingItem("track.mp3");
        values.Add(batchItem.StatusDisplay);
        values.Add(batchItem.ValidationStatusDisplay);
        batchItem.Status = BatchProcessingStatus.Ready;
        values.Add(batchItem.StatusDisplay);
        batchItem.Status = BatchProcessingStatus.Failed;
        values.Add(batchItem.StatusDisplay);

        foreach (var value in values)
        {
            Assert.DoesNotMatch(MissingResourceRegex(), value);
        }
    }

    private static IReadOnlySet<string> LoadResourceKeys(string fileName)
    {
        var path = Path.Combine(TestPaths.RepositoryRoot, "Resources", fileName);
        var document = XDocument.Load(path);

        return document
            .Descendants("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"![A-Za-z0-9_]+!")]
    private static partial Regex MissingResourceRegex();
}
