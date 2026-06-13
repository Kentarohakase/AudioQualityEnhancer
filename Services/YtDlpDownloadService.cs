using System.Globalization;
using System.Text.RegularExpressions;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed record YtDlpDownloadRequest(
    string Url,
    string FfmpegDirectory,
    string OutputTemplate,
    string? ChapterTemplate,
    bool SplitChapters,
    bool RemoveSponsorSegments,
    bool DownloadPlaylist = false);

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

    public async Task<Result<IReadOnlyList<string>>> DownloadAsync(
        string url,
        string targetDirectory,
        bool splitChapters,
        bool removeSponsorSegments,
        bool downloadPlaylist,
        Action<string>? log,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsLikelyValidUrl(url))
        {
            return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_DownloadInvalidUrl"]);
        }

        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (availability.IsFailure)
        {
            return Result<IReadOnlyList<string>>.Failure(availability.ErrorMessage ?? LocalizationService.Instance["Error_YtDlpUnavailable"]);
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

        // Download into an isolated working folder so the produced files (one, or many
        // when splitting chapters) are trivial to collect, then move them into place.
        var workDirectory = Path.Combine(targetDirectory, $"{FileNameService.TemporaryFilePrefix}dl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        var request = new YtDlpDownloadRequest(
            url.Trim(),
            ffmpegDirectory,
            Path.Combine(workDirectory, "%(title).150B [%(id)s].%(ext)s"),
            Path.Combine(workDirectory, "chapters", "%(section_number)03d - %(section_title).100B.%(ext)s"),
            splitChapters,
            removeSponsorSegments,
            downloadPlaylist);
        var arguments = BuildArguments(request);

        try
        {
            log?.Invoke(LocalizationService.Instance.Format("Log_DownloadStartedFormat", url.Trim()));
            var result = await _processRunner.RunAsync(
                new ProcessRunOptions(
                    _toolDiscoveryService.ResolveExecutable(ToolName),
                    arguments,
                    line =>
                    {
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
                return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_YtDlpTimeout"]);
            }

            if (result.WasCancelled)
            {
                return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_ProcessingCancelled"]);
            }

            if (result.ExitCode != 0)
            {
                return Result<IReadOnlyList<string>>.Failure(CreateExitErrorMessage(result));
            }

            var files = CollectDownloadedFiles(workDirectory, splitChapters, targetDirectory);
            if (files.Count == 0)
            {
                return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_DownloadFileMissing"]);
            }

            progress?.Invoke(100);
            return Result<IReadOnlyList<string>>.Success(files);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_YtDlpUnavailable"], ex);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure(LocalizationService.Instance["Error_DownloadFailed"], ex);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
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

    internal static IReadOnlyList<string> BuildArguments(YtDlpDownloadRequest request)
    {
        var args = new List<string>();

        if (request.DownloadPlaylist)
        {
            args.Add("--yes-playlist");
            args.Add("--playlist-end");
            args.Add("100");
        }
        else
        {
            args.Add("--no-playlist");
        }

        args.AddRange(new[]
        {
            "-f",
            "bestaudio/best",
            "--remux-video",
            "ogg/m4a/mka",
            "--ffmpeg-location",
            request.FfmpegDirectory,
            "--embed-metadata",
            "--embed-thumbnail",
            "--no-part",
            "--newline"
        });

        if (request.RemoveSponsorSegments)
        {
            args.Add("--sponsorblock-remove");
            args.Add("default");
        }

        // Chapter splitting and playlist mode don't combine cleanly; playlist wins.
        if (request.SplitChapters && !request.DownloadPlaylist)
        {
            args.Add("--split-chapters");
            if (!string.IsNullOrWhiteSpace(request.ChapterTemplate))
            {
                args.Add("-o");
                args.Add($"chapter:{request.ChapterTemplate}");
            }
        }

        args.Add("-o");
        args.Add(request.OutputTemplate);
        args.Add(request.Url);
        return args;
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

    private IReadOnlyList<string> CollectDownloadedFiles(string workDirectory, bool splitChapters, string targetDirectory)
    {
        var chapterDirectory = Path.Combine(workDirectory, "chapters");

        // When splitting produced chapter files, take those; otherwise (no chapters in
        // the source, or splitting off) take the single full-length file.
        var sourceFiles = splitChapters
            && Directory.Exists(chapterDirectory)
            && Directory.EnumerateFiles(chapterDirectory).Any(_fileNameService.IsSupportedInputFile)
            ? Directory.EnumerateFiles(chapterDirectory)
            : Directory.EnumerateFiles(workDirectory);

        var moved = new List<string>();
        foreach (var file in sourceFiles
            .Where(_fileNameService.IsSupportedInputFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var destination = MoveIntoDirectory(file, targetDirectory);
            if (destination is not null)
            {
                moved.Add(destination);
            }
        }

        return moved;
    }

    private static string? MoveIntoDirectory(string sourceFile, string targetDirectory)
    {
        try
        {
            var fileName = Path.GetFileName(sourceFile);
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var destination = Path.Combine(targetDirectory, fileName);

            var index = 1;
            while (File.Exists(destination))
            {
                destination = Path.Combine(targetDirectory, $"{baseName} ({index}){extension}");
                index++;
            }

            File.Move(sourceFile, destination);
            return destination;
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

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Leftover working folders are cleaned up on a later run; never fail here.
        }
    }

    [GeneratedRegex(@"\[download\]\s+(\d{1,3}(?:\.\d+)?)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressRegex();
}
