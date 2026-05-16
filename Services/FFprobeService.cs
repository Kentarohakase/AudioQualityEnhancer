using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FFprobeService
{
    private readonly FileNameService _fileNameService;
    private readonly ToolDiscoveryService _toolDiscoveryService;

    public FFprobeService(FileNameService fileNameService, ToolDiscoveryService toolDiscoveryService)
    {
        _fileNameService = fileNameService;
        _toolDiscoveryService = toolDiscoveryService;
    }

    public async Task<Result> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _toolDiscoveryService.GetStatusAsync("ffprobe", cancellationToken);
            return status.IsAvailable
                ? Result.Success()
                : Result.Failure(status.ErrorMessage ?? "FFprobe ist nicht verfügbar.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result.Failure("FFprobe wurde nicht gefunden. Installiere FFmpeg und stelle sicher, dass ffprobe.exe im PATH liegt oder neben der App liegt.", ex);
        }
    }

    public async Task<Result<AudioInfo>> AnalyzeAsync(string inputPath, Action<string>? log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Result<AudioInfo>.Failure("Bitte wähle zuerst eine Datei aus.");
        }

        if (!File.Exists(inputPath))
        {
            return Result<AudioInfo>.Failure("Die ausgewählte Datei existiert nicht.");
        }

        if (!_fileNameService.IsSupportedInputFile(inputPath))
        {
            return Result<AudioInfo>.Failure("Dieses Dateiformat wird nicht unterstützt. Unterstützt werden mp3, wav, flac, m4a, aac, ogg, opus, mp4 und mkv.");
        }

        try
        {
            log?.Invoke("Analysiere Quelle mit FFprobe...");
            var args = new[] { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", inputPath };
            var processResult = await RunProcessAsync(args, cancellationToken);

            if (processResult.ExitCode != 0)
            {
                return Result<AudioInfo>.Failure("FFprobe konnte die Datei nicht analysieren. Prüfe, ob die Datei eine lesbare Audiospur enthält.");
            }

            if (string.IsNullOrWhiteSpace(processResult.StandardOutput))
            {
                return Result<AudioInfo>.Failure("FFprobe hat keine Analysedaten zurückgegeben.");
            }

            var audioInfo = ParseAudioInfo(inputPath, processResult.StandardOutput);
            if (audioInfo is null)
            {
                return Result<AudioInfo>.Failure("In der Datei wurde keine Audiospur gefunden.");
            }

            return Result<AudioInfo>.Success(audioInfo);
        }
        catch (OperationCanceledException)
        {
            return Result<AudioInfo>.Failure("Analyse wurde abgebrochen.");
        }
        catch (JsonException ex)
        {
            return Result<AudioInfo>.Failure("FFprobe-Daten konnten nicht gelesen werden.", ex);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return Result<AudioInfo>.Failure("FFprobe wurde nicht gefunden. Installiere FFmpeg und stelle sicher, dass ffprobe.exe im PATH liegt oder neben der App liegt.", ex);
        }
        catch (Exception ex)
        {
            return Result<AudioInfo>.Failure("Bei der Analyse ist ein unerwarteter Fehler aufgetreten.", ex);
        }
    }

    private static AudioInfo? ParseAudioInfo(string inputPath, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? audioStream = null;
        foreach (var stream in streams.EnumerateArray())
        {
            if (TryGetString(stream, "codec_type") == "audio")
            {
                audioStream = stream;
                break;
            }
        }

        if (audioStream is null)
        {
            return null;
        }

        var streamElement = audioStream.Value;
        root.TryGetProperty("format", out var formatElement);

        var codec = TryGetString(streamElement, "codec_name") ?? "Unbekannt";
        var codecLongName = TryGetString(streamElement, "codec_long_name") ?? string.Empty;
        var container = TryGetString(formatElement, "format_name") ?? Path.GetExtension(inputPath).TrimStart('.');
        var bitRate = TryGetLong(streamElement, "bit_rate") ?? TryGetLong(formatElement, "bit_rate");
        var sampleRate = TryGetInt(streamElement, "sample_rate");
        var channels = TryGetInt(streamElement, "channels");
        var duration = TryGetDuration(streamElement, "duration") ?? TryGetDuration(formatElement, "duration");
        var fileSize = new FileInfo(inputPath).Length;

        return new AudioInfo
        {
            SourcePath = inputPath,
            Codec = codec,
            CodecLongName = codecLongName,
            BitRate = bitRate,
            SampleRate = sampleRate,
            Channels = channels,
            Duration = duration,
            Container = string.IsNullOrWhiteSpace(container) ? "Unbekannt" : container,
            FileSizeBytes = fileSize,
            IsLikelyLossy = IsLikelyLossy(codec)
        };
    }

    private async Task<ProcessResult> RunProcessAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = _toolDiscoveryService.ResolveExecutable("ffprobe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var startedAt = DateTimeOffset.Now;
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessResult(process.ExitCode, stdout, stderr, DateTimeOffset.Now - startedAt);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after cancellation.
        }
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

    private static bool IsLikelyLossy(string codec)
    {
        var normalized = codec.Trim().ToLowerInvariant();
        return normalized is "mp3" or "aac" or "vorbis" or "opus" or "ac3" or "eac3" or "wma" or "wmav1" or "wmav2" or "amr_nb" or "amr_wb" or "mp2" or "dts";
    }
}
