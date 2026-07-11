using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioValidationServiceTests
{
    [Fact]
    public void BuildReport_GoodLosslessOutputPasses()
    {
        using var source = CreateInfo("flac", isLossy: false);
        using var output = CreateInfo("flac", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14.2,
            TruePeakDb = -1.5,
            MaxVolumeDb = -1.6
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.flac");

        Assert.Equal(AudioComparisonStatus.Passed, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.NoIssues);
        Assert.DoesNotContain(report.Findings, finding => finding.Severity != AudioInsightSeverity.Info);
    }

    [Fact]
    public void BuildReport_ClippingOutputIsCritical()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("wav", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -0.05
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Wav24),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.wav");

        Assert.Equal(AudioComparisonStatus.Critical, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.PotentialClipping);
    }

    [Fact]
    public void BuildReport_LowHeadroomOutputIsWarning()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("wav", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -0.5
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Wav24),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.wav");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.LowHeadroom);
    }

    [Fact]
    public void CreateValidationResult_CriticalReportFailsAndPreservesReport()
    {
        var report = CreateComparisonReport(AudioComparisonStatus.Critical);

        var result = AudioValidationService.CreateValidationResult(report);

        Assert.True(result.IsFailure);
        Assert.Same(report, result.Value);
        Assert.Equal(report.Summary, result.ErrorMessage);
    }

    [Fact]
    public void CreateValidationResult_WarningReportSucceedsAndPreservesReport()
    {
        var report = CreateComparisonReport(AudioComparisonStatus.Warning);

        var result = AudioValidationService.CreateValidationResult(report);

        Assert.True(result.IsSuccess);
        Assert.Same(report, result.Value);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void BuildReport_DurationMismatchIsCriticalWhenLarge()
    {
        using var source = CreateInfo("flac", isLossy: false, duration: TimeSpan.FromSeconds(60));
        using var output = CreateInfo("flac", isLossy: false, duration: TimeSpan.FromSeconds(54));
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -1.5
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.flac");

        Assert.Equal(AudioComparisonStatus.Critical, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.DurationMismatch);
    }

    [Fact]
    public void BuildReport_CodecMismatchCreatesWarning()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("aac", isLossy: true);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -2
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.flac");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.CodecMismatch);
    }

    [Fact]
    public void BuildReport_PremiereProfileRequires48Khz()
    {
        using var source = CreateInfo("wav", isLossy: false, sampleRate: 48_000);
        using var output = CreateInfo("pcm_s24le", isLossy: false, sampleRate: 44_100);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -2
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.PremierePro),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\clip_music_premiere_pro.wav");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.SampleRateMismatch);
    }

    [Fact]
    public void BuildReport_ChannelCountChangeCreatesWarning()
    {
        using var source = CreateInfo("wav", isLossy: false, channels: 6);
        using var output = CreateInfo("flac", isLossy: false, channels: 2);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -2
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\surround_music.flac");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.ChannelCountChanged);
    }

    [Fact]
    public void BuildReport_LoudnessTargetMissCreatesWarning()
    {
        using var source = CreateInfo("flac", isLossy: false);
        using var output = CreateInfo("flac", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -20,
            TruePeakDb = -2
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Speech, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\voice_speech.flac");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.LoudnessOffTarget);
    }

    [Fact]
    public void BuildReport_PodcastVoiceUsesSpeechLoudnessTarget()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("wav", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -20,
            TruePeakDb = -2
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.PodcastVoice, ExportFormat.Wav24),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\voice_podcast_voice.wav");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.LoudnessOffTarget);
    }

    [Fact]
    public void ValidateOutputFile_RejectsMissingAndEmptyFiles()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var missing = Path.Combine(tempDirectory, "missing.flac");
            var empty = Path.Combine(tempDirectory, "empty.flac");
            File.WriteAllBytes(empty, Array.Empty<byte>());

            Assert.True(AudioValidationService.ValidateOutputFile(missing).IsFailure);
            Assert.True(AudioValidationService.ValidateOutputFile(empty).IsFailure);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildFileProblemReport_UsesCriticalStatus()
    {
        using var source = CreateInfo("wav", isLossy: false);

        var report = AudioValidationService.BuildFileProblemReport(source, string.Empty, "missing");

        Assert.Equal(AudioComparisonStatus.Critical, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.OutputMissing);
    }

    [Fact]
    public void BuildReport_LossySourceToLosslessFormatStaysInformational()
    {
        using var source = CreateInfo("mp3", isLossy: true);
        using var output = CreateInfo("flac", isLossy: false);
        using var diagnostics = new AudioDiagnostics
        {
            IntegratedLoudnessLufs = -14,
            TruePeakDb = -1.5
        };

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            diagnostics,
            outputDiagnosticsSkipped: false,
            outputPath: @"C:\audio\song_music.flac");

        Assert.Equal(AudioComparisonStatus.Passed, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.LossyToLossless);
    }

    [Fact]
    public void BuildReport_MissingOutputDiagnosticsForLoudnessPresetCreatesWarning()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("flac", isLossy: false);

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.Music, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            outputDiagnostics: null,
            outputDiagnosticsSkipped: true,
            outputPath: @"C:\audio\song_music.flac");

        Assert.Equal(AudioComparisonStatus.Warning, report.Status);
        Assert.Contains(report.Findings, finding =>
            finding.Kind == AudioComparisonFindingKind.OutputDiagnosticsMissing &&
            finding.Severity == AudioInsightSeverity.Warning);
    }

    [Fact]
    public void BuildReport_MissingOutputDiagnosticsForNeutralPresetStaysInformational()
    {
        using var source = CreateInfo("wav", isLossy: false);
        using var output = CreateInfo("flac", isLossy: false);

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.NoiseReduction, ExportFormat.Flac),
            source,
            output,
            sourceDiagnostics: null,
            outputDiagnostics: null,
            outputDiagnosticsSkipped: true,
            outputPath: @"C:\audio\noise.flac");

        Assert.Equal(AudioComparisonStatus.Passed, report.Status);
        Assert.Contains(report.Findings, finding =>
            finding.Kind == AudioComparisonFindingKind.OutputDiagnosticsMissing &&
            finding.Severity == AudioInsightSeverity.Info);
    }

    [Fact]
    public void BuildReport_StreamCopyUsesMetadataOnlyFinding()
    {
        using var source = CreateInfo("aac", isLossy: true);
        using var output = CreateInfo("aac", isLossy: true);

        var report = AudioValidationService.BuildReport(
            CreateOptions(source, AudioPreset.ExtractCopy, ExportFormat.Aac_256),
            source,
            output,
            sourceDiagnostics: null,
            outputDiagnostics: null,
            outputDiagnosticsSkipped: true,
            outputPath: @"C:\audio\clip_extracted.m4a");

        Assert.Equal(AudioComparisonStatus.Passed, report.Status);
        Assert.Contains(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.StreamCopyMetadataOnly);
        Assert.DoesNotContain(report.Findings, finding => finding.Kind == AudioComparisonFindingKind.OutputDiagnosticsMissing);
    }

    private static ProcessingOptions CreateOptions(AudioInfo sourceInfo, AudioPreset preset, ExportFormat exportFormat)
    {
        return new ProcessingOptions
        {
            SourceInfo = sourceInfo,
            Preset = preset,
            ExportFormat = exportFormat
        };
    }

    private static AudioComparisonReport CreateComparisonReport(AudioComparisonStatus status)
    {
        return new AudioComparisonReport(
            status,
            status.ToString(),
            $"{status} summary",
            @"C:\audio\output.flac",
            outputInfo: null,
            outputDiagnostics: null,
            findings: Array.Empty<AudioComparisonFinding>(),
            metrics: Array.Empty<AudioComparisonMetric>(),
            outputDiagnosticsSkipped: false);
    }

    private static AudioInfo CreateInfo(
        string codec,
        bool isLossy,
        TimeSpan? duration = null,
        int sampleRate = 48_000,
        int channels = 2)
    {
        return new AudioInfo
        {
            SourcePath = $@"C:\audio\sample.{codec}",
            Codec = codec,
            CodecLongName = codec.ToUpperInvariant(),
            BitRate = isLossy ? 128_000 : 900_000,
            SampleRate = sampleRate,
            Channels = channels,
            Duration = duration ?? TimeSpan.FromSeconds(60),
            Container = codec,
            FileSizeBytes = 1_000_000,
            IsLikelyLossy = isLossy
        };
    }
}
