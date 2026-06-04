using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class AudioValidationService
{
    private const double DurationWarningSeconds = 1d;
    private const double DurationWarningRatio = 0.01d;
    private const double DurationCriticalSeconds = 3d;
    private const double DurationCriticalRatio = 0.03d;
    private const double LoudnessToleranceLu = 2d;

    private readonly FFprobeService _ffprobeService;
    private readonly AudioDiagnosticsService _audioDiagnosticsService;
    private readonly LogService _logService;

    public AudioValidationService(
        FFprobeService ffprobeService,
        AudioDiagnosticsService audioDiagnosticsService,
        LogService logService)
    {
        _ffprobeService = ffprobeService;
        _audioDiagnosticsService = audioDiagnosticsService;
        _logService = logService;
    }

    public async Task<Result<AudioComparisonReport>> ValidateAsync(
        ProcessingOptions options,
        string outputPath,
        AudioDiagnostics? sourceDiagnostics,
        CancellationToken cancellationToken)
    {
        if (options.SourceInfo is null)
        {
            return Result<AudioComparisonReport>.Failure(LocalizationService.Instance["Error_SourceAnalysisFailed"]);
        }

        var fileCheck = ValidateOutputFile(outputPath);
        if (fileCheck.IsFailure)
        {
            var report = BuildFileProblemReport(options.SourceInfo, outputPath, fileCheck.ErrorMessage ?? LocalizationService.Instance["ValidationSummary_Critical"]);
            return Result<AudioComparisonReport>.Failure(fileCheck.ErrorMessage ?? LocalizationService.Instance["ValidationSummary_Critical"], value: report);
        }

        _logService.Info(LocalizationService.Instance.Format("Log_ValidationStartingFormat", outputPath));

        var outputInfoResult = await _ffprobeService.AnalyzeAsync(outputPath, _logService.Info, cancellationToken);
        if (outputInfoResult.IsFailure || outputInfoResult.Value is null)
        {
            var report = BuildUnreadableOutputReport(options.SourceInfo, outputPath, outputInfoResult.ErrorMessage);
            return Result<AudioComparisonReport>.Failure(
                outputInfoResult.ErrorMessage ?? LocalizationService.Instance["Error_OutputAnalysisFailed"],
                outputInfoResult.Exception,
                report);
        }

        AudioDiagnostics? outputDiagnostics = null;
        var outputDiagnosticsSkipped = options.Preset.IsCopyOnly;
        if (!outputDiagnosticsSkipped)
        {
            var diagnosticsResult = await _audioDiagnosticsService.AnalyzeAsync(
                outputPath,
                outputInfoResult.Value.Duration,
                _logService.Info,
                progress: null,
                cancellationToken,
                outputInfoResult.Value.SelectedAudioStream);

            if (diagnosticsResult.IsSuccess)
            {
                outputDiagnostics = diagnosticsResult.Value;
            }
            else
            {
                outputDiagnosticsSkipped = true;
                _logService.Warning(diagnosticsResult.ErrorMessage ?? LocalizationService.Instance["Error_DiagnosticsFailed"]);
            }
        }

        var reportResult = BuildReport(
            options,
            options.SourceInfo,
            outputInfoResult.Value,
            sourceDiagnostics,
            outputDiagnostics,
            outputDiagnosticsSkipped,
            outputPath);

        _logService.Info(LocalizationService.Instance.Format("Log_ValidationCompleteFormat", reportResult.StatusText));
        return Result<AudioComparisonReport>.Success(reportResult);
    }

    internal static Result ValidateOutputFile(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
        {
            return Result.Failure(LocalizationService.Instance["Error_OutputFileMissingValidation"]);
        }

        var fileInfo = new FileInfo(outputPath);
        return fileInfo.Length > 0
            ? Result.Success()
            : Result.Failure(LocalizationService.Instance["Error_OutputFileEmptyValidation"]);
    }

    internal static AudioComparisonReport BuildReport(
        ProcessingOptions options,
        AudioInfo sourceInfo,
        AudioInfo outputInfo,
        AudioDiagnostics? sourceDiagnostics,
        AudioDiagnostics? outputDiagnostics,
        bool outputDiagnosticsSkipped,
        string outputPath)
    {
        var findings = new List<AudioComparisonFinding>();
        var metrics = BuildMetrics(sourceInfo, outputInfo, sourceDiagnostics, outputDiagnostics);

        AddDurationFinding(sourceInfo, outputInfo, findings);
        AddFormatFindings(options, sourceInfo, outputInfo, findings);
        AddPeakFindings(outputDiagnostics, findings);
        AddLoudnessFinding(options, outputDiagnostics, findings);

        var effectiveExportFormat = ExportFormat.ResolveForPreset(options.Preset, options.ExportFormat);
        if (sourceInfo.IsLikelyLossy && effectiveExportFormat.IsLossless)
        {
            AddFinding(findings, AudioComparisonFindingKind.LossyToLossless, AudioInsightSeverity.Info);
        }

        if (options.Preset.IsCopyOnly)
        {
            AddFinding(findings, AudioComparisonFindingKind.StreamCopyMetadataOnly, AudioInsightSeverity.Info);
        }
        else if (outputDiagnosticsSkipped || outputDiagnostics is null)
        {
            AddFinding(findings, AudioComparisonFindingKind.OutputDiagnosticsMissing, AudioInsightSeverity.Info);
        }

        if (findings.Count == 0)
        {
            AddFinding(findings, AudioComparisonFindingKind.NoIssues, AudioInsightSeverity.Info);
        }

        var status = GetStatus(findings);
        return new AudioComparisonReport(
            status,
            LocalizationService.Instance[$"ValidationStatus_{status}"],
            LocalizationService.Instance[$"ValidationSummary_{status}"],
            outputPath,
            outputInfo,
            outputDiagnostics,
            findings,
            metrics,
            outputDiagnosticsSkipped);
    }

    internal static AudioComparisonReport BuildFileProblemReport(AudioInfo sourceInfo, string outputPath, string message)
    {
        var kind = string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath)
            ? AudioComparisonFindingKind.OutputMissing
            : AudioComparisonFindingKind.OutputEmpty;

        var findings = new List<AudioComparisonFinding>
        {
            CreateFinding(kind, AudioInsightSeverity.Critical)
        };

        var metrics = new List<AudioComparisonMetric>
        {
            new(LocalizationService.Instance["Field_OutputFile"], sourceInfo.FileName, string.IsNullOrWhiteSpace(outputPath) ? "-" : outputPath),
            new(LocalizationService.Instance["Field_Message"], "-", message)
        };

        return new AudioComparisonReport(
            AudioComparisonStatus.Critical,
            LocalizationService.Instance["ValidationStatus_Critical"],
            LocalizationService.Instance["ValidationSummary_Critical"],
            outputPath,
            null,
            null,
            findings,
            metrics,
            outputDiagnosticsSkipped: true);
    }

    internal static AudioComparisonReport BuildUnreadableOutputReport(AudioInfo sourceInfo, string outputPath, string? message)
    {
        var findings = new List<AudioComparisonFinding>
        {
            CreateFinding(AudioComparisonFindingKind.OutputUnreadable, AudioInsightSeverity.Critical)
        };

        var metrics = new List<AudioComparisonMetric>
        {
            new(LocalizationService.Instance["Field_OutputFile"], sourceInfo.FileName, outputPath),
            new(LocalizationService.Instance["Field_Message"], "-", message ?? LocalizationService.Instance["Error_OutputAnalysisFailed"])
        };

        return new AudioComparisonReport(
            AudioComparisonStatus.Critical,
            LocalizationService.Instance["ValidationStatus_Critical"],
            LocalizationService.Instance["ValidationSummary_Critical"],
            outputPath,
            null,
            null,
            findings,
            metrics,
            outputDiagnosticsSkipped: true);
    }

    private static IReadOnlyList<AudioComparisonMetric> BuildMetrics(
        AudioInfo sourceInfo,
        AudioInfo outputInfo,
        AudioDiagnostics? sourceDiagnostics,
        AudioDiagnostics? outputDiagnostics)
    {
        return new[]
        {
            new AudioComparisonMetric(LocalizationService.Instance["Field_Codec"], sourceInfo.CodecDisplay, outputInfo.CodecDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_Container"], sourceInfo.ContainerDisplay, outputInfo.ContainerDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_Duration"], sourceInfo.DurationDisplay, outputInfo.DurationDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_SampleRate"], sourceInfo.SampleRateDisplay, outputInfo.SampleRateDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_Channels"], sourceInfo.ChannelsDisplay, outputInfo.ChannelsDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_FileSize"], sourceInfo.FileSizeDisplay, outputInfo.FileSizeDisplay),
            new AudioComparisonMetric(LocalizationService.Instance["Field_Loudness"], sourceDiagnostics?.IntegratedLoudnessDisplay ?? "-", outputDiagnostics?.IntegratedLoudnessDisplay ?? "-"),
            new AudioComparisonMetric(LocalizationService.Instance["Field_TruePeak"], sourceDiagnostics?.TruePeakDisplay ?? "-", outputDiagnostics?.TruePeakDisplay ?? "-"),
            new AudioComparisonMetric(LocalizationService.Instance["Field_MaxPeak"], sourceDiagnostics?.MaxVolumeDisplay ?? "-", outputDiagnostics?.MaxVolumeDisplay ?? "-"),
            new AudioComparisonMetric(LocalizationService.Instance["Field_MeanVolume"], sourceDiagnostics?.MeanVolumeDisplay ?? "-", outputDiagnostics?.MeanVolumeDisplay ?? "-"),
            new AudioComparisonMetric(LocalizationService.Instance["Field_LoudnessRange"], sourceDiagnostics?.LoudnessRangeDisplay ?? "-", outputDiagnostics?.LoudnessRangeDisplay ?? "-")
        };
    }

    private static void AddDurationFinding(
        AudioInfo sourceInfo,
        AudioInfo outputInfo,
        ICollection<AudioComparisonFinding> findings)
    {
        if (sourceInfo.Duration is null || outputInfo.Duration is null)
        {
            return;
        }

        var sourceSeconds = Math.Max(sourceInfo.Duration.Value.TotalSeconds, 0);
        var diff = Math.Abs(outputInfo.Duration.Value.TotalSeconds - sourceSeconds);
        var warningThreshold = Math.Max(DurationWarningSeconds, sourceSeconds * DurationWarningRatio);
        var criticalThreshold = Math.Max(DurationCriticalSeconds, sourceSeconds * DurationCriticalRatio);

        if (diff > criticalThreshold)
        {
            AddFinding(findings, AudioComparisonFindingKind.DurationMismatch, AudioInsightSeverity.Critical);
        }
        else if (diff > warningThreshold)
        {
            AddFinding(findings, AudioComparisonFindingKind.DurationMismatch, AudioInsightSeverity.Warning);
        }
    }

    private static void AddFormatFindings(
        ProcessingOptions options,
        AudioInfo sourceInfo,
        AudioInfo outputInfo,
        ICollection<AudioComparisonFinding> findings)
    {
        if (!options.Preset.IsCopyOnly)
        {
            var expectedCodec = GetExpectedOutputCodec(options);
            if (!string.IsNullOrWhiteSpace(expectedCodec) &&
                !CodecsMatch(expectedCodec, outputInfo.Codec))
            {
                AddFinding(findings, AudioComparisonFindingKind.CodecMismatch, AudioInsightSeverity.Warning);
            }
        }

        if (GetExpectedSampleRate(options) is { } expectedSampleRate)
        {
            if (outputInfo.SampleRate is > 0 && outputInfo.SampleRate != expectedSampleRate)
            {
                AddFinding(findings, AudioComparisonFindingKind.SampleRateMismatch, AudioInsightSeverity.Warning);
            }
        }
        else if (sourceInfo.SampleRate is > 0 &&
                 outputInfo.SampleRate is > 0 &&
                 sourceInfo.SampleRate != outputInfo.SampleRate)
        {
            AddFinding(findings, AudioComparisonFindingKind.SampleRateMismatch, AudioInsightSeverity.Warning);
        }

        if (sourceInfo.Channels is > 0 &&
            outputInfo.Channels is > 0 &&
            sourceInfo.Channels != outputInfo.Channels)
        {
            AddFinding(findings, AudioComparisonFindingKind.ChannelCountChanged, AudioInsightSeverity.Warning);
        }
    }

    private static void AddPeakFindings(AudioDiagnostics? outputDiagnostics, ICollection<AudioComparisonFinding> findings)
    {
        if (outputDiagnostics is null)
        {
            return;
        }

        var peak = outputDiagnostics.TruePeakDb ?? outputDiagnostics.MaxVolumeDb;
        if (outputDiagnostics.HasPotentialClipping)
        {
            AddFinding(findings, AudioComparisonFindingKind.PotentialClipping, AudioInsightSeverity.Critical);
        }
        else if (peak is >= -1.0)
        {
            AddFinding(findings, AudioComparisonFindingKind.LowHeadroom, AudioInsightSeverity.Warning);
        }
    }

    private static void AddLoudnessFinding(
        ProcessingOptions options,
        AudioDiagnostics? outputDiagnostics,
        ICollection<AudioComparisonFinding> findings)
    {
        if (outputDiagnostics?.IntegratedLoudnessLufs is not { } loudness)
        {
            return;
        }

        var target = GetTargetLoudness(options.Preset);
        if (target is null)
        {
            return;
        }

        if (Math.Abs(loudness - target.Value) > LoudnessToleranceLu)
        {
            AddFinding(findings, AudioComparisonFindingKind.LoudnessOffTarget, AudioInsightSeverity.Warning);
        }
    }

    private static double? GetTargetLoudness(AudioPreset preset)
    {
        if (preset.Id == AudioPreset.Music.Id)
        {
            return -14;
        }

        if (preset.Id == AudioPreset.Speech.Id ||
            preset.Id == AudioPreset.PodcastVoice.Id ||
            preset.Id == AudioPreset.NoisySpeechCleanup.Id)
        {
            return -16;
        }

        return null;
    }

    private static string? GetExpectedOutputCodec(ProcessingOptions options)
    {
        var exportFormat = ExportFormat.ResolveForPreset(options.Preset, options.ExportFormat);
        if (exportFormat.Id == ExportFormat.Wav24.Id || exportFormat.Id == ExportFormat.PremierePro.Id)
        {
            return "pcm_s24le";
        }

        if (exportFormat.Id == ExportFormat.Flac.Id)
        {
            return "flac";
        }

        if (exportFormat.Id == ExportFormat.Mp3_320.Id)
        {
            return "mp3";
        }

        if (exportFormat.Id == ExportFormat.Aac_256.Id)
        {
            return "aac";
        }

        if (exportFormat.Id == ExportFormat.Opus_160.Id || exportFormat.Id == ExportFormat.Opus_192.Id)
        {
            return "opus";
        }

        return null;
    }

    private static int? GetExpectedSampleRate(ProcessingOptions options)
    {
        return options.ExportFormat.Id == ExportFormat.PremierePro.Id ? 48_000 : null;
    }

    private static bool CodecsMatch(string expectedCodec, string actualCodec)
    {
        return NormalizeCodec(expectedCodec).Equals(NormalizeCodec(actualCodec), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodec(string codec)
    {
        return codec.Trim().ToLowerInvariant();
    }

    private static AudioComparisonStatus GetStatus(IReadOnlyList<AudioComparisonFinding> findings)
    {
        if (findings.Any(f => f.Severity == AudioInsightSeverity.Critical))
        {
            return AudioComparisonStatus.Critical;
        }

        return findings.Any(f => f.Severity == AudioInsightSeverity.Warning)
            ? AudioComparisonStatus.Warning
            : AudioComparisonStatus.Passed;
    }

    private static void AddFinding(
        ICollection<AudioComparisonFinding> findings,
        AudioComparisonFindingKind kind,
        AudioInsightSeverity severity)
    {
        findings.Add(CreateFinding(kind, severity));
    }

    private static AudioComparisonFinding CreateFinding(AudioComparisonFindingKind kind, AudioInsightSeverity severity)
    {
        return new AudioComparisonFinding(
            kind,
            severity,
            LocalizationService.Instance[$"AnalysisSeverity_{severity}"],
            LocalizationService.Instance[$"ValidationFinding_{kind}_Title"],
            LocalizationService.Instance[$"ValidationFinding_{kind}_Message"]);
    }
}
