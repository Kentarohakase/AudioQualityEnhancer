namespace AudioQualityEnhancer.Models;

public sealed class ExportFormat
{
    public ExportFormat(
        string id,
        string displayName,
        string extension,
        string description,
        IReadOnlyList<string> ffmpegArguments,
        bool isLossless)
    {
        Id = id;
        DisplayName = displayName;
        Extension = extension;
        Description = description;
        FFmpegArguments = ffmpegArguments;
        IsLossless = isLossless;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Extension { get; }

    public string Description { get; }

    public IReadOnlyList<string> FFmpegArguments { get; }

    public bool IsLossless { get; }

    public override string ToString()
    {
        return DisplayName;
    }

    public static ExportFormat Wav24 { get; } = new(
        "wav24",
        "WAV 24 Bit",
        ".wav",
        "Unkomprimiertes PCM mit 24 Bit. Sehr groß, aber verlustfrei nach der Bearbeitung.",
        new[] { "-c:a", "pcm_s24le" },
        isLossless: true);

    public static ExportFormat Flac { get; } = new(
        "flac",
        "FLAC",
        ".flac",
        "Verlustfreie Kompression. Sinnvoll für Archivierung nach der Bearbeitung.",
        new[] { "-c:a", "flac", "-compression_level", "8" },
        isLossless: true);

    public static ExportFormat Mp3_320 { get; } = new(
        "mp3_320",
        "MP3 320k",
        ".mp3",
        "Breit kompatibel, aber verlustbehaftet.",
        new[] { "-c:a", "libmp3lame", "-b:a", "320k" },
        isLossless: false);

    public static ExportFormat Aac_256 { get; } = new(
        "aac_256",
        "AAC 256k",
        ".m4a",
        "Gute Qualität bei moderater Dateigröße.",
        new[] { "-c:a", "aac", "-b:a", "256k" },
        isLossless: false);

    public static ExportFormat Opus_160 { get; } = new(
        "opus_160",
        "Opus 160k",
        ".opus",
        "Effizienter Codec für kleine Dateien bei guter Qualität.",
        new[] { "-c:a", "libopus", "-b:a", "160k", "-vbr", "on" },
        isLossless: false);

    public static ExportFormat Opus_192 { get; } = new(
        "opus_192",
        "Opus 192k",
        ".opus",
        "Effizienter Codec mit mehr Reserve für komplexes Material.",
        new[] { "-c:a", "libopus", "-b:a", "192k", "-vbr", "on" },
        isLossless: false);

    public static IReadOnlyList<ExportFormat> All { get; } = new[]
    {
        Wav24,
        Flac,
        Mp3_320,
        Aac_256,
        Opus_160,
        Opus_192
    };
}
