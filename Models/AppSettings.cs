namespace AudioQualityEnhancer.Models;

public sealed record AppSettings
{
    public string Language { get; init; } = "de";

    public string PresetId { get; init; } = "music";

    public string ExportFormatId { get; init; } = "flac";

    public string OutputDirectory { get; init; } = string.Empty;

    public bool SaveLogFile { get; init; } = true;

    public bool EnableSpeechCompression { get; init; } = false;

    public bool EnableSpeechPresenceBoost { get; init; } = true;

    public bool UseTwoPassLoudness { get; init; } = true;

    public int NoiseReductionFloor { get; init; } = -25;
}
