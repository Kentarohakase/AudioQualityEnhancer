namespace AudioQualityEnhancer.Models;

public sealed class ProcessingOptions
{
    public string InputPath { get; init; } = string.Empty;

    public string OutputDirectory { get; init; } = string.Empty;

    public AudioPreset Preset { get; init; } = AudioPreset.Music;

    public ExportFormat ExportFormat { get; init; } = ExportFormat.Flac;

    public AudioInfo? SourceInfo { get; init; }

    public AudioStreamInfo? AudioStream { get; init; }

    public int NoiseReductionFloor { get; init; } = -25;

    /// <summary>Loudness target in LUFS, or null to use the preset default.</summary>
    public string? LoudnessTargetLufs { get; init; }

    public bool EnableNoiseTracking { get; init; }

    public bool EnableSpeechCompression { get; init; }

    public bool EnableSpeechPresenceBoost { get; init; }

    public bool UseTwoPassLoudness { get; init; } = true;
}
