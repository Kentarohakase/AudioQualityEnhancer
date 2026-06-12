using System.Globalization;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed class AudioProcessedPreviewService
{
    public static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(20);

    internal const string PreviewFilePattern = "processed-preview-*.wav";

    private readonly FFmpegService _ffmpegService;
    private readonly string _previewDirectory;

    // Remembers the loudest segment per source file so switching presets does not
    // re-run the volumedetect probes. Only touched from the UI thread.
    private readonly Dictionary<string, TimeSpan> _segmentStartCache = new(StringComparer.Ordinal);

    public AudioProcessedPreviewService(FFmpegService ffmpegService)
        : this(ffmpegService, Path.Combine(Path.GetTempPath(), "AudioQualityEnhancer", "Previews"))
    {
    }

    internal AudioProcessedPreviewService(FFmpegService ffmpegService, string previewDirectory)
    {
        _ffmpegService = ffmpegService;
        _previewDirectory = previewDirectory;
    }

    public async Task<Result<ProcessedPreviewResult>> RenderAsync(
        ProcessingOptions options,
        Action<string>? log,
        Action<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath) || !File.Exists(options.InputPath))
        {
            return Result<ProcessedPreviewResult>.Failure(LocalizationService.Instance["Error_SourceFileNotFound"]);
        }

        var filterPlan = AudioFilterPlanner.BuildPlan(options);
        if (!CanRender(options, filterPlan))
        {
            return Result<ProcessedPreviewResult>.Failure(LocalizationService.Instance["Error_ProcessedPreviewNoFilters"]);
        }

        Directory.CreateDirectory(_previewDirectory);
        CleanupStalePreviews(_previewDirectory, TimeSpan.FromDays(2));
        var outputPath = Path.Combine(_previewDirectory, $"processed-preview-{Guid.NewGuid():N}.wav");
        var audioStream = options.SourceInfo is null
            ? options.AudioStream
            : AudioProcessingService.ResolveAudioStream(options.AudioStream, options.SourceInfo);
        var outputSampleRate = AudioProcessingService.ResolveLoudnessOutputSampleRate(
            filterPlan.LoudnessSettings is not null,
            audioStream,
            options.SourceInfo);
        var duration = ResolveProgressDuration(options.SourceInfo);
        var previewStart = await ResolvePreviewStartAsync(options, audioStream, log, cancellationToken);

        // Match the export pipeline: with two-pass loudness enabled the preview segment is
        // measured first and rendered with the same linear loudnorm the export will use.
        // A single-pass dynamic preview would sound different from the final file.
        var filterGraph = filterPlan.FilterGraph;
        if (filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness)
        {
            var measured = await MeasureSegmentLoudnessAsync(options.InputPath, filterPlan, audioStream, log, duration, previewStart, cancellationToken);
            if (measured is not null)
            {
                filterGraph = AudioFilterPlanner.BuildLoudnormFilter(filterPlan.PreLoudnessFilters, filterPlan.LoudnessSettings, measured, printJson: false);
            }
        }

        var arguments = BuildRenderArguments(options.InputPath, outputPath, filterGraph, audioStream, outputSampleRate, previewStart);

        log?.Invoke(LocalizationService.Instance.Format("Log_ProcessedPreviewStartingFormat", PreviewDuration.TotalSeconds));
        var result = await _ffmpegService.ExecuteAsync(arguments, log, progress, duration, cancellationToken);
        if (result.IsFailure)
        {
            TryDelete(outputPath);
            return Result<ProcessedPreviewResult>.Failure(result.ErrorMessage ?? LocalizationService.Instance["Error_ProcessedPreviewFailed"], result.Exception);
        }

        if (!File.Exists(outputPath))
        {
            return Result<ProcessedPreviewResult>.Failure(LocalizationService.Instance["Error_ProcessedPreviewMissingOutput"]);
        }

        return Result<ProcessedPreviewResult>.Success(new ProcessedPreviewResult(outputPath));
    }

    public static bool CanRender(ProcessingOptions options)
    {
        return CanRender(options, AudioFilterPlanner.BuildPlan(options));
    }

    public static string BuildCacheKey(ProcessingOptions options)
    {
        var filterPlan = AudioFilterPlanner.BuildPlan(options);
        var audioStream = options.SourceInfo is null
            ? options.AudioStream
            : AudioProcessingService.ResolveAudioStream(options.AudioStream, options.SourceInfo);
        var loudnessMode = filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness
            ? "twopass"
            : "singlepass";

        return string.Join(
            "|",
            NormalizePath(options.InputPath),
            GetFileStamp(options.InputPath),
            audioStream?.StreamIndex.ToString(CultureInfo.InvariantCulture) ?? "default",
            filterPlan.FilterGraph,
            loudnessMode);
    }

    private async Task<TimeSpan> ResolvePreviewStartAsync(
        ProcessingOptions options,
        AudioStreamInfo? audioStream,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var candidates = GetSegmentCandidates(options.SourceInfo?.Duration);
        if (candidates.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var cacheKey = string.Join(
            "|",
            NormalizePath(options.InputPath),
            GetFileStamp(options.InputPath),
            audioStream?.StreamIndex.ToString(CultureInfo.InvariantCulture) ?? "default");
        if (_segmentStartCache.TryGetValue(cacheKey, out var cachedStart))
        {
            return cachedStart;
        }

        var bestStart = TimeSpan.Zero;
        var bestMeanVolume = double.NegativeInfinity;
        foreach (var candidate in candidates)
        {
            var arguments = BuildVolumeDetectArguments(options.InputPath, audioStream, candidate);
            var result = await _ffmpegService.ExecuteAsync(arguments, null, null, null, cancellationToken);
            if (result.Value?.WasCancelled == true)
            {
                return TimeSpan.Zero;
            }

            if (result.IsFailure || result.Value is null)
            {
                continue;
            }

            var meanVolume = TryParseMeanVolume(result.Value.StandardError + Environment.NewLine + result.Value.StandardOutput);
            if (meanVolume is not null && meanVolume > bestMeanVolume)
            {
                bestMeanVolume = meanVolume.Value;
                bestStart = candidate;
            }
        }

        _segmentStartCache[cacheKey] = bestStart;
        if (bestStart > TimeSpan.Zero)
        {
            log?.Invoke(LocalizationService.Instance.Format("Log_PreviewSegmentFormat", bestStart.TotalSeconds));
        }

        return bestStart;
    }

    /// <summary>
    /// Candidate preview start positions. Short or unknown sources always preview from the
    /// beginning; longer sources probe evenly spread segments to find the loudest one, so the
    /// preview is judged on representative material instead of a quiet intro.
    /// </summary>
    internal static IReadOnlyList<TimeSpan> GetSegmentCandidates(TimeSpan? duration)
    {
        if (duration is null || duration.Value < PreviewDuration + PreviewDuration)
        {
            return Array.Empty<TimeSpan>();
        }

        var usableSeconds = (duration.Value - PreviewDuration).TotalSeconds;
        return new[] { 0d, 0.25d, 0.5d, 0.75d }
            .Select(fraction => TimeSpan.FromSeconds(usableSeconds * fraction))
            .ToArray();
    }

    internal static double? TryParseMeanVolume(string output)
    {
        const string marker = "mean_volume:";
        var index = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var rest = output[(index + marker.Length)..];
        var lineEnd = rest.IndexOfAny(new[] { '\r', '\n' });
        if (lineEnd >= 0)
        {
            rest = rest[..lineEnd];
        }

        rest = rest.Replace("dB", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    internal static IReadOnlyList<string> BuildVolumeDetectArguments(
        string inputPath,
        AudioStreamInfo? audioStream,
        TimeSpan seekStart)
    {
        var args = AudioProcessingService.BuildInputArguments(inputPath, audioStream, seekStart);
        args.Add("-t");
        args.Add(PreviewDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        args.Add("-af");
        args.Add("volumedetect");
        args.Add("-f");
        args.Add("null");
        args.Add("NUL");
        return args;
    }

    private async Task<LoudnormMeasuredStats?> MeasureSegmentLoudnessAsync(
        string inputPath,
        AudioFilterPlan filterPlan,
        AudioStreamInfo? audioStream,
        Action<string>? log,
        TimeSpan duration,
        TimeSpan seekStart,
        CancellationToken cancellationToken)
    {
        var measureFilter = AudioFilterPlanner.BuildLoudnormFilter(
            filterPlan.PreLoudnessFilters,
            filterPlan.LoudnessSettings!,
            null,
            printJson: true);
        var arguments = BuildMeasureArguments(inputPath, measureFilter, audioStream, seekStart);

        var result = await _ffmpegService.ExecuteAsync(arguments, log, null, duration, cancellationToken);
        if (result.IsFailure || result.Value is null)
        {
            // A failed measurement falls back to the single-pass preview instead of failing.
            return null;
        }

        return AudioProcessingService.TryParseLoudnormStats(
            result.Value.StandardError + Environment.NewLine + result.Value.StandardOutput);
    }

    internal static IReadOnlyList<string> BuildMeasureArguments(
        string inputPath,
        string filterGraph,
        AudioStreamInfo? audioStream,
        TimeSpan? seekStart = null)
    {
        var args = AudioProcessingService.BuildInputArguments(inputPath, audioStream, seekStart);
        args.Add("-t");
        args.Add(PreviewDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        args.Add("-af");
        args.Add(filterGraph);
        args.Add("-f");
        args.Add("null");
        args.Add("NUL");
        return args;
    }

    internal static IReadOnlyList<string> BuildRenderArguments(
        string inputPath,
        string outputPath,
        string filterGraph,
        AudioStreamInfo? audioStream,
        int? outputSampleRate = null,
        TimeSpan? seekStart = null)
    {
        var args = AudioProcessingService.BuildInputArguments(inputPath, audioStream, seekStart);
        args.Add("-t");
        args.Add(PreviewDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        args.Add("-af");
        args.Add(filterGraph);
        if (outputSampleRate is > 0)
        {
            args.Add("-ar");
            args.Add(outputSampleRate.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-c:a");
        args.Add("pcm_s24le");
        args.Add("-f");
        args.Add("wav");
        args.Add(outputPath);
        return args;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary preview cleanup should not hide the real UI action.
        }
    }

    internal static int CleanupStalePreviews(string previewDirectory, TimeSpan minimumAge)
    {
        if (!Directory.Exists(previewDirectory))
        {
            return 0;
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(previewDirectory, PreviewFilePattern);
        }
        catch
        {
            return 0;
        }

        var cutoff = DateTimeOffset.Now - minimumAge;
        var deleted = 0;
        foreach (var path in candidates)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.LastWriteTimeUtc > cutoff.UtcDateTime)
                {
                    continue;
                }

                fileInfo.Delete();
                deleted++;
            }
            catch
            {
                // Best effort cleanup; locked previews can be retried later.
            }
        }

        return deleted;
    }

    private static bool CanRender(ProcessingOptions options, AudioFilterPlan filterPlan)
    {
        return !options.Preset.IsCopyOnly && !options.Preset.IsArchiveExport && filterPlan.HasFilters;
    }

    private static TimeSpan ResolveProgressDuration(AudioInfo? sourceInfo)
    {
        if (sourceInfo?.Duration is { TotalSeconds: > 0 } duration && duration < PreviewDuration)
        {
            return duration;
        }

        return PreviewDuration;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string GetFileStamp(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "missing";
            }

            var info = new FileInfo(path);
            return string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
        }
        catch
        {
            return "unknown";
        }
    }
}

internal sealed record ProcessedPreviewResult(string OutputPath);
