using System.Globalization;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed class AudioProcessedPreviewService
{
    public static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(20);

    internal const string PreviewFilePattern = "processed-preview-*.wav";

    private readonly FFmpegService _ffmpegService;
    private readonly string _previewDirectory;

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
        var arguments = BuildRenderArguments(options.InputPath, outputPath, filterPlan.FilterGraph, audioStream, outputSampleRate);
        var duration = ResolveProgressDuration(options.SourceInfo);

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
        var filterGraph = AudioFilterPlanner.BuildPlan(options).FilterGraph;
        var audioStream = options.SourceInfo is null
            ? options.AudioStream
            : AudioProcessingService.ResolveAudioStream(options.AudioStream, options.SourceInfo);

        return string.Join(
            "|",
            NormalizePath(options.InputPath),
            GetFileStamp(options.InputPath),
            audioStream?.StreamIndex.ToString(CultureInfo.InvariantCulture) ?? "default",
            filterGraph);
    }

    internal static IReadOnlyList<string> BuildRenderArguments(
        string inputPath,
        string outputPath,
        string filterGraph,
        AudioStreamInfo? audioStream,
        int? outputSampleRate = null)
    {
        var args = AudioProcessingService.BuildInputArguments(inputPath, audioStream);
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
        args.Add("pcm_s16le");
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
