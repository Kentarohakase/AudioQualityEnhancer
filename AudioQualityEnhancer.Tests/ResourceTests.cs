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
            AudioPreset.Music.Name,
            AudioPreset.Music.Description,
            AudioPreset.Music.QualityNote,
            AudioPreset.Speech.Name,
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
