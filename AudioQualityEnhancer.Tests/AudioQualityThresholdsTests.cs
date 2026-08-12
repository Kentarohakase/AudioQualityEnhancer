using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioQualityThresholdsTests
{
    [Theory]
    [InlineData(true, 1, 95_000, true)]
    [InlineData(true, 1, 96_000, false)]
    [InlineData(true, 2, 127_000, true)]
    [InlineData(true, 2, 128_000, false)]
    [InlineData(true, 2, 96_000, true)]
    [InlineData(false, 2, 64_000, false)]
    [InlineData(true, 2, 0, false)]
    public void HasLowBitrate_UsesAChannelDependentThreshold(bool isLossy, int channels, int bitRate, bool expected)
    {
        using var info = new AudioInfo
        {
            IsLikelyLossy = isLossy,
            Channels = channels,
            BitRate = bitRate
        };

        Assert.Equal(expected, AudioQualityThresholds.HasLowBitrate(info));
    }

    /// <summary>A lossless source is never judged by its bitrate.</summary>
    [Fact]
    public void HasLowBitrate_IgnoresALosslessSource()
    {
        using var info = new AudioInfo { IsLikelyLossy = false, Channels = 2, BitRate = 1_000 };

        Assert.False(AudioQualityThresholds.HasLowBitrate(info));
    }

    [Fact]
    public void HasLowBitrate_IsFalseWithoutInfo()
    {
        Assert.False(AudioQualityThresholds.HasLowBitrate(null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(22_050, true)]
    [InlineData(31_999, true)]
    [InlineData(32_000, false)]
    [InlineData(48_000, false)]
    public void HasLowSampleRate_UsesTheDocumentedBoundary(int sampleRate, bool expected)
    {
        using var info = new AudioInfo { SampleRate = sampleRate };

        Assert.Equal(expected, AudioQualityThresholds.HasLowSampleRate(info));
    }

    [Theory]
    [InlineData(-2.0, false)]
    [InlineData(-1.1, false)]
    [InlineData(-1.0, true)]
    [InlineData(-0.5, true)]
    public void HasLowHeadroom_UsesTheDocumentedBoundary(double truePeakDb, bool expected)
    {
        using var diagnostics = new AudioDiagnostics { TruePeakDb = truePeakDb };

        Assert.Equal(expected, AudioQualityThresholds.HasLowHeadroom(diagnostics));
    }

    [Fact]
    public void HasLowHeadroom_FallsBackToTheMeasuredMaximum()
    {
        using var diagnostics = new AudioDiagnostics { TruePeakDb = null, MaxVolumeDb = -0.2 };

        Assert.True(AudioQualityThresholds.HasLowHeadroom(diagnostics));
    }

    /// <summary>
    /// Clipping is reported on its own, so it must not also count as low headroom or the
    /// same problem would appear twice in the findings.
    /// </summary>
    [Fact]
    public void HasLowHeadroom_ExcludesActualClipping()
    {
        using var diagnostics = new AudioDiagnostics { TruePeakDb = 0.5, MaxVolumeDb = 0.5 };

        Assert.True(AudioQualityThresholds.HasPotentialClipping(diagnostics));
        Assert.False(AudioQualityThresholds.HasLowHeadroom(diagnostics));
    }

    [Theory]
    [InlineData(-30.0, true, false)]
    [InlineData(-28.0, false, false)]
    [InlineData(-16.0, false, false)]
    [InlineData(-9.0, false, false)]
    [InlineData(-5.0, false, true)]
    public void LoudnessBands_DoNotOverlap(double lufs, bool veryQuiet, bool alreadyLoud)
    {
        using var diagnostics = new AudioDiagnostics { IntegratedLoudnessLufs = lufs };

        Assert.Equal(veryQuiet, AudioQualityThresholds.IsVeryQuiet(diagnostics));
        Assert.Equal(alreadyLoud, AudioQualityThresholds.IsAlreadyLoud(diagnostics));
        Assert.Equal(veryQuiet || alreadyLoud, AudioQualityThresholds.HasProblematicLoudness(diagnostics));
    }

    [Fact]
    public void AllChecks_AreFalseWithoutDiagnostics()
    {
        Assert.False(AudioQualityThresholds.HasPotentialClipping(null));
        Assert.False(AudioQualityThresholds.HasLowHeadroom(null));
        Assert.False(AudioQualityThresholds.IsVeryQuiet(null));
        Assert.False(AudioQualityThresholds.IsAlreadyLoud(null));
        Assert.False(AudioQualityThresholds.HasProblematicLoudness(null));
    }
}
