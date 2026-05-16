namespace AudioQualityEnhancer.Models;

public sealed class ProcessingOptions
{
    public string InputPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public AudioPreset Preset { get; init; } = AudioPreset.Music;

    public ExportFormat ExportFormat { get; init; } = ExportFormat.Flac;

    public AudioInfo? SourceInfo { get; init; }

    public int NoiseReductionFloor { get; init; } = -25;

    public bool EnableSpeechCompression { get; init; }

    public bool EnableSpeechPresenceBoost { get; init; }

    public bool UseTwoPassLoudness { get; init; } = true;
}
