using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioDiagnosticsServiceTests
{
    [Fact]
    public void BuildArguments_UsesReadOnlyAnalysisPipeline()
    {
        var args = AudioDiagnosticsService.BuildArguments("input.mp3");

        Assert.Contains("-nostdin", args);
        Assert.Contains("0:a:0", args);
        Assert.Contains("ebur128=peak=true,volumedetect", args);
        Assert.Equal("NUL", args[^1]);
    }

    [Fact]
    public void ParseDiagnostics_ReadsEbur128AndVolumedetectValues()
    {
        var output = """
            [Parsed_ebur128_0 @ 000001] Summary:

              Integrated loudness:
                I:         -18.7 LUFS
                Threshold: -28.7 LUFS

              Loudness range:
                LRA:         8.2 LU
                Threshold: -38.3 LUFS

              True peak:
                Peak:       -1.2 dBFS

            [Parsed_volumedetect_1 @ 000001] mean_volume: -21.1 dB
            [Parsed_volumedetect_1 @ 000001] max_volume: -0.3 dB
            """;

        using var diagnostics = AudioDiagnosticsService.ParseDiagnostics(output);

        Assert.NotNull(diagnostics);
        Assert.Equal(-18.7, diagnostics.IntegratedLoudnessLufs);
        Assert.Equal(8.2, diagnostics.LoudnessRangeLu);
        Assert.Equal(-1.2, diagnostics.TruePeakDb);
        Assert.Equal(-0.3, diagnostics.MaxVolumeDb);
        Assert.Equal(-21.1, diagnostics.MeanVolumeDb);
    }

    [Fact]
    public void ParseDiagnostics_UsesLastSummarySection()
    {
        var output = """
            Summary:
              Integrated loudness:
                I:         -30.0 LUFS
              True peak:
                Peak:       -5.0 dBFS

            Summary:
              Integrated loudness:
                I:         -16.0 LUFS
              True peak:
                Peak:       -1.5 dBFS
            """;

        using var diagnostics = AudioDiagnosticsService.ParseDiagnostics(output);

        Assert.NotNull(diagnostics);
        Assert.Equal(-16.0, diagnostics.IntegratedLoudnessLufs);
        Assert.Equal(-1.5, diagnostics.TruePeakDb);
    }

    [Fact]
    public void ParseDiagnostics_ReturnsNullForUnrelatedOutput()
    {
        var diagnostics = AudioDiagnosticsService.ParseDiagnostics("ffmpeg output without measurement values");

        Assert.Null(diagnostics);
    }
}
