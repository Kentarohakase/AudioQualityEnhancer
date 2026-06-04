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

    [Fact]
    public void PodcastVoicePreset_AddsFinishedSpeechChain()
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.PodcastVoice,
            ExportFormat = ExportFormat.Aac_256,
            UseTwoPassLoudness = false
        });

        Assert.StartsWith("highpass=f=80", preview, StringComparison.Ordinal);
        Assert.Contains("equalizer=f=180:t=q:w=1:g=-2", preview);
        Assert.Contains("equalizer=f=3500:t=q:w=1:g=2", preview);
        Assert.Contains("deesser=i=0.25:m=0.5:f=0.5", preview);
        Assert.Contains("acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250", preview);
        Assert.EndsWith("loudnorm=I=-16:TP=-1.5:LRA=9", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void NoisySpeechPreset_AddsConservativeCleanupChain()
    {
        var preview = AudioProcessingService.BuildFilterPreview(new ProcessingOptions
        {
            Preset = AudioPreset.NoisySpeechCleanup,
            ExportFormat = ExportFormat.Aac_256,
            UseTwoPassLoudness = false
        });

        Assert.StartsWith("highpass=f=90", preview, StringComparison.Ordinal);
        Assert.Contains("afftdn=nf=-25", preview);
        Assert.Contains("deesser=i=0.25:m=0.5:f=0.5", preview);
        Assert.Contains("acompressor=threshold=-20dB:ratio=2:attack=20:release=250", preview);
        Assert.EndsWith("loudnorm=I=-16:TP=-1.5:LRA=9", preview, StringComparison.Ordinal);
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
    public void ResolveForPreset_ArchiveExportAlwaysUsesFlac()
    {
        Assert.Same(ExportFormat.Flac, ExportFormat.ResolveForPreset(AudioPreset.ArchiveExport, ExportFormat.Mp3_320));
        Assert.Same(ExportFormat.Aac_256, ExportFormat.ResolveForPreset(AudioPreset.Music, ExportFormat.Aac_256));
    }

    [Fact]
    public void PresetAndExportLists_ContainExpectedProfiles()
    {
        Assert.Contains(AudioPreset.ArchiveExport, AudioPreset.All);
        Assert.Contains(AudioPreset.EverydayExport, AudioPreset.All);
        Assert.Contains(AudioPreset.PodcastVoice, AudioPreset.All);
        Assert.Contains(AudioPreset.NoisySpeechCleanup, AudioPreset.All);
        Assert.Contains(ExportFormat.PremierePro, ExportFormat.All);
        Assert.Contains(ExportFormat.Opus_192, ExportFormat.All);
    }

    [Fact]
    public void BuildInputArguments_DefaultsToFirstAudioStream()
    {
        var args = AudioProcessingService.BuildInputArguments("input.mp4", audioStream: null);

        Assert.Contains("-map", args);
        Assert.Contains("0:a:0", args);
    }

    [Fact]
    public void BuildInputArguments_MapsSelectedAudioStreamByContainerStreamIndex()
    {
        var stream = new AudioStreamInfo(
            StreamIndex: 3,
            AudioStreamIndex: 1,
            Codec: "aac",
            CodecLongName: "AAC",
            BitRate: 192_000,
            SampleRate: 48_000,
            Channels: 2,
            Duration: TimeSpan.FromSeconds(10),
            Language: "eng",
            Title: "English",
            HandlerName: string.Empty);

        var args = AudioProcessingService.BuildInputArguments("input.mkv", stream);

        Assert.Contains("0:3", args);
        Assert.DoesNotContain("0:a:0", args);
    }

    [Fact]
    public void ResolveAudioStream_FallsBackToSelectedSourceStreamForInvalidRequest()
    {
        var first = new AudioStreamInfo(1, 0, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(10), "deu", "Deutsch", string.Empty);
        var invalid = new AudioStreamInfo(99, 9, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(10), "eng", "English", string.Empty);
        using var info = new AudioInfo
        {
            Codec = "aac",
            AudioStreams = new[] { first },
            SelectedAudioStreamIndex = first.StreamIndex
        };

        var resolved = AudioProcessingService.ResolveAudioStream(invalid, info);

        Assert.Same(first, resolved);
    }
}
