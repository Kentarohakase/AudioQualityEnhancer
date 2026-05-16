using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed partial class AudioDiagnosticsService
{
    private readonly ToolDiscoveryService _toolDiscoveryService;

    public AudioDiagnosticsService(ToolDiscoveryService toolDiscoveryService)
    {
        _toolDiscoveryService = toolDiscoveryService;
    }

    public async Task<Result<AudioDiagnostics>> AnalyzeAsync(
        string inputPath,
        TimeSpan? totalDuration,
        Action<string>? log,
        Action<double>? progress,
        CancellationToken cancellationToken)
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

        var result = await RunAnalysisProcessAsync(inputPath, totalDuration, progress, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _toolDiscoveryService.ResolveExecutable("ffmpeg"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(inputPath))
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = DateTimeOffset.Now;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputClosed.TrySetResult();
                return;
            }

            lock (stdout)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorClosed.TrySetResult();
                return;
            }

            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }

            TryReportProgress(e.Data, totalDuration, progress);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputClosed.Task, errorClosed.Task);

            var result = new ProcessResult(
                process.ExitCode,
                stdout.ToString(),
                stderr.ToString(),
                DateTimeOffset.Now - startedAt);

            return process.ExitCode == 0
                ? Result<ProcessResult>.Success(result)
                : Result<ProcessResult>.Failure(LocalizationService.Instance.Format("Error_FFmpegExitCodeFormat", process.ExitCode), value: result);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_AnalysisCancelled"]);
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

    private static IReadOnlyList<string> BuildArguments(string inputPath)
    {
        return new[]
        {
            "-hide_banner",
            "-nostdin",
            "-i",
            inputPath,
            "-map",
            "0:a:0",
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

    private static AudioDiagnostics? ParseDiagnostics(string output)
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after cancellation.
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
