using System.Globalization;
using System.Text.RegularExpressions;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

// Downloads the best audio stream of a single URL with yt-dlp, remuxing (without
// re-encoding) into a container the app supports, then leaves it to the normal
// pipeline to normalize and export.
public sealed partial class YtDlpDownloadService
{
    internal const string ToolName = "yt-dlp";

    // yt-dlp prints periodic progress, so this much silence means a stuck download
    // (e.g. a dropped connection) rather than normal slow progress.
    internal static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(24);

    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly FileNameService _fileNameService;
    private readonly IProcessRunner _processRunner;

    public YtDlpDownloadService(ToolDiscoveryService toolDiscoveryService, FileNameService fileNameService)
        : this(toolDiscoveryService, fileNameService, new ProcessRunner())
    {
    }

    internal YtDlpDownloadService(
        ToolDiscoveryService toolDiscoveryService,
        FileNameService fileNameService,
        IProcessRunner processRunner)
    {
        _toolDiscoveryService = toolDiscoveryService;
        _fileNameService = fileNameService;
        _processRunner = processRunner;
    }

    public async Task<Result> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var status = await _toolDiscoveryService.GetStatusAsync(ToolName, cancellationToken, "--version");
        return status.IsAvailable
            ? Result.Success()
            : Result.Failure(status.ErrorMessage ?? LocalizationService.Instance["Error_YtDlpUnavailable"]);
    }

    public async Task<Result<string>> DownloadAsync(
        string url,
        string targetDirectory,
        Action<string>? log,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsLikelyValidUrl(url))
        {
            return Result<string>.Failure(LocalizationService.Instance["Error_DownloadInvalidUrl"]);
        }

        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (availability.IsFailure)
        {
            return Result<string>.Failure(availability.ErrorMessage ?? LocalizationService.Instance["Error_YtDlpUnavailable"]);
        }

        string ffmpegDirectory;
        try
        {
            ffmpegDirectory = Path.GetDirectoryName(_toolDiscoveryService.ResolveExecutable("ffmpeg")) ?? AppContext.BaseDirectory;
        }
        catch
        {
            ffmpegDirectory = AppContext.BaseDirectory;
        }

        Directory.CreateDirectory(targetDirectory);
        var outputTemplate = Path.Combine(targetDirectory, "%(title).150B [%(id)s].%(ext)s");
        var arguments = BuildArguments(url.Trim(), ffmpegDirectory, outputTemplate);
        var capturedPaths = new List<string>();

        try
        {
            log?.Invoke(LocalizationService.Instance.Format("Log_DownloadStartedFormat", url.Trim()));
            var result = await _processRunner.RunAsync(
                new ProcessRunOptions(
                    _toolDiscoveryService.ResolveExecutable(ToolName),
                    arguments,
                    line =>
                    {
                        if (TryCaptureFilePath(line, capturedPaths))
                        {
                            return;
                        }

                        if (!TryReportProgress(line, progress))
                        {
                            log?.Invoke(line);
                        }
                    },
                    line => log?.Invoke(line),
                    DefaultInactivityTimeout),
                cancellationToken);

            if (result.TimedOut)
            {
                return Result<string>.Failure(LocalizationService.Instance["Error_YtDlpTimeout"]);
            }

            if (result.WasCancelled)
            {
                return Result<string>.Failure(LocalizationService.Instance["Error_ProcessingCancelled"]);
            }

            if (result.ExitCode != 0)
            {
                return Result<string>.Failure(CreateExitErrorMessage(result));
            }

            var downloadedFile = ResolveDownloadedFile(capturedPaths, targetDirectory);
            if (downloadedFile is null)
            {
                return Result<string>.Failure(LocalizationService.Instance["Error_DownloadFileMissing"]);
            }

            progress?.Invoke(100);
            return Result<string>.Success(downloadedFile);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<string>.Failure(LocalizationService.Instance["Error_YtDlpUnavailable"], ex);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(LocalizationService.Instance["Error_DownloadFailed"], ex);
        }
    }

    /// <summary>
    /// Ensures a writable per-user copy of yt-dlp exists (the bundled app folder is
    /// read-only under asInvoker) and, when due, self-updates it. Best effort: failures
    /// are logged but never surfaced. Returns the new check time, or null if not checked.
    /// </summary>
    public async Task<DateTimeOffset?> PrepareAsync(
        bool autoUpdate,
        DateTimeOffset? lastCheckUtc,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        TryEnsureUserCopy();

        if (!autoUpdate)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (lastCheckUtc.HasValue && now - lastCheckUtc.Value < UpdateCheckInterval)
        {
            return null;
        }

        var status = await _toolDiscoveryService.GetStatusAsync(ToolName, cancellationToken, "--version");
        if (!status.IsAvailable)
        {
            return null;
        }

        await TryUpdateAsync(log, cancellationToken);
        return now;
    }

    internal static IReadOnlyList<string> BuildArguments(string url, string ffmpegDirectory, string outputTemplate)
    {
        return new List<string>
        {
            "--no-playlist",
            "-f",
            "bestaudio/best",
            "--remux-video",
            "ogg/m4a/mka",
            "--ffmpeg-location",
            ffmpegDirectory,
            "--embed-metadata",
            "--no-part",
            "--newline",
            "--no-simulate",
            "--print",
            "after_move:filepath",
            "-o",
            outputTemplate,
            url
        };
    }

    internal static bool IsLikelyValidUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    internal static string CreateExitErrorMessage(ProcessResult result)
    {
        var detail = ResolveFriendlyErrorDetail(result.StandardError + Environment.NewLine + result.StandardOutput);
        var baseMessage = LocalizationService.Instance.Format("Error_DownloadFailedFormat", result.ExitCode);
        return string.IsNullOrWhiteSpace(detail)
            ? baseMessage
            : $"{baseMessage} {detail}";
    }

    private static string ResolveFriendlyErrorDetail(string output)
    {
        if (ContainsAny(output, "getaddrinfo", "Failed to resolve", "Temporary failure in name resolution", "Unable to download webpage", "Connection refused", "Connection timed out"))
        {
            return LocalizationService.Instance["Error_DownloadNoNetwork"];
        }

        if (ContainsAny(output, "Video unavailable", "Private video", "members-only", "This video is", "Sign in to confirm", "removed by the user", "is not available", "Premieres in"))
        {
            return LocalizationService.Instance["Error_DownloadUnavailable"];
        }

        if (ContainsAny(output, "nsig extraction failed", "Signature extraction failed", "Please report this issue", "Unable to extract", "player response"))
        {
            return LocalizationService.Instance["Error_YtDlpOutdated"];
        }

        return string.Empty;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryCaptureFilePath(string line, List<string> capturedPaths)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || !Path.IsPathRooted(trimmed))
        {
            return false;
        }

        try
        {
            if (File.Exists(trimmed))
            {
                capturedPaths.Add(trimmed);
                return true;
            }
        }
        catch
        {
            // Not a usable path; fall through and let it be logged.
        }

        return false;
    }

    private static bool TryReportProgress(string line, Action<double>? progress)
    {
        if (progress is null)
        {
            return false;
        }

        var match = ProgressRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            progress(Math.Clamp(percent, 0d, 99.5d));
        }

        return true;
    }

    private string? ResolveDownloadedFile(IReadOnlyList<string> capturedPaths, string targetDirectory)
    {
        for (var i = capturedPaths.Count - 1; i >= 0; i--)
        {
            if (File.Exists(capturedPaths[i]))
            {
                return capturedPaths[i];
            }
        }

        try
        {
            return Directory
                .EnumerateFiles(targetDirectory)
                .Where(_fileNameService.IsSupportedInputFile)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void TryEnsureUserCopy()
    {
        try
        {
            var userExe = Path.Combine(ToolDiscoveryService.GetUserToolsDirectory(), $"{ToolName}.exe");
            if (File.Exists(userExe))
            {
                return;
            }

            var bundled = FindBundledExecutable();
            if (bundled is null)
            {
                return;
            }

            Directory.CreateDirectory(ToolDiscoveryService.GetUserToolsDirectory());
            File.Copy(bundled, userExe, overwrite: false);
        }
        catch
        {
            // Preparing the writable copy is best effort; discovery falls back to the
            // bundled tool or PATH.
        }
    }

    private async Task TryUpdateAsync(Action<string>? log, CancellationToken cancellationToken)
    {
        try
        {
            log?.Invoke(LocalizationService.Instance["Log_YtDlpUpdating"]);
            await _processRunner.RunAsync(
                new ProcessRunOptions(
                    _toolDiscoveryService.ResolveExecutable(ToolName),
                    new[] { "-U" },
                    line => log?.Invoke(line),
                    line => log?.Invoke(line),
                    TimeSpan.FromMinutes(2)),
                cancellationToken);
        }
        catch
        {
            // A failed update never blocks usage; an outdated yt-dlp surfaces a clear
            // error only when a download actually fails.
        }
    }

    private static string? FindBundledExecutable()
    {
        var exeName = $"{ToolName}.exe";
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, exeName),
            Path.Combine(AppContext.BaseDirectory, "Tools", exeName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    [GeneratedRegex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressRegex();
}
