using System.Diagnostics;
using System.Globalization;
using System.Text;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FFmpegService
{
    private readonly ToolDiscoveryService _toolDiscoveryService;

    public FFmpegService(ToolDiscoveryService toolDiscoveryService)
    {
        _toolDiscoveryService = toolDiscoveryService;
    }

    public async Task<Result> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var status = await _toolDiscoveryService.GetStatusAsync("ffmpeg", cancellationToken);
        return status.IsAvailable
            ? Result.Success()
            : Result.Failure(status.ErrorMessage ?? LocalizationService.Instance["Error_FFmpegUnavailable"]);
    }

    public async Task<Result<ProcessResult>> ExecuteAsync(
        IReadOnlyList<string> arguments,
        Action<string>? log,
        Action<double>? progress,
        TimeSpan? totalDuration,
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

        foreach (var argument in arguments)
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

            if (!TryReportProgressFromProgressLine(e.Data, totalDuration, progress) && !IsProgressMetadataLine(e.Data))
            {
                log?.Invoke(e.Data);
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

            log?.Invoke(e.Data);
            TryReportProgressFromFFmpegText(e.Data, totalDuration, progress);
        };

        try
        {
            log?.Invoke(LocalizationService.Instance.Format("Log_FFmpegStartingFormat", FormatCommand(arguments)));
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

            if (process.ExitCode == 0)
            {
                progress?.Invoke(100);
                return Result<ProcessResult>.Success(result);
            }

            return Result<ProcessResult>.Failure(LocalizationService.Instance.Format("Error_FFmpegExitCodeFormat", process.ExitCode), value: result);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            var result = new ProcessResult(
                process.HasExited ? process.ExitCode : -1,
                stdout.ToString(),
                stderr.ToString(),
                DateTimeOffset.Now - startedAt,
                WasCancelled: true);

            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_ProcessingCancelled"], value: result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_FFmpegNotFound"], ex);
        }
        catch (Exception ex)
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_FFmpegStartFailed"], ex);
        }
    }

    private static string FormatCommand(IEnumerable<string> arguments)
    {
        return "ffmpeg " + string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
    }

    private static bool TryReportProgressFromProgressLine(string line, TimeSpan? totalDuration, Action<double>? progress)
    {
        if (progress is null)
        {
            return false;
        }

        if (line.StartsWith("progress=end", StringComparison.OrdinalIgnoreCase))
        {
            progress(100);
            return true;
        }

        if (!totalDuration.HasValue || totalDuration.Value.TotalSeconds <= 0)
        {
            return IsProgressMetadataLine(line);
        }

        if (line.StartsWith("out_time_ms=", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(line["out_time_ms=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
        {
            ReportPercent(TimeSpan.FromSeconds(microseconds / 1_000_000d), totalDuration.Value, progress);
            return true;
        }

        if (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase) &&
            TimeSpan.TryParse(line["out_time=".Length..], CultureInfo.InvariantCulture, out var outTime))
        {
            ReportPercent(outTime, totalDuration.Value, progress);
            return true;
        }

        return IsProgressMetadataLine(line);
    }

    private static void TryReportProgressFromFFmpegText(string line, TimeSpan? totalDuration, Action<double>? progress)
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
        var value = end > start ? line[start..end] : line[start..];

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var outTime))
        {
            ReportPercent(outTime, totalDuration.Value, progress);
        }
    }

    private static bool IsProgressMetadataLine(string line)
    {
        return line.StartsWith("frame=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("fps=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("stream_", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("bitrate=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("total_size=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("out_time_", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("dup_frames=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("drop_frames=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("speed=", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("progress=", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportPercent(TimeSpan current, TimeSpan total, Action<double> progress)
    {
        var percent = Math.Clamp(current.TotalSeconds / total.TotalSeconds * 100d, 0d, 99.5d);
        progress(percent);
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
}
