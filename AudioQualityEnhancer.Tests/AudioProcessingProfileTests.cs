using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioProcessingProfileTests
{
    [Fact]
    public void MusicPreset_UsesConservativeLoudnessNormalization()
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.Music,
            ExportFormat = ExportFormat.Flac,
            UseTwoPassLoudness = false
        });

        Assert.Equal("loudnorm=I=-14:TP=-1.5:LRA=11", preview);
    }

    [Fact]
    public void SpeechPreset_AddsOnlyRequestedSpeechProcessing()
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.Speech,
            ExportFormat = ExportFormat.PremierePro,
            EnableSpeechCompression = true,
            EnableSpeechPresenceBoost = true,
            UseTwoPassLoudness = false
        });

        Assert.StartsWith("highpass=f=80", preview, StringComparison.Ordinal);
        Assert.Contains("equalizer=f=3500:t=q:w=1:g=2", preview);
        Assert.Contains("acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250", preview);
        Assert.Contains("loudnorm=I=-16:TP=-1.5:LRA=11", preview);
    }

    [Theory]
    [InlineData(-99, "afftdn=nf=-35")]
    [InlineData(-25, "afftdn=nf=-25")]
    [InlineData(0, "afftdn=nf=-20")]
    public void NoiseReductionPreset_ClampsNoiseFloorToSafeRange(int inputFloor, string expectedFilter)
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.NoiseReduction,
            NoiseReductionFloor = inputFloor
        });

        Assert.Equal(expectedFilter, preview);
    }

    [Fact]
    public void CopyPreset_ShowsStreamCopyInsteadOfFilters()
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.ExtractCopy
        });

        Assert.Contains("-c:a copy", preview);
    }

    [Fact]
    public void PremiereProExport_IsLosslessWav24At48Khz()
    {
        Assert.Equal("premiere_pro", ExportFormat.PremierePro.Id);
        Assert.Equal(".wav", ExportFormat.PremierePro.Extension);
        Assert.True(ExportFormat.PremierePro.IsLossless);
        Assert.Equal(new[] { "-ar", "48000", "-c:a", "pcm_s24le" }, ExportFormat.PremierePro.FFmpegArguments);
    }

    [Fact]
    public void PresetAndExportLists_ContainExpectedProfiles()
    {
        Assert.Contains(AudioPreset.ArchiveExport, AudioPreset.All);
        Assert.Contains(AudioPreset.EverydayExport, AudioPreset.All);
        Assert.Contains(ExportFormat.PremierePro, ExportFormat.All);
        Assert.Contains(ExportFormat.Opus_192, ExportFormat.All);
    }
}
