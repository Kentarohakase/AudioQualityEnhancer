using System.Globalization;
using System.Text.Json;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FFprobeService
{
    private readonly FileNameService _fileNameService;
    private readonly ToolDiscoveryService _toolDiscoveryService;
    private readonly IProcessRunner _processRunner;

    public FFprobeService(FileNameService fileNameService, ToolDiscoveryService toolDiscoveryService)
        : this(fileNameService, toolDiscoveryService, new ProcessRunner())
    {
    }

    internal FFprobeService(
        FileNameService fileNameService,
        ToolDiscoveryService toolDiscoveryService,
        IProcessRunner processRunner)
    {
        _fileNameService = fileNameService;
        _toolDiscoveryService = toolDiscoveryService;
        _processRunner = processRunner;
    }

    public async Task<Result> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _toolDiscoveryService.GetStatusAsync("ffprobe", cancellationToken);
            return status.IsAvailable
                ? Result.Success()
                : Result.Failure(status.ErrorMessage ?? LocalizationService.Instance["Error_FFprobeUnavailable"]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result.Failure(LocalizationService.Instance["Error_FFprobeNotFound"], ex);
        }
    }

    public async Task<Result<AudioInfo>> AnalyzeAsync(string inputPath, Action<string>? log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_NoFileSelected"]);
        }

        if (!File.Exists(inputPath))
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_FileNotFound"]);
        }

        if (!_fileNameService.IsSupportedInputFile(inputPath))
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_UnsupportedFormat"]);
        }

        try
        {
            log?.Invoke(LocalizationService.Instance["Log_AnalyzingSourceFFprobe"]);
            var args = new[] { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", inputPath };
            var processResult = await RunProcessAsync(args, cancellationToken);

            if (processResult.WasCancelled)
            {
                return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_AnalysisCancelled"]);
            }

            if (processResult.ExitCode != 0)
            {
                return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_FFprobeAnalysisFailed"]);
            }

            if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            {
                return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_FFprobeNoData"]);
            }

            var audioInfo = ParseAudioInfo(inputPath, processResult.StandardOutput);
            if (audioInfo is null)
            {
                return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_NoAudioStream"]);
            }

            return Result<AudioInfo>.Success(audioInfo);
        }
        catch (OperationCanceledException)
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_AnalysisCancelled"]);
        }
        catch (JsonException ex)
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_FFprobeDataUnreadable"], ex);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_FFprobeNotFound"], ex);
        }
        catch (Exception ex)
        {
            return Result<AudioInfo>.Failure(LocalizationService.Instance["Error_AnalysisFailed"], ex);
        }
    }

    internal static AudioInfo? ParseAudioInfo(string inputPath, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var audioStreams = new List<AudioStreamInfo>();
        var audioStreamIndex = 0;
        foreach (var stream in streams.EnumerateArray())
        {
            if (TryGetString(stream, "codec_type") == "audio")
            {
                audioStreams.Add(ParseAudioStream(stream, audioStreamIndex));
                audioStreamIndex++;
            }
        }

        if (audioStreams.Count == 0)
        {
            return null;
        }

        root.TryGetProperty("format", out var formatElement);

        var selectedStream = audioStreams[0];
        var container = TryGetString(formatElement, "format_name") ?? Path.GetExtension(inputPath).TrimStart('.');
        var formatBitRate = TryGetLong(formatElement, "bit_rate");
        var formatDuration = TryGetDuration(formatElement, "duration");
        var fileSize = new FileInfo(inputPath).Length;

        return new AudioInfo
        {
            SourcePath = inputPath,
            Codec = selectedStream.Codec,
            CodecLongName = selectedStream.CodecLongName,
            BitRate = selectedStream.BitRate ?? formatBitRate,
            SampleRate = selectedStream.SampleRate,
            Channels = selectedStream.Channels,
            Duration = selectedStream.Duration ?? formatDuration,
            Container = container,
            AudioStreams = audioStreams.Select(stream => stream with
            {
                BitRate = stream.BitRate ?? formatBitRate,
                Duration = stream.Duration ?? formatDuration
            }).ToArray(),
            SelectedAudioStreamIndex = selectedStream.StreamIndex,
            FileSizeBytes = fileSize,
            IsLikelyLossy = AudioInfo.IsCodecLikelyLossy(selectedStream.Codec)
        };
    }

    private static AudioStreamInfo ParseAudioStream(JsonElement streamElement, int audioStreamIndex)
    {
        var streamIndex = TryGetInt(streamElement, "index") ?? audioStreamIndex;
        var codec = TryGetString(streamElement, "codec_name") ?? string.Empty;
        var codecLongName = TryGetString(streamElement, "codec_long_name") ?? string.Empty;
        var bitRate = TryGetLong(streamElement, "bit_rate");
        var sampleRate = TryGetInt(streamElement, "sample_rate");
        var channels = TryGetInt(streamElement, "channels");
        var duration = TryGetDuration(streamElement, "duration");

        streamElement.TryGetProperty("tags", out var tagsElement);
        var language = TryGetString(tagsElement, "language") ?? string.Empty;
        var title = TryGetString(tagsElement, "title") ?? string.Empty;
        var handlerName = TryGetString(tagsElement, "handler_name") ?? string.Empty;

        return new AudioStreamInfo(
            streamIndex,
            audioStreamIndex,
            codec,
            codecLongName,
            bitRate,
            sampleRate,
            channels,
            duration,
            language,
            title,
            handlerName);
    }

    private async Task<ProcessResult> RunProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        return await _processRunner.RunAsync(
            new ProcessRunOptions(
                _toolDiscoveryService.ResolveExecutable("ffprobe"),
                arguments,
                InactivityTimeout: FFmpegService.DefaultInactivityTimeout),
            cancellationToken);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind != JsonValueKind.Null)
        {
            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
        }

        return null;
    }

    private static long? TryGetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var numericValue))
        {
            return numericValue;
        }

        var value = TryGetString(element, propertyName);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var numericValue))
        {
            return numericValue;
        }

        var value = TryGetString(element, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static TimeSpan? TryGetDuration(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var numericValue))
        {
            return TimeSpan.FromSeconds(numericValue);
        }

        var value = TryGetString(element, propertyName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

}
