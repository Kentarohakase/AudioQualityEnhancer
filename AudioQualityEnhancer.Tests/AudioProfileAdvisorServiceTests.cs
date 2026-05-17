using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioProfileAdvisorServiceTests
{
    private readonly AudioProfileAdvisorService _service = new(new FileNameService());

    [Fact]
    public void BuildAdvice_GoodLosslessSource_OffersGentleFlacAndEverydayExport()
    {
        using var info = CreateInfo("track.flac", "flac", false, 900_000, 48_000, 2);
        using var diagnostics = CreateDiagnostics(-14, -3);

        var advice = _service.BuildAdvice(info, diagnostics);

        Assert.Contains(advice.Suggestions, suggestion =>
            suggestion.Preset.Id == AudioPreset.Music.Id &&
            suggestion.ExportFormat?.Id == ExportFormat.Flac.Id);
        Assert.Contains(advice.Suggestions, suggestion =>
            suggestion.Preset.Id == AudioPreset.EverydayExport.Id &&
            suggestion.ExportFormat?.Id == ExportFormat.Aac_256.Id);
        Assert.False(advice.NeedsAdvancedAnalysis);
    }

    [Fact]
    public void BuildAdvice_MonoLowBitrateSource_PrioritizesSpeech()
    {
        using var info = CreateInfo("speech.mp3", "mp3", true, 80_000, 44_100, 1);

        var advice = _service.BuildAdvice(info, diagnostics: null);

        Assert.Equal(AudioPreset.Speech.Id, advice.Suggestions[0].Preset.Id);
        Assert.True(advice.NeedsAdvancedAnalysis);
        Assert.True(advice.HasNote);
    }

    [Fact]
    public void BuildAdvice_VideoSource_OffersPremiereProfile()
    {
        using var info = CreateInfo("clip.mkv", "unknown", false, null, 48_000, 2, container: "matroska,webm");

        var advice = _service.BuildAdvice(info, diagnostics: null);

        Assert.Contains(advice.Suggestions, suggestion =>
            suggestion.ExportFormat?.Id == ExportFormat.PremierePro.Id);
    }

    [Fact]
    public void BuildAdvice_ClippingSource_PrefersLosslessOutput()
    {
        using var info = CreateInfo("track.wav", "pcm_s24le", false, 1_400_000, 48_000, 2);
        using var diagnostics = CreateDiagnostics(-12, -0.05);

        var advice = _service.BuildAdvice(info, diagnostics);

        var first = advice.Suggestions[0];
        Assert.Equal(AudioPreset.Music.Id, first.Preset.Id);
        Assert.Equal(ExportFormat.Flac.Id, first.ExportFormat?.Id);
        Assert.Equal("lossless_headroom", first.Id);
    }

    [Fact]
    public void BuildAdvice_StreamCopy_IsOnlySuggestedWhenCompatibleContainerExists()
    {
        using var mp3Info = CreateInfo("track.mp3", "mp3", true, 320_000, 44_100, 2);
        using var unsupportedInfo = CreateInfo("track.raw", "unknown_codec", false, null, 44_100, 2);

        var mp3Advice = _service.BuildAdvice(mp3Info, diagnostics: null);
        var unsupportedAdvice = _service.BuildAdvice(unsupportedInfo, diagnostics: null);

        Assert.Contains(mp3Advice.Suggestions, suggestion =>
            suggestion.Preset.Id == AudioPreset.ExtractCopy.Id &&
            suggestion.ExportFormat is null);
        Assert.DoesNotContain(unsupportedAdvice.Suggestions, suggestion =>
            suggestion.Preset.Id == AudioPreset.ExtractCopy.Id);
    }

    [Fact]
    public void BuildAdvice_ReturnsAtMostThreeSuggestions()
    {
        using var info = CreateInfo("clip.mp4", "aac", true, 256_000, 48_000, 2, container: "mov,mp4,m4a,3gp,3g2,mj2");

        var advice = _service.BuildAdvice(info, diagnostics: null);

        Assert.InRange(advice.Suggestions.Count, 1, 3);
    }

    private static AudioInfo CreateInfo(
        string sourcePath,
        string codec,
        bool isLikelyLossy,
        long? bitRate,
        int? sampleRate,
        int? channels,
        string container = "audio")
    {
        return new AudioInfo
        {
            SourcePath = sourcePath,
            Codec = codec,
            BitRate = bitRate,
            SampleRate = sampleRate,
            Channels = channels,
            Container = container,
            Duration = TimeSpan.FromSeconds(30),
            FileSizeBytes = 1024 * 1024,
            IsLikelyLossy = isLikelyLossy
        };
    }

    private static AudioDiagnostics CreateDiagnostics(double loudness, double peak)
    {
        return new AudioDiagnostics
        {
            IntegratedLoudnessLufs = loudness,
            TruePeakDb = peak,
            MaxVolumeDb = peak - 0.1
        };
    }
}
