using System.Globalization;

namespace AudioQualityEnhancer.Models;

public sealed class AudioInfo
{
    public string SourcePath { get; init; } = string.Empty;

    public string FileName => Path.GetFileName(SourcePath);

    public string Codec { get; init; } = "Unbekannt";

    public string CodecLongName { get; init; } = string.Empty;

    public long? BitRate { get; init; }

    public int? SampleRate { get; init; }

    public int? Channels { get; init; }

    public TimeSpan? Duration { get; init; }

    public string Container { get; init; } = "Unbekannt";

    public long FileSizeBytes { get; init; }

    public bool IsLikelyLossy { get; init; }

    public string CodecDisplay => string.IsNullOrWhiteSpace(CodecLongName)
        ? Codec
        : $"{Codec} ({CodecLongName})";

    public string BitRateDisplay => BitRate is > 0
        ? $"{BitRate.Value / 1000d:0} kbit/s"
        : "Unbekannt";

    public string SampleRateDisplay => SampleRate is > 0
        ? $"{SampleRate.Value.ToString("N0", CultureInfo.CurrentCulture)} Hz"
        : "Unbekannt";

    public string ChannelsDisplay => Channels switch
    {
        1 => "1 (Mono)",
        2 => "2 (Stereo)",
        > 2 => $"{Channels} Kanäle",
        _ => "Unbekannt"
    };

    public string DurationDisplay => Duration.HasValue
        ? Duration.Value.ToString(Duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.CurrentCulture)
        : "Unbekannt";

    public string FileSizeDisplay => FileSizeBytes > 0
        ? $"{FileSizeBytes / 1024d / 1024d:0.0} MB"
        : "Unbekannt";

    public string LossyDisplay => IsLikelyLossy ? "Wahrscheinlich verlustbehaftet" : "Wahrscheinlich verlustfrei oder unkomprimiert";

    public string LossyWarning => IsLikelyLossy
        ? "Die Quelle ist bereits verlustbehaftet. Die Bearbeitung kann Klang, Lautheit und Verständlichkeit verbessern, aber verlorene Details nicht vollständig wiederherstellen."
        : string.Empty;
}
