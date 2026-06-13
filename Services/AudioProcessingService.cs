using System.Globalization;
using System.Text.Json;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioProcessingService
{
    private readonly FFmpegService _ffmpegService;
    private readonly FFprobeService _ffprobeService;
    private readonly FileNameService _fileNameService;
    private readonly LogService _logService;

    public AudioProcessingService(
        FFmpegService ffmpegService,
        FFprobeService ffprobeService,
        FileNameService fileNameService,
        LogService logService)
    {
        _ffmpegService = ffmpegService;
        _ffprobeService = ffprobeService;
        _fileNameService = fileNameService;
        _logService = logService;
    }

    public static string BuildFilterPreview(ProcessingOptions options)
    {
        if (options.Preset.IsCopyOnly)
        {
            return LocalizationService.Instance["Filter_StreamCopy"];
        }

        var filterPlan = AudioFilterPlanner.BuildPlan(options);
        if (!filterPlan.HasFilters)
        {
            return LocalizationService.Instance["Filter_NoFilters"];
        }

        if (filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness)
        {
            var pass1 = AudioFilterPlanner.BuildLoudnormFilter(filterPlan.PreLoudnessFilters, filterPlan.LoudnessSettings, null, printJson: true);
            var pass2 = AudioFilterPlanner.BuildLoudnormFilter(filterPlan.PreLoudnessFilters, filterPlan.LoudnessSettings, LoudnormMeasuredStats.Placeholder, printJson: false);
            return $"Pass 1: {pass1}{Environment.NewLine}Pass 2: {pass2}";
        }

        return filterPlan.FilterGraph;
    }

    public async Task<Result<ProcessResult>> ProcessAsync(
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, 2, LocalizationService.Instance["Phase_CheckInput"]);

        if (string.IsNullOrWhiteSpace(options.InputPath) || !File.Exists(options.InputPath))
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_SourceFileNotFound"]);
        }

        if (!_fileNameService.IsSupportedInputFile(options.InputPath))
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_UnsupportedFormatShort"]);
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_OutputFolderRequired"]);
        }

        Report(progress, 4, LocalizationService.Instance["Phase_CheckOutput"]);
        var outputDirectoryCheck = EnsureOutputDirectoryWritable(options.OutputDirectory);
        if (outputDirectoryCheck.IsFailure)
        {
            return Result<ProcessResult>.Failure(outputDirectoryCheck.ErrorMessage ?? LocalizationService.Instance["Error_OutputFolderNotWritable"], outputDirectoryCheck.Exception);
        }

        Report(progress, 5, LocalizationService.Instance["Phase_CheckFFmpeg"]);
        var ffmpegAvailability = await _ffmpegService.CheckAvailabilityAsync(cancellationToken);
        if (ffmpegAvailability.IsFailure)
        {
            return Result<ProcessResult>.Failure(ffmpegAvailability.ErrorMessage ?? LocalizationService.Instance["Error_FFmpegUnavailable"], ffmpegAvailability.Exception);
        }

        _fileNameService.CleanupTemporaryOutputFiles(options.OutputDirectory, TimeSpan.FromDays(2));

        var sourceInfo = options.SourceInfo;
        if (sourceInfo is null)
        {
            Report(progress, 8, LocalizationService.Instance["Phase_AnalyzingSource"]);
            var analysis = await _ffprobeService.AnalyzeAsync(options.InputPath, _logService.Info, cancellationToken);
            if (analysis.IsFailure || analysis.Value is null)
            {
                return Result<ProcessResult>.Failure(analysis.ErrorMessage ?? LocalizationService.Instance["Error_SourceAnalysisFailed"], analysis.Exception);
            }

            sourceInfo = analysis.Value;
        }

        var diskSpaceCheck = EnsureSufficientDiskSpace(options.OutputDirectory, EstimateOutputSizeBytes(options, sourceInfo));
        if (diskSpaceCheck.IsFailure)
        {
            return Result<ProcessResult>.Failure(diskSpaceCheck.ErrorMessage ?? LocalizationService.Instance["Error_OutputFolderNotWritable"], diskSpaceCheck.Exception);
        }

        Report(progress, 10, LocalizationService.Instance["Phase_PreparingProcessing"]);
        var outputPlan = BuildOutputPlan(options, sourceInfo);
        if (outputPlan.IsFailure || outputPlan.Value is null)
        {
            return Result<ProcessResult>.Failure(outputPlan.ErrorMessage ?? LocalizationService.Instance["Error_PreparationFailed"], outputPlan.Exception);
        }

        var plan = outputPlan.Value;
        var tempOutputPath = _fileNameService.CreateTemporaryOutputPath(options.OutputDirectory, plan.FinalOutputPath);

        _logService.Info(LocalizationService.Instance.Format("Log_TargetFormat", plan.FinalOutputPath));
        LogFilterPlan(plan);

        var filterGraph = plan.FilterGraph;
        if (plan.ShouldUseTwoPassLoudness)
        {
            var selectedStream = ResolveAudioStream(options.AudioStream, sourceInfo);
            var pass1Result = await RunLoudnessAnalysisPassAsync(options.InputPath, plan, sourceInfo, selectedStream, progress, cancellationToken);
            if (pass1Result.IsFailure)
            {
                TryDeleteTempFile(tempOutputPath);
                return Result<ProcessResult>.Failure(pass1Result.ErrorMessage ?? LocalizationService.Instance["Error_LoudnessMeasurementFailed"], pass1Result.Exception);
            }

            if (pass1Result.Value is not null)
            {
                filterGraph = AudioFilterPlanner.BuildLoudnormFilter(plan.PreLoudnessFilters, plan.LoudnessSettings!, pass1Result.Value, printJson: false);
                _logService.Info(LocalizationService.Instance["Log_TwoPassMeasurementsApplied"]);
            }
            else
            {
                _logService.Warning(LocalizationService.Instance["Log_TwoPassMeasurementsFailed"]);
                filterGraph = plan.FilterGraph;
            }
        }

        var renderStream = ResolveAudioStream(options.AudioStream, sourceInfo);
        var outputSampleRate = ResolveLoudnessOutputSampleRate(plan.LoudnessSettings is not null, renderStream, sourceInfo);
        var ffmpegArguments = BuildRenderArguments(options.InputPath, tempOutputPath, plan.FFmpegArguments, filterGraph, renderStream, outputSampleRate, plan.IncludeCoverArt);
        if (plan.ShouldUseTwoPassLoudness)
        {
            _logService.Info(LocalizationService.Instance.Format("Log_RenderFilterFormat", filterGraph));
        }

        var renderStart = plan.ShouldUseTwoPassLoudness ? 50d : 12d;
        var renderSpan = plan.ShouldUseTwoPassLoudness ? 45d : 83d;

        var processingPhase = LocalizationService.Instance["Phase_ProcessingAudio"];
        var pass2Detail = plan.ShouldUseTwoPassLoudness ? LocalizationService.Instance["Phase_Pass2of2"] : null;
        Report(progress, renderStart, processingPhase, pass2Detail);
        var ffmpegResult = await _ffmpegService.ExecuteAsync(
            ffmpegArguments,
            _logService.Info,
            value => Report(progress, renderStart + value / 100d * renderSpan, processingPhase, pass2Detail),
            sourceInfo.Duration,
            cancellationToken);

        if (ffmpegResult.IsFailure || ffmpegResult.Value is null)
        {
            TryDeleteTempFile(tempOutputPath);
            return ffmpegResult;
        }

        try
        {
            Report(progress, 98, LocalizationService.Instance["Phase_SavingResult"]);
            File.Move(tempOutputPath, plan.FinalOutputPath);
            _logService.Info(LocalizationService.Instance.Format("Log_FinishedFormat", plan.FinalOutputPath));

            Report(progress, 100, LocalizationService.Instance["Phase_Done"]);
            var completedResult = ffmpegResult.Value with { OutputPath = plan.FinalOutputPath };
            return Result<ProcessResult>.Success(completedResult);
        }
        catch (IOException ex) when (File.Exists(plan.FinalOutputPath))
        {
            TryDeleteTempFile(tempOutputPath);
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_TargetExists"], ex, ffmpegResult.Value);
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tempOutputPath);
            return Result<ProcessResult>.Failure(LocalizationService.Instance["Error_SaveFailed"], ex, ffmpegResult.Value);
        }
    }

    private async Task<Result<LoudnormMeasuredStats?>> RunLoudnessAnalysisPassAsync(
        string inputPath,
        OutputPlan plan,
        AudioInfo sourceInfo,
        AudioStreamInfo? audioStream,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var phase = LocalizationService.Instance["Phase_AnalyzingLoudness"];
        var pass1Detail = LocalizationService.Instance["Phase_Pass1of2"];
        Report(progress, 12, phase, pass1Detail);

        var pass1Filter = AudioFilterPlanner.BuildLoudnormFilter(plan.PreLoudnessFilters, plan.LoudnessSettings!, null, printJson: true);
        var pass1Arguments = BuildLoudnessAnalysisArguments(inputPath, pass1Filter, audioStream);

        _logService.Info(LocalizationService.Instance["Log_TwoPassStartingMeasure"]);
        _logService.Info(LocalizationService.Instance.Format("Log_AnalysisFilterFormat", pass1Filter));

        var pass1Result = await _ffmpegService.ExecuteAsync(
            pass1Arguments,
            _logService.Info,
            value => Report(progress, 12 + value / 100d * 35d, phase, pass1Detail),
            sourceInfo.Duration,
            cancellationToken);

        if (pass1Result.IsFailure || pass1Result.Value is null)
        {
            return Result<LoudnormMeasuredStats?>.Failure(pass1Result.ErrorMessage ?? LocalizationService.Instance["Error_LoudnessMeasurementFailed"], pass1Result.Exception);
        }

        var stats = TryParseLoudnormStats(pass1Result.Value.StandardError + Environment.NewLine + pass1Result.Value.StandardOutput);
        return Result<LoudnormMeasuredStats?>.Success(stats);
    }

    private Result<OutputPlan> BuildOutputPlan(ProcessingOptions options, AudioInfo sourceInfo)
    {
        if (options.Preset.IsCopyOnly)
        {
            var suggestion = _fileNameService.SuggestCopyOutput(sourceInfo);
            if (suggestion is null)
            {
                return Result<OutputPlan>.Failure(LocalizationService.Instance["Error_StreamCopyNotPossible"]);
            }

            _logService.Info(LocalizationService.Instance.Format("Log_LosslessExtractionFormat", suggestion.Reason));
            var outputPath = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, "extracted", suggestion.Extension);
            return Result<OutputPlan>.Success(new OutputPlan(outputPath, new[] { "-c:a", "copy" }, string.Empty, Array.Empty<string>(), null, false, false));
        }

        var exportFormat = ExportFormat.ResolveForPreset(options.Preset, options.ExportFormat);
        if (options.Preset.IsArchiveExport && options.ExportFormat.Id != ExportFormat.Flac.Id)
        {
            _logService.Info(LocalizationService.Instance["Log_ArchiveForcesFlac"]);
        }

        var suffix = options.Preset.IsArchiveExport
            ? "archive_flac"
            : exportFormat.Id == ExportFormat.PremierePro.Id
                ? $"{options.Preset.Id}_premiere_pro"
                : options.Preset.Id;
        var outputPathForTranscode = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, suffix, exportFormat.Extension);
        var filterPlan = AudioFilterPlanner.BuildPlan(options);
        var includeCoverArt = exportFormat.SupportsCoverArt && _fileNameService.IsAudioOnlyContainer(options.InputPath);

        return Result<OutputPlan>.Success(new OutputPlan(
            outputPathForTranscode,
            exportFormat.FFmpegArguments,
            filterPlan.FilterGraph,
            filterPlan.PreLoudnessFilters,
            filterPlan.LoudnessSettings,
            filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness,
            includeCoverArt));
    }

    /// <summary>
    /// Rough output size estimate in bytes, used only for the free-space check.
    /// Estimates err on the small side so the check never blocks a feasible export.
    /// </summary>
    internal static long EstimateOutputSizeBytes(ProcessingOptions options, AudioInfo sourceInfo)
    {
        if (options.Preset.IsCopyOnly)
        {
            return sourceInfo.FileSizeBytes;
        }

        var exportFormat = ExportFormat.ResolveForPreset(options.Preset, options.ExportFormat);
        var durationSeconds = sourceInfo.Duration?.TotalSeconds ?? 0;
        if (durationSeconds <= 0)
        {
            return sourceInfo.FileSizeBytes;
        }

        var channels = Math.Max(1, options.AudioStream?.Channels ?? sourceInfo.Channels ?? 2);
        var sampleRate = options.AudioStream?.SampleRate ?? sourceInfo.SampleRate ?? 48_000;
        var bytesPerSecond = exportFormat.Id switch
        {
            "premiere_pro" => 48_000d * channels * 3,
            "wav24" => (double)sampleRate * channels * 3,
            "flac" => sampleRate * channels * 3 * 0.65,
            "mp3_320" => 40_000d,
            "aac_256" => 32_000d,
            "opus_160" => 20_000d,
            "opus_192" => 24_000d,
            _ => (double)sampleRate * channels * 3
        };

        return (long)(durationSeconds * bytesPerSecond);
    }

    internal static Result EnsureSufficientDiskSpace(string outputDirectory, long estimatedBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(outputDirectory));
            if (string.IsNullOrWhiteSpace(root))
            {
                return Result.Success();
            }

            var drive = new DriveInfo(root);
            var requiredBytes = estimatedBytes + 32 * 1024 * 1024;
            if (drive.AvailableFreeSpace < requiredBytes)
            {
                var neededMb = requiredBytes / (1024 * 1024);
                var freeMb = drive.AvailableFreeSpace / (1024 * 1024);
                return Result.Failure(LocalizationService.Instance.Format("Error_InsufficientDiskSpaceFormat", neededMb, freeMb));
            }

            return Result.Success();
        }
        catch
        {
            // UNC shares and exotic mounts have no reliable free-space query; the
            // writability probe already ran, so processing proceeds without the check.
            return Result.Success();
        }
    }

    internal static Result EnsureOutputDirectoryWritable(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Result.Failure(LocalizationService.Instance["Error_OutputFolderRequired"]);
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var probePath = Path.Combine(outputDirectory, $"{FileNameService.TemporaryFilePrefix}write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return Result.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException or ArgumentException)
        {
            return Result.Failure(LocalizationService.Instance["Error_OutputFolderNotWritable"], ex);
        }
    }

    private static IReadOnlyList<string> BuildRenderArguments(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> codecArguments,
        string filterGraph,
        AudioStreamInfo? audioStream,
        int? outputSampleRate = null,
        bool includeCoverArt = false)
    {
        return BuildRenderPlan(inputPath, outputPath, codecArguments, filterGraph, audioStream, outputSampleRate, includeCoverArt).Arguments;
    }

    internal static FFmpegRenderPlan BuildRenderPlan(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> codecArguments,
        string filterGraph,
        AudioStreamInfo? audioStream,
        int? outputSampleRate = null,
        bool includeCoverArt = false)
    {
        var args = BuildInputArguments(inputPath, audioStream, includeCoverArt: includeCoverArt);

        if (!string.IsNullOrWhiteSpace(filterGraph))
        {
            args.Add("-af");
            args.Add(filterGraph);
        }

        if (outputSampleRate is > 0)
        {
            args.Add("-ar");
            args.Add(outputSampleRate.Value.ToString(CultureInfo.InvariantCulture));
        }

        args.AddRange(codecArguments);

        if (includeCoverArt)
        {
            args.Add("-c:v");
            args.Add("copy");
        }

        args.Add(outputPath);
        return new FFmpegRenderPlan(inputPath, outputPath, codecArguments, filterGraph, audioStream?.FFmpegMapSpecifier ?? "0:a:0", args);
    }

    // FFmpeg's loudnorm filter resamples internally and outputs 192 kHz; without an
    // explicit output rate every normalized export would inherit that. Export formats
    // with their own -ar (e.g. Premiere Pro) still win because their arguments come later.
    internal static int? ResolveLoudnessOutputSampleRate(bool usesLoudness, AudioStreamInfo? audioStream, AudioInfo? sourceInfo)
    {
        if (!usesLoudness)
        {
            return null;
        }

        var sampleRate = audioStream?.SampleRate ?? sourceInfo?.SampleRate;
        return sampleRate is > 0 ? sampleRate : null;
    }

    private static IReadOnlyList<string> BuildLoudnessAnalysisArguments(string inputPath, string filterGraph, AudioStreamInfo? audioStream)
    {
        var args = BuildInputArguments(inputPath, audioStream);
        args.Add("-af");
        args.Add(filterGraph);
        args.Add("-f");
        args.Add("null");
        args.Add("NUL");
        return args;
    }

    internal static List<string> BuildInputArguments(string inputPath, AudioStreamInfo? audioStream, TimeSpan? seekStart = null, bool includeCoverArt = false)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-stats_period",
            "0.5",
            "-progress",
            "pipe:1"
        };

        if (seekStart is { TotalSeconds: > 0 } seek)
        {
            args.Add("-ss");
            args.Add(seek.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture));
        }

        args.Add("-i");
        args.Add(inputPath);
        args.Add("-map");
        args.Add(audioStream?.FFmpegMapSpecifier ?? "0:a:0");

        if (includeCoverArt)
        {
            // Carry an attached cover-art picture through (optional with '?'); for audio
            // sources the first video stream is the cover rather than real video.
            args.Add("-map");
            args.Add("0:v:0?");
        }
        else
        {
            args.Add("-vn");
        }

        args.Add("-sn");
        args.Add("-dn");

        return args;
    }

    internal static AudioStreamInfo? ResolveAudioStream(AudioStreamInfo? requestedStream, AudioInfo sourceInfo)
    {
        if (sourceInfo.AudioStreams.Count == 0)
        {
            return requestedStream;
        }

        if (requestedStream is not null)
        {
            var matchingStream = sourceInfo.AudioStreams.FirstOrDefault(stream => stream.StreamIndex == requestedStream.StreamIndex);
            if (matchingStream is not null)
            {
                return matchingStream;
            }
        }

        return sourceInfo.SelectedAudioStream ?? sourceInfo.AudioStreams.First();
    }

    internal static LoudnormMeasuredStats? TryParseLoudnormStats(string output)
    {
        var marker = output.IndexOf("\"input_i\"", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = output.LastIndexOf('{', marker);
        if (start < 0)
        {
            return null;
        }

        var end = FindMatchingBrace(output, start);
        if (end < 0)
        {
            return null;
        }

        var json = output[start..(end + 1)];

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new LoudnormMeasuredStats(
                GetRequiredString(root, "input_i"),
                GetRequiredString(root, "input_tp"),
                GetRequiredString(root, "input_lra"),
                GetRequiredString(root, "input_thresh"),
                GetRequiredString(root, "target_offset"));
        }
        catch
        {
            return null;
        }
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? property.GetRawText()
            : "0";
    }

    private static int FindMatchingBrace(string value, int start)
    {
        var depth = 0;
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] == '{')
            {
                depth++;
            }
            else if (value[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void LogFilterPlan(OutputPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.FilterGraph))
        {
            _logService.Info(LocalizationService.Instance.Format("Log_FilterFormat", plan.FilterGraph));
        }
        else
        {
            _logService.Info(LocalizationService.Instance["Log_FilterNone"]);
        }

        if (plan.ShouldUseTwoPassLoudness)
        {
            _logService.Info(LocalizationService.Instance["Log_TwoPassActive"]);
        }
    }

    private static void Report(IProgress<ProcessingProgress>? progress, double percentage, string phase, string? detail = null)
    {
        progress?.Report(new ProcessingProgress(Math.Clamp(percentage, 0, 100), phase, detail));
    }

    private static void TryDeleteTempFile(string path)
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
            // A failed cleanup should not hide the real processing error.
        }
    }

    private sealed record OutputPlan(
        string FinalOutputPath,
        IReadOnlyList<string> FFmpegArguments,
        string FilterGraph,
        IReadOnlyList<string> PreLoudnessFilters,
        LoudnessSettings? LoudnessSettings,
        bool ShouldUseTwoPassLoudness,
        bool IncludeCoverArt);

}

internal sealed record FFmpegRenderPlan(
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> CodecArguments,
    string FilterGraph,
    string AudioMap,
    IReadOnlyList<string> Arguments);
