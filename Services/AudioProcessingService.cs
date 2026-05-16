using System.Globalization;
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

    public async Task<Result<ProcessResult>> ProcessAsync(
        ProcessingOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath) || !File.Exists(options.InputPath))
        {
            return Result<ProcessResult>.Failure("Die Quelldatei wurde nicht gefunden.");
        }

        if (!_fileNameService.IsSupportedInputFile(options.InputPath))
        {
            return Result<ProcessResult>.Failure("Dieses Dateiformat wird nicht unterstützt.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            return Result<ProcessResult>.Failure("Bitte wähle einen Ausgabeordner aus.");
        }

        var ffmpegAvailability = await _ffmpegService.CheckAvailabilityAsync(cancellationToken);
        if (ffmpegAvailability.IsFailure)
        {
            return Result<ProcessResult>.Failure(ffmpegAvailability.ErrorMessage ?? "FFmpeg ist nicht verfügbar.", ffmpegAvailability.Exception);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var sourceInfo = options.SourceInfo;
        if (sourceInfo is null)
        {
            var analysis = await _ffprobeService.AnalyzeAsync(options.InputPath, _logService.Info, cancellationToken);
            if (analysis.IsFailure || analysis.Value is null)
            {
                return Result<ProcessResult>.Failure(analysis.ErrorMessage ?? "Die Quelle konnte nicht analysiert werden.", analysis.Exception);
            }

            sourceInfo = analysis.Value;
        }

        var outputPlan = BuildOutputPlan(options, sourceInfo);
        if (outputPlan.IsFailure || outputPlan.Value is null)
        {
            return Result<ProcessResult>.Failure(outputPlan.ErrorMessage ?? "Die Verarbeitung konnte nicht vorbereitet werden.", outputPlan.Exception);
        }

        var plan = outputPlan.Value;
        var tempOutputPath = _fileNameService.CreateTemporaryOutputPath(options.OutputDirectory, plan.FinalOutputPath);
        var ffmpegArguments = BuildArguments(options.InputPath, tempOutputPath, plan.FFmpegArguments, plan.FilterGraph);

        _logService.Info($"Ziel: {plan.FinalOutputPath}");
        if (!string.IsNullOrWhiteSpace(plan.FilterGraph))
        {
            _logService.Info($"Filter: {plan.FilterGraph}");
        }
        else
        {
            _logService.Info("Filter: keine");
        }

        var ffmpegResult = await _ffmpegService.ExecuteAsync(
            ffmpegArguments,
            _logService.Info,
            value => progress?.Report(value),
            sourceInfo.Duration,
            cancellationToken);

        if (ffmpegResult.IsFailure || ffmpegResult.Value is null)
        {
            TryDeleteTempFile(tempOutputPath);
            return ffmpegResult;
        }

        try
        {
            if (File.Exists(plan.FinalOutputPath))
            {
                return Result<ProcessResult>.Failure("Die Zieldatei existiert inzwischen bereits. Bitte starte die Verarbeitung erneut.");
            }

            File.Move(tempOutputPath, plan.FinalOutputPath);
            _logService.Info($"Fertig: {plan.FinalOutputPath}");

            var completedResult = ffmpegResult.Value with { OutputPath = plan.FinalOutputPath };
            return Result<ProcessResult>.Success(completedResult);
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tempOutputPath);
            return Result<ProcessResult>.Failure("Die Ausgabedatei konnte nach der Verarbeitung nicht gespeichert werden.", ex, ffmpegResult.Value);
        }
    }

    private Result<OutputPlan> BuildOutputPlan(ProcessingOptions options, AudioInfo sourceInfo)
    {
        if (options.Preset.IsCopyOnly)
        {
            var suggestion = _fileNameService.SuggestCopyOutput(sourceInfo);
            if (suggestion is null)
            {
                return Result<OutputPlan>.Failure("Diese Audiospur kann nicht zuverlässig ohne Re-Encoding in ein bekanntes Zielformat extrahiert werden. Wähle stattdessen ein Exportformat wie FLAC oder WAV.");
            }

            _logService.Info($"Verlustfreie Extraktion: {suggestion.Reason}");
            var outputPath = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, "extracted", suggestion.Extension);
            return Result<OutputPlan>.Success(new OutputPlan(outputPath, new[] { "-c:a", "copy" }, string.Empty));
        }

        var exportFormat = options.Preset.IsArchiveExport ? ExportFormat.Flac : options.ExportFormat;
        if (options.Preset.IsArchiveExport && options.ExportFormat.Id != ExportFormat.Flac.Id)
        {
            _logService.Info("Archiv Export erzwingt FLAC, damit die Bearbeitung verlustfrei gespeichert wird.");
        }

        var suffix = options.Preset.IsArchiveExport ? "archive_flac" : options.Preset.Id;
        var outputPathForTranscode = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, suffix, exportFormat.Extension);
        var filterGraph = BuildFilterGraph(options);
        return Result<OutputPlan>.Success(new OutputPlan(outputPathForTranscode, exportFormat.FFmpegArguments, filterGraph));
    }

    private static IReadOnlyList<string> BuildArguments(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> codecArguments,
        string filterGraph)
    {
        var args = new List<string>
        {
            "-hide_banner",
            "-nostdin",
            "-y",
            "-stats_period",
            "0.5",
            "-progress",
            "pipe:1",
            "-i",
            inputPath,
            "-map",
            "0:a:0",
            "-vn",
            "-sn",
            "-dn"
        };

        if (!string.IsNullOrWhiteSpace(filterGraph))
        {
            args.Add("-af");
            args.Add(filterGraph);
        }

        args.AddRange(codecArguments);
        args.Add(outputPath);
        return args;
    }

    private static string BuildFilterGraph(ProcessingOptions options)
    {
        if (options.Preset.Id == AudioPreset.Music.Id)
        {
            return "loudnorm=I=-14:TP=-1.5:LRA=11";
        }

        if (options.Preset.Id == AudioPreset.Speech.Id)
        {
            var filters = new List<string>
            {
                "highpass=f=80"
            };

            if (options.EnableSpeechPresenceBoost)
            {
                filters.Add("equalizer=f=3500:t=q:w=1:g=2");
            }

            if (options.EnableSpeechCompression)
            {
                filters.Add("acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250");
            }

            filters.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
            return string.Join(",", filters);
        }

        if (options.Preset.Id == AudioPreset.NoiseReduction.Id)
        {
            var value = Math.Clamp(options.NoiseReductionFloor, -35, -20);
            return string.Create(CultureInfo.InvariantCulture, $"afftdn=nf={value}");
        }

        return string.Empty;
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

    private sealed record OutputPlan(string FinalOutputPath, IReadOnlyList<string> FFmpegArguments, string FilterGraph);
}
