using System.Globalization;
using System.Text.RegularExpressions;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed partial class AudioDiagnosticsService
{
    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly IProcessRunner _processRunner;

    public AudioDiagnosticsService(ToolDiscoveryService toolDiscoveryService)
        : this(toolDiscoveryService, new ProcessRunner())
    {
    }

    internal AudioDiagnosticsService(ToolDiscoveryService toolDiscoveryService, IProcessRunner processRunner)
    {
        _toolDiscoveryService = toolDiscoveryService;
        _processRunner = processRunner;
    }

    public async Task<Result<AudioDiagnostics>> AnalyzeAsync(
        string inputPath,
        TimeSpan? totalDuration,
        Action<string>? log,
        Action<double>? progress,
        CancellationToken cancellationToken,
        AudioStreamInfo? audioStream = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return Result<AudioDiagnostics>.Failure(LocalizationService.Instance["Error_SourceFileNotFound"]);
        }

        ToolStatus status;
        try
        {
            status = await _toolDiscoveryService.GetStatusAsync("ffmpeg", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Result<AudioDiagnostics>.Failure(LocalizationService.Instance["Error_AnalysisCancelled"]);
        }

        if (!status.IsAvailable)
        {
            return Result<AudioDiagnostics>.Failure(status.ErrorMessage ?? LocalizationService.Instance["Error_FFmpegUnavailable"]);
        }

        log?.Invoke(LocalizationService.Instance["Log_AdvancedAnalysisStarting"]);

        var result = await RunAnalysisProcessAsync(inputPath, totalDuration, progress, cancellationToken, audioStream);
        if (result.IsFailure || result.Value is null)
        {
            return Result<AudioDiagnostics>.Failure(result.ErrorMessage ?? LocalizationService.Instance["Error_DiagnosticsFailed"], result.Exception);
        }

        var output = result.Value.StandardError + Environment.NewLine + result.Value.StandardOutput;
        var diagnostics = ParseDiagnostics(output);
        if (diagnostics is null)
        {
            return Result<AudioDiagnostics>.Failure(LocalizationService.Instance["Error_DiagnosticsUnreadable"]);
        }

        log?.Invoke(LocalizationService.Instance.Format("Log_AdvancedAnalysisCompleteFormat", diagnostics.IntegratedLoudnessDisplay, diagnostics.TruePeakDisplay));
        return Result<AudioDiagnostics>.Success(diagnostics);
    }

    private async Task<Result<ProcessResult>> RunAnalysisProcessAsync(
        string inputPath,
        TimeSpan? totalDuration,
        Action<double>? progress,
        CancellationToken cancellationToken,
        AudioStreamInfo? audioStream)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessRunOptions(
                    _toolDiscoveryService.ResolveExecutable("ffmpeg"),
                    BuildArguments(inputPath, audioStream),
                    StandardErrorLine: line => TryReportProgress(line, totalDuration, progress),
                    InactivityTimeout: FFmpegService.DefaultInactivityTimeout),
                cancellationToken);

            if (result.TimedOut)
            {
                return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_FFmpegTimeout"], value: result);
            }

            if (result.WasCancelled)
            {
                return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_AnalysisCancelled"], value: result);
            }

            return result.ExitCode == 0
                ? Result<ProcessResult>.Success(result)
                : Result<ProcessResult>.Failure(LocalizationService.Instance.Format("Error_FFmpegExitCodeFormat", result.ExitCode), value: result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_FFmpegNotFound"], ex);
        }
        catch (Exception ex)
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_DiagnosticsFailed"], ex);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string inputPath, AudioStreamInfo? audioStream = null)
    {
        return new[]
        {
            "-hide_banner",
            "-nostdin",
            "-i",
            inputPath,
            "-map",
            audioStream?.FFmpegMapSpecifier ?? "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-af",
            "ebur128=peak=true,volumedetect",
            "-f",
            "null",
            "NUL"
        };
    }

    internal static AudioDiagnostics? ParseDiagnostics(string output)
    {
        var summary = LastSection(output, "Summary:") ?? output;
        var integrated = TryMatchDouble(summary, IntegratedLoudnessRegex());
        var lra = TryMatchDouble(summary, LoudnessRangeRegex());
        var truePeak = TryMatchDouble(summary, TruePeakRegex());
        var maxVolume = TryMatchDouble(output, MaxVolumeRegex());
        var meanVolume = TryMatchDouble(output, MeanVolumeRegex());

        if (integrated is null &&
            lra is null &&
            truePeak is null &&
            maxVolume is null &&
            meanVolume is null)
        {
            return null;
        }

        return new AudioDiagnostics
        {
            IntegratedLoudnessLufs = integrated,
            LoudnessRangeLu = lra,
            TruePeakDb = truePeak,
            MaxVolumeDb = maxVolume,
            MeanVolumeDb = meanVolume
        };
    }

    private static string? LastSection(string value, string marker)
    {
        var index = value.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? value[index..] : null;
    }

    private static double? TryMatchDouble(string value, Regex regex)
    {
        var match = regex.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups["value"].Value;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static void TryReportProgress(string line, TimeSpan? totalDuration, Action<double>? progress)
    {
        if (progress is null || !totalDuration.HasValue || totalDuration.Value.TotalSeconds <= 0)
        {
            return;
        }

        const string marker = "time=";
        var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var start = index + marker.Length;
        var end = line.IndexOf(' ', start);
        var raw = end > start ? line[start..end] : line[start..];

        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var elapsed))
        {
            progress(Math.Clamp(elapsed.TotalSeconds / totalDuration.Value.TotalSeconds * 100d, 0, 99.5));
        }
    }

    [GeneratedRegex(@"Integrated loudness:\s*(?:\r?\n\s*.+)*?\r?\n\s*I:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*LUFS", RegexOptions.IgnoreCase)]
    private static partial Regex IntegratedLoudnessRegex();

    [GeneratedRegex(@"Loudness range:\s*(?:\r?\n\s*.+)*?\r?\n\s*LRA:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*LU", RegexOptions.IgnoreCase)]
    private static partial Regex LoudnessRangeRegex();

    [GeneratedRegex(@"True peak:\s*(?:\r?\n\s*.+)*?\r?\n\s*Peak:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*dBFS", RegexOptions.IgnoreCase)]
    private static partial Regex TruePeakRegex();

    [GeneratedRegex(@"max_volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex MaxVolumeRegex();

    [GeneratedRegex(@"mean_volume:\s*(?<value>[-+]?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex MeanVolumeRegex();
}
