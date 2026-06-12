using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class FileNameService
{
    internal const string TemporaryFilePrefix = "aqe_";

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
        ".mkv",
        ".mka"
    };

    public string BuildOpenDialogFilter()
    {
        var audio = LocalizationService.Instance["Dialog_FilterAudio"];
        var all = LocalizationService.Instance["Dialog_FilterAll"];
        return $"{audio}|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.opus;*.mp4;*.mkv;*.mka|{all}|*.*";
    }

    public bool IsSupportedInputFile(string path)
    {
        return SupportedInputExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// Expands dropped paths so folders contribute their supported files (recursively,
    /// sorted for a stable queue order) while plain file paths pass through unchanged.
    /// </summary>
    public IReadOnlyList<string> ExpandInputPaths(IEnumerable<string> paths)
    {
        var expanded = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                expanded.AddRange(FindSupportedFiles(path));
            }
            else
            {
                expanded.Add(path);
            }
        }

        return expanded;
    }

    private IReadOnlyList<string> FindSupportedFiles(string directory)
    {
        try
        {
            return Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(IsSupportedInputFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
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
        var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(finalOutputPath));
        var name = $"{TemporaryFilePrefix}{safeBaseName}_{Guid.NewGuid():N}{extension}";
        return Path.Combine(tempDirectory, name);
    }

    public int CleanupTemporaryOutputFiles(string outputDirectory, TimeSpan minimumAge)
    {
        var tempDirectory = Path.Combine(outputDirectory, "Temp");
        if (!Directory.Exists(tempDirectory))
        {
            return 0;
        }

        var cutoff = DateTimeOffset.Now - minimumAge;
        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(tempDirectory, $"{TemporaryFilePrefix}*");
        }
        catch
        {
            return 0;
        }

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
                // Cleanup is best effort; locked temp files are retried on a future run.
            }
        }

        return deleted;
    }

    public CopyOutputSuggestion? SuggestCopyOutput(AudioInfo info)
    {
        var codec = info.Codec.Trim().ToLowerInvariant();
        var loc = LocalizationService.Instance;

        return codec switch
        {
            "mp3" => new CopyOutputSuggestion(".mp3", "MP3", loc["CopyReason_Mp3"]),
            "aac" => new CopyOutputSuggestion(".m4a", "M4A/AAC", loc["CopyReason_Aac"]),
            "alac" => new CopyOutputSuggestion(".m4a", "M4A/ALAC", loc["CopyReason_Alac"]),
            "flac" => new CopyOutputSuggestion(".flac", "FLAC", loc["CopyReason_Flac"]),
            "opus" => new CopyOutputSuggestion(".opus", "Opus", loc["CopyReason_Opus"]),
            "vorbis" => new CopyOutputSuggestion(".ogg", "Ogg Vorbis", loc["CopyReason_Vorbis"]),
            "pcm_s16le" or "pcm_s24le" or "pcm_s32le" or "pcm_f32le" or "pcm_f64le" => new CopyOutputSuggestion(".wav", "WAV/PCM", loc["CopyReason_Pcm"]),
            "ac3" or "eac3" or "dts" => new CopyOutputSuggestion(".mka", "Matroska Audio", loc["CopyReason_Ac3"]),
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
