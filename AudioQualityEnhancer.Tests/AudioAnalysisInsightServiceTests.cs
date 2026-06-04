using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioAnalysisInsightServiceTests
{
    private readonly AudioAnalysisInsightService _service = new();

    [Fact]
    public void BuildReport_GoodLosslessSourceWithDiagnostics_ReturnsHighScore()
    {
        using var info = CreateInfo(codec: "flac", isLossy: false, bitRate: 900_000, sampleRate: 48_000);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -16, truePeak: -2.5, maxVolume: -2.4);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(100, report.Score);
        Assert.Equal(AudioAnalysisStatus.Excellent, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.NoIssues);
        Assert.Empty(report.Recommendations);
    }

    [Fact]
    public void BuildReport_LossyLowBitrateSource_AddsLossyAndBitrateFindings()
    {
        using var info = CreateInfo(codec: "mp3", isLossy: true, bitRate: 96_000, sampleRate: 44_100, channels: 2);

        var report = _service.BuildReport(info, diagnostics: null);

        Assert.Equal(80, report.Score);
        Assert.Equal(AudioAnalysisStatus.Caution, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.LossySource);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.LowBitrate && f.Severity == AudioInsightSeverity.Warning);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.AdvancedAnalysisRecommended);
    }

    [Fact]
    public void BuildReport_LowSampleRate_AddsSampleRateWarning()
    {
        using var info = CreateInfo(codec: "aac", isLossy: true, bitRate: 160_000, sampleRate: 22_050);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -18, truePeak: -3, maxVolume: -3);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(85, report.Score);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.LowSampleRate && f.Severity == AudioInsightSeverity.Warning);
    }

    [Fact]
    public void BuildReport_PotentialClippingFromTruePeak_AddsCriticalFinding()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 1_411_200, sampleRate: 44_100);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -14, truePeak: -0.05, maxVolume: -1.5);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(75, report.Score);
        Assert.Equal(AudioAnalysisStatus.Critical, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.PotentialClipping && f.Severity == AudioInsightSeverity.Critical);
    }

    [Fact]
    public void BuildReport_PotentialClippingFromSamplePeak_AddsCriticalFinding()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 1_411_200, sampleRate: 44_100);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -14, truePeak: -2, maxVolume: -0.05);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(AudioAnalysisStatus.Critical, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.PotentialClipping && f.Severity == AudioInsightSeverity.Critical);
    }

    [Fact]
    public void BuildReport_LowHeadroom_AddsWarningWithoutCriticalStatus()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 1_411_200, sampleRate: 44_100);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -14, truePeak: -0.5, maxVolume: -1.2);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(90, report.Score);
        Assert.Equal(AudioAnalysisStatus.Caution, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.LowHeadroom && f.Severity == AudioInsightSeverity.Warning);
    }

    [Fact]
    public void BuildReport_VeryQuietSource_AddsQuietRecommendation()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 1_411_200, sampleRate: 44_100);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -30, truePeak: -8, maxVolume: -8);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(90, report.Score);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.VeryQuiet);
        Assert.Contains(report.Recommendations, r => r.Kind == AudioAnalysisFindingKind.VeryQuiet);
    }

    [Fact]
    public void BuildReport_AlreadyLoudSource_AddsAlreadyLoudRecommendation()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 1_411_200, sampleRate: 44_100);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -8, truePeak: -2, maxVolume: -2);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(90, report.Score);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.AlreadyLoud);
        Assert.Contains(report.Recommendations, r => r.Kind == AudioAnalysisFindingKind.AlreadyLoud);
    }

    [Fact]
    public void BuildReport_MonoSource_AddsInformationalChannelFinding()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 768_000, sampleRate: 48_000, channels: 1);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -16, truePeak: -2, maxVolume: -2);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(100, report.Score);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.MonoSource && f.Severity == AudioInsightSeverity.Info);
        Assert.Contains(report.Recommendations, r => r.Kind == AudioAnalysisFindingKind.MonoSource);
    }

    [Fact]
    public void BuildReport_MultichannelSource_AddsWarningWithoutCriticalStatus()
    {
        using var info = CreateInfo(codec: "wav", isLossy: false, bitRate: 4_608_000, sampleRate: 48_000, channels: 6);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -18, truePeak: -3, maxVolume: -3);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Equal(95, report.Score);
        Assert.Equal(AudioAnalysisStatus.Caution, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.MultichannelSource && f.Severity == AudioInsightSeverity.Warning);
    }

    [Fact]
    public void BuildReport_LossySource_AddsTranscodingRiskGuidance()
    {
        using var info = CreateInfo(codec: "aac", isLossy: true, bitRate: 256_000, sampleRate: 48_000);
        using var diagnostics = CreateDiagnostics(integratedLoudness: -16, truePeak: -2, maxVolume: -2);

        var report = _service.BuildReport(info, diagnostics);

        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.LossyTranscodingRisk);
        Assert.Contains(report.Recommendations, r => r.Kind == AudioAnalysisFindingKind.LossyTranscodingRisk);
    }

    [Fact]
    public void BuildReport_MissingDiagnostics_AddsInfoOnlyWithoutScorePenalty()
    {
        using var info = CreateInfo(codec: "flac", isLossy: false, bitRate: 900_000, sampleRate: 48_000);

        var report = _service.BuildReport(info, diagnostics: null);

        Assert.Equal(100, report.Score);
        Assert.Equal(AudioAnalysisStatus.Good, report.Status);
        Assert.Contains(report.Findings, f => f.Kind == AudioAnalysisFindingKind.AdvancedAnalysisRecommended && f.Severity == AudioInsightSeverity.Info);
        Assert.Contains(report.Recommendations, r => r.Kind == AudioAnalysisFindingKind.AdvancedAnalysisRecommended);
    }

    private static AudioInfo CreateInfo(
        string codec,
        bool isLossy,
        long bitRate,
        int sampleRate,
        int channels = 2)
    {
        return new AudioInfo
        {
            Codec = codec,
            IsLikelyLossy = isLossy,
            BitRate = bitRate,
            SampleRate = sampleRate,
            Channels = channels,
            Duration = TimeSpan.FromMinutes(3),
            Container = codec,
            FileSizeBytes = 1024 * 1024
        };
    }

    private static AudioDiagnostics CreateDiagnostics(
        double integratedLoudness,
        double truePeak,
        double maxVolume)
    {
        return new AudioDiagnostics
        {
            IntegratedLoudnessLufs = integratedLoudness,
            TruePeakDb = truePeak,
            MaxVolumeDb = maxVolume,
            LoudnessRangeLu = 8,
            MeanVolumeDb = -20
        };
    }
}
