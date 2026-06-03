using System.Globalization;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed class AudioProcessedPreviewService
{
    public static readonly TimeSpan PreviewDuration = TimeSpan.FromSeconds(20);

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
        var outputPath = Path.Combine(_previewDirectory, $"processed-preview-{Guid.NewGuid():N}.wav");
        var audioStream = options.SourceInfo is null
            ? options.AudioStream
            : AudioProcessingService.ResolveAudioStream(options.AudioStream, options.SourceInfo);
        var arguments = BuildRenderArguments(options.InputPath, outputPath, filterPlan.FilterGraph, audioStream);
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
        AudioStreamInfo? audioStream)
    {
        var args = AudioProcessingService.BuildInputArguments(inputPath, audioStream);
        args.Add("-t");
        args.Add(PreviewDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));
        args.Add("-af");
        args.Add(filterGraph);
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
