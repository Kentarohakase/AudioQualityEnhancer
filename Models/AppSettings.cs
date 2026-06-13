namespace AudioQualityEnhancer.Models;

public sealed record AppSettings
{
    public string Language { get; init; } = "de";

    public string Theme { get; init; } = "Light";

    public string PresetId { get; init; } = "music";

    public string ExportFormatId { get; init; } = "flac";

    public string OutputDirectory { get; init; } = string.Empty;

    public bool SaveLogFile { get; init; } = true;

    public bool SaveReportFile { get; init; } = true;

    public bool EnableSpeechCompression { get; init; } = false;

    public bool EnableSpeechPresenceBoost { get; init; } = true;

    public bool UseTwoPassLoudness { get; init; } = true;

    public int NoiseReductionFloor { get; init; } = -25;

    public string LoudnessTargetId { get; init; } = "auto";

    public bool EnableNoiseTracking { get; init; } = false;

    public bool YtDlpAutoUpdate { get; init; } = true;

    public string YtDlpLastUpdateCheckUtc { get; init; } = string.Empty;

    public bool SplitChapters { get; init; } = false;

    public bool RemoveSponsorSegments { get; init; } = false;

    public bool DownloadOriginalOnly { get; init; } = false;

    public bool DownloadPlaylist { get; init; } = false;

    public bool CheckForUpdates { get; init; } = true;

    public string AppUpdateLastCheckUtc { get; init; } = string.Empty;

    public double WindowWidth { get; init; }

    public double WindowHeight { get; init; }

    public bool WindowMaximized { get; init; } = false;
}
