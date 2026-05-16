using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FileNameService
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static IReadOnlySet<string> SupportedInputExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".wav",
        ".flac",
        ".m4a",
        ".aac",
        ".ogg",
        ".opus",
        ".mp4",
        ".mkv"
    };

    public string BuildOpenDialogFilter()
    {
        return "Audio- und Videodateien|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mp4;*.mkv|Alle Dateien|*.*";
    }

    public bool IsSupportedInputFile(string path)
    {
        return SupportedInputExtensions.Contains(Path.GetExtension(path));
    }

    public string CreateUniqueOutputPath(string inputPath, string outputDirectory, string suffix, string extension)
    {
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var safeBaseName = SanitizeFileName(baseName);
        var safeSuffix = SanitizeFileName(suffix);
        var candidateName = $"{safeBaseName}_{safeSuffix}{extension}";
        var candidatePath = Path.Combine(outputDirectory, candidateName);
        var inputFullPath = Path.GetFullPath(inputPath);

        var index = 1;
        while (File.Exists(candidatePath) || PathsEqual(candidatePath, inputFullPath))
        {
            candidateName = $"{safeBaseName}_{safeSuffix}_{index:000}{extension}";
            candidatePath = Path.Combine(outputDirectory, candidateName);
            index++;
        }

        return candidatePath;
    }

    public string CreateTemporaryOutputPath(string outputDirectory, string finalOutputPath)
    {
        var tempDirectory = Path.Combine(outputDirectory, "Temp");
        Directory.CreateDirectory(tempDirectory);

        var extension = Path.GetExtension(finalOutputPath);
        var name = $"{Path.GetFileNameWithoutExtension(finalOutputPath)}_{Guid.NewGuid():N}{extension}";
        return Path.Combine(tempDirectory, name);
    }

    public CopyOutputSuggestion? SuggestCopyOutput(AudioInfo info)
    {
        var codec = info.Codec.Trim().ToLowerInvariant();

        return codec switch
        {
            "mp3" => new CopyOutputSuggestion(".mp3", "MP3", "MP3 kann ohne Re-Encoding in eine MP3-Datei extrahiert werden."),
            "aac" => new CopyOutputSuggestion(".m4a", "M4A/AAC", "AAC wird für die verlustfreie Extraktion in einem M4A-Container gespeichert."),
            "alac" => new CopyOutputSuggestion(".m4a", "M4A/ALAC", "ALAC wird für die verlustfreie Extraktion in einem M4A-Container gespeichert."),
            "flac" => new CopyOutputSuggestion(".flac", "FLAC", "FLAC kann ohne Re-Encoding in eine FLAC-Datei extrahiert werden."),
            "opus" => new CopyOutputSuggestion(".opus", "Opus", "Opus kann ohne Re-Encoding in eine Opus-Datei extrahiert werden."),
            "vorbis" => new CopyOutputSuggestion(".ogg", "Ogg Vorbis", "Vorbis wird für die verlustfreie Extraktion in einem Ogg-Container gespeichert."),
            "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" or "pcm_f64le" => new CopyOutputSuggestion(".wav", "WAV/PCM", "PCM-Audio wird ohne Re-Encoding in einem WAV-Container gespeichert."),
            "ac3" or "eac3" or "dts" => new CopyOutputSuggestion(".mka", "Matroska Audio", "Dieser Codec wird für verlustfreie Extraktion in einem MKA-Container gespeichert."),
            _ => null
        };
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = new string(value.Select(ch => InvalidFileNameChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "audio" : sanitized;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CopyOutputSuggestion(string Extension, string FriendlyName, string Reason);
