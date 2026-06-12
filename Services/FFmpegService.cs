using System.Globalization;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FFmpegService
{
    // FFmpeg emits progress output at least every 0.5 seconds, so this much
    // silence indicates a stuck process rather than a long-running encode.
    internal static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromMinutes(2);

    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly IProcessRunner _processRunner;

    public FFmpegService(ToolDiscoveryService toolDiscoveryService)
        : this(toolDiscoveryService, new ProcessRunner())
    {
    }

    internal FFmpegService(ToolDiscoveryService toolDiscoveryService, IProcessRunner processRunner)
    {
        _toolDiscoveryService = toolDiscoveryService;
        _processRunner = processRunner;
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
        try
        {
            log?.Invoke(LocalizationService.Instance.Format("Log_FFmpegStartingFormat", FormatCommand(arguments)));
            var result = await _processRunner.RunAsync(
                new ProcessRunOptions(
                    _toolDiscoveryService.ResolveExecutable("ffmpeg"),
                    arguments,
                    line =>
                    {
                        if (!TryReportProgressFromProgressLine(line, totalDuration, progress) && !IsProgressMetadataLine(line))
                        {
                            log?.Invoke(line);
                        }
                    },
                    line =>
                    {
                        log?.Invoke(line);
                        TryReportProgressFromFFmpegText(line, totalDuration, progress);
                    },
                    DefaultInactivityTimeout),
                cancellationToken);

            if (result.TimedOut)
            {
                return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_FFmpegTimeout"], value: result);
            }

            if (result.WasCancelled)
            {
                return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_ProcessingCancelled"], value: result);
            }

            if (result.ExitCode == 0)
            {
                progress?.Invoke(100);
                return Result<ProcessResult>.Success(result);
            }

            return Result<ProcessResult>.Failure(CreateExitErrorMessage(result), value: result);
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

    internal static string FormatCommand(IEnumerable<string> arguments)
    {
        return "ffmpeg " + string.Join(" ", arguments.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace) || value.Contains('"', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    internal static string CreateExitErrorMessage(ProcessResult result)
    {
        var detail = ResolveFriendlyErrorDetail(result.StandardError + Environment.NewLine + result.StandardOutput);
        var baseMessage = LocalizationService.Instance.Format("Error_FFmpegExitCodeFormat", result.ExitCode);
        return string.IsNullOrWhiteSpace(detail)
            ? baseMessage
            : $"{baseMessage} {detail}";
    }

    private static string ResolveFriendlyErrorDetail(string output)
    {
        if (ContainsAny(output, "Permission denied", "Access is denied", "Device or resource busy"))
        {
            return LocalizationService.Instance["Error_FFmpegOutputLocked"];
        }

        if (ContainsAny(output, "No such file or directory", "Cannot open file", "Failed to open"))
        {
            return LocalizationService.Instance["Error_FFmpegPathUnavailable"];
        }

        if (ContainsAny(output, "Unknown encoder", "Encoder not found", "Unknown decoder", "Decoder not found"))
        {
            return LocalizationService.Instance["Error_FFmpegCodecUnavailable"];
        }

        if (ContainsAny(output, "Invalid argument", "Unable to find a suitable output format"))
        {
            return LocalizationService.Instance["Error_FFmpegInvalidArguments"];
        }

        if (ContainsAny(output, "Invalid data found", "could not find codec parameters", "moov atom not found"))
        {
            return LocalizationService.Instance["Error_FFmpegInputUnreadable"];
        }

        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
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

}
