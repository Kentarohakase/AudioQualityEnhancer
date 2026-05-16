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
            return "Stream copy: -vn -c:a copy";
        }

        var filterPlan = BuildFilterPlan(options);
        if (!filterPlan.HasFilters)
        {
            return "Keine Audiofilter. Es wird nur in das gewählte Zielformat exportiert.";
        }

        if (filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness)
        {
            var pass1 = BuildLoudnormFilter(filterPlan.PreLoudnessFilters, filterPlan.LoudnessSettings, null, printJson: true);
            var pass2 = BuildLoudnormFilter(filterPlan.PreLoudnessFilters, filterPlan.LoudnessSettings, LoudnormMeasuredStats.Placeholder, printJson: false);
            return $"Pass 1: {pass1}{Environment.NewLine}Pass 2: {pass2}";
        }

        return filterPlan.FilterGraph;
    }

    public async Task<Result<ProcessResult>> ProcessAsync(
        ProcessingOptions options,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, 2, "Prüfe Eingabe");

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

        Report(progress, 5, "Prüfe FFmpeg");
        var ffmpegAvailability = await _ffmpegService.CheckAvailabilityAsync(cancellationToken);
        if (ffmpegAvailability.IsFailure)
        {
            return Result<ProcessResult>.Failure(ffmpegAvailability.ErrorMessage ?? "FFmpeg ist nicht verfügbar.", ffmpegAvailability.Exception);
        }

        Directory.CreateDirectory(options.OutputDirectory);

        var sourceInfo = options.SourceInfo;
        if (sourceInfo is null)
        {
            Report(progress, 8, "Analysiere Quelle");
            var analysis = await _ffprobeService.AnalyzeAsync(options.InputPath, _logService.Info, cancellationToken);
            if (analysis.IsFailure || analysis.Value is null)
            {
                return Result<ProcessResult>.Failure(analysis.ErrorMessage ?? "Die Quelle konnte nicht analysiert werden.", analysis.Exception);
            }

            sourceInfo = analysis.Value;
        }

        Report(progress, 10, "Bereite Verarbeitung vor");
        var outputPlan = BuildOutputPlan(options, sourceInfo);
        if (outputPlan.IsFailure || outputPlan.Value is null)
        {
            return Result<ProcessResult>.Failure(outputPlan.ErrorMessage ?? "Die Verarbeitung konnte nicht vorbereitet werden.", outputPlan.Exception);
        }

        var plan = outputPlan.Value;
        var tempOutputPath = _fileNameService.CreateTemporaryOutputPath(options.OutputDirectory, plan.FinalOutputPath);

        _logService.Info($"Ziel: {plan.FinalOutputPath}");
        LogFilterPlan(plan);

        var filterGraph = plan.FilterGraph;
        if (plan.ShouldUseTwoPassLoudness)
        {
            var pass1Result = await RunLoudnessAnalysisPassAsync(options.InputPath, plan, sourceInfo, progress, cancellationToken);
            if (pass1Result.IsFailure)
            {
                TryDeleteTempFile(tempOutputPath);
                return Result<ProcessResult>.Failure(pass1Result.ErrorMessage ?? "Der Loudness-Messpass ist fehlgeschlagen.", pass1Result.Exception);
            }

            if (pass1Result.Value is not null)
            {
                filterGraph = BuildLoudnormFilter(plan.PreLoudnessFilters, plan.LoudnessSettings!, pass1Result.Value, printJson: false);
                _logService.Info("Zwei-Pass-Loudness: Messwerte wurden übernommen.");
            }
            else
            {
                _logService.Warning("Zwei-Pass-Loudness konnte nicht ausgewertet werden. Die Verarbeitung läuft mit dem sicheren Ein-Pass-Filter weiter.");
                filterGraph = plan.FilterGraph;
            }
        }

        var ffmpegArguments = BuildRenderArguments(options.InputPath, tempOutputPath, plan.FFmpegArguments, filterGraph);
        if (plan.ShouldUseTwoPassLoudness)
        {
            _logService.Info($"Renderfilter: {filterGraph}");
        }

        var renderStart = plan.ShouldUseTwoPassLoudness ? 50d : 12d;
        var renderSpan = plan.ShouldUseTwoPassLoudness ? 45d : 83d;

        Report(progress, renderStart, "Verarbeite Audio", plan.ShouldUseTwoPassLoudness ? "Pass 2 von 2" : null);
        var ffmpegResult = await _ffmpegService.ExecuteAsync(
            ffmpegArguments,
            _logService.Info,
            value => Report(progress, renderStart + value / 100d * renderSpan, "Verarbeite Audio", plan.ShouldUseTwoPassLoudness ? "Pass 2 von 2" : null),
            sourceInfo.Duration,
            cancellationToken);

        if (ffmpegResult.IsFailure || ffmpegResult.Value is null)
        {
            TryDeleteTempFile(tempOutputPath);
            return ffmpegResult;
        }

        try
        {
            Report(progress, 98, "Speichere Ergebnis");
            if (File.Exists(plan.FinalOutputPath))
            {
                return Result<ProcessResult>.Failure("Die Zieldatei existiert inzwischen bereits. Bitte starte die Verarbeitung erneut.");
            }

            File.Move(tempOutputPath, plan.FinalOutputPath);
            _logService.Info($"Fertig: {plan.FinalOutputPath}");

            Report(progress, 100, "Fertig");
            var completedResult = ffmpegResult.Value with { OutputPath = plan.FinalOutputPath };
            return Result<ProcessResult>.Success(completedResult);
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tempOutputPath);
            return Result<ProcessResult>.Failure("Die Ausgabedatei konnte nach der Verarbeitung nicht gespeichert werden.", ex, ffmpegResult.Value);
        }
    }

    private async Task<Result<LoudnormMeasuredStats?>> RunLoudnessAnalysisPassAsync(
        string inputPath,
        OutputPlan plan,
        AudioInfo sourceInfo,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        Report(progress, 12, "Analysiere Loudness", "Pass 1 von 2");

        var pass1Filter = BuildLoudnormFilter(plan.PreLoudnessFilters, plan.LoudnessSettings!, null, printJson: true);
        var pass1Arguments = BuildLoudnessAnalysisArguments(inputPath, pass1Filter);

        _logService.Info("Zwei-Pass-Loudness: starte Messpass.");
        _logService.Info($"Analysefilter: {pass1Filter}");

        var pass1Result = await _ffmpegService.ExecuteAsync(
            pass1Arguments,
            _logService.Info,
            value => Report(progress, 12 + value / 100d * 35d, "Analysiere Loudness", "Pass 1 von 2"),
            sourceInfo.Duration,
            cancellationToken);

        if (pass1Result.IsFailure || pass1Result.Value is null)
        {
            return Result<LoudnormMeasuredStats?>.Failure(pass1Result.ErrorMessage ?? "Der Loudness-Messpass ist fehlgeschlagen.", pass1Result.Exception);
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
                return Result<OutputPlan>.Failure("Diese Audiospur kann nicht zuverlässig ohne Re-Encoding in ein bekanntes Zielformat extrahiert werden. Wähle stattdessen ein Exportformat wie FLAC oder WAV.");
            }

            _logService.Info($"Verlustfreie Extraktion: {suggestion.Reason}");
            var outputPath = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, "extracted", suggestion.Extension);
            return Result<OutputPlan>.Success(new OutputPlan(outputPath, new[] { "-c:a", "copy" }, string.Empty, Array.Empty<string>(), null, false));
        }

        var exportFormat = options.Preset.IsArchiveExport ? ExportFormat.Flac : options.ExportFormat;
        if (options.Preset.IsArchiveExport && options.ExportFormat.Id != ExportFormat.Flac.Id)
        {
            _logService.Info("Archiv Export erzwingt FLAC, damit die Bearbeitung verlustfrei gespeichert wird.");
        }

        var suffix = options.Preset.IsArchiveExport
            ? "archive_flac"
            : exportFormat.Id == ExportFormat.PremierePro.Id
                ? $"{options.Preset.Id}_premiere_pro"
                : options.Preset.Id;
        var outputPathForTranscode = _fileNameService.CreateUniqueOutputPath(options.InputPath, options.OutputDirectory, suffix, exportFormat.Extension);
        var filterPlan = BuildFilterPlan(options);

        return Result<OutputPlan>.Success(new OutputPlan(
            outputPathForTranscode,
            exportFormat.FFmpegArguments,
            filterPlan.FilterGraph,
            filterPlan.PreLoudnessFilters,
            filterPlan.LoudnessSettings,
            filterPlan.LoudnessSettings is not null && options.UseTwoPassLoudness));
    }

    private static FilterPlan BuildFilterPlan(ProcessingOptions options)
    {
        if (options.Preset.Id == AudioPreset.Music.Id)
        {
            var loudness = new LoudnessSettings("-14", "-1.5", "11");
            return new FilterPlan(
                BuildLoudnormFilter(Array.Empty<string>(), loudness, null, printJson: false),
                Array.Empty<string>(),
                loudness);
        }

        if (options.Preset.Id == AudioPreset.Speech.Id)
        {
            var preFilters = new List<string>
            {
                "highpass=f=80"
            };

            if (options.EnableSpeechPresenceBoost)
            {
                preFilters.Add("equalizer=f=3500:t=q:w=1:g=2");
            }

            if (options.EnableSpeechCompression)
            {
                preFilters.Add("acompressor=threshold=-18dB:ratio=2.5:attack=20:release=250");
            }

            var loudness = new LoudnessSettings("-16", "-1.5", "11");
            return new FilterPlan(BuildLoudnormFilter(preFilters, loudness, null, printJson: false), preFilters, loudness);
        }

        if (options.Preset.Id == AudioPreset.NoiseReduction.Id)
        {
            var value = Math.Clamp(options.NoiseReductionFloor, -35, -20);
            return new FilterPlan(string.Create(CultureInfo.InvariantCulture, $"afftdn=nf={value}"), Array.Empty<string>(), null);
        }

        return new FilterPlan(string.Empty, Array.Empty<string>(), null);
    }

    private static IReadOnlyList<string> BuildRenderArguments(
        string inputPath,
        string outputPath,
        IReadOnlyList<string> codecArguments,
        string filterGraph)
    {
        var args = BuildInputArguments(inputPath);

        if (!string.IsNullOrWhiteSpace(filterGraph))
        {
            args.Add("-af");
            args.Add(filterGraph);
        }

        args.AddRange(codecArguments);
        args.Add(outputPath);
        return args;
    }

    private static IReadOnlyList<string> BuildLoudnessAnalysisArguments(string inputPath, string filterGraph)
    {
        var args = BuildInputArguments(inputPath);
        args.Add("-af");
        args.Add(filterGraph);
        args.Add("-f");
        args.Add("null");
        args.Add("NUL");
        return args;
    }

    private static List<string> BuildInputArguments(string inputPath)
    {
        return new List<string>
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
    }

    private static string BuildLoudnormFilter(
        IReadOnlyList<string> preFilters,
        LoudnessSettings settings,
        LoudnormMeasuredStats? stats,
        bool printJson)
    {
        var filters = new List<string>(preFilters);
        var loudnorm = string.Create(
            CultureInfo.InvariantCulture,
            $"loudnorm=I={settings.IntegratedLufs}:TP={settings.TruePeakDb}:LRA={settings.LoudnessRange}");

        if (stats is not null)
        {
            loudnorm += string.Create(
                CultureInfo.InvariantCulture,
                $":measured_I={stats.InputIntegrated}:measured_TP={stats.InputTruePeak}:measured_LRA={stats.InputLoudnessRange}:measured_thresh={stats.InputThreshold}:offset={stats.TargetOffset}:linear=true:print_format=summary");
        }
        else if (printJson)
        {
            loudnorm += ":print_format=json";
        }

        filters.Add(loudnorm);
        return string.Join(",", filters);
    }

    private static LoudnormMeasuredStats? TryParseLoudnormStats(string output)
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
            _logService.Info($"Filter: {plan.FilterGraph}");
        }
        else
        {
            _logService.Info("Filter: keine");
        }

        if (plan.ShouldUseTwoPassLoudness)
        {
            _logService.Info("Loudness: Zwei-Pass-Modus aktiv.");
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
        bool ShouldUseTwoPassLoudness);

    private sealed record FilterPlan(
        string FilterGraph,
        IReadOnlyList<string> PreLoudnessFilters,
        LoudnessSettings? LoudnessSettings)
    {
        public bool HasFilters => !string.IsNullOrWhiteSpace(FilterGraph);
    }

    private sealed record LoudnessSettings(string IntegratedLufs, string TruePeakDb, string LoudnessRange);

    private sealed record LoudnormMeasuredStats(
        string InputIntegrated,
        string InputTruePeak,
        string InputLoudnessRange,
        string InputThreshold,
        string TargetOffset)
    {
        public static LoudnormMeasuredStats Placeholder { get; } = new("measured_I", "measured_TP", "measured_LRA", "measured_thresh", "offset");
    }
}
