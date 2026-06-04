using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class ExportFormat : INotifyPropertyChanged
{
    public ExportFormat(
        string id,
        string displayNameKey,
        string extension,
        string descriptionKey,
        IReadOnlyList<string> ffmpegArguments,
        bool isLossless)
    {
        Id = id;
        DisplayNameKey = displayNameKey;
        Extension = extension;
        DescriptionKey = descriptionKey;
        FFmpegArguments = ffmpegArguments;
        IsLossless = isLossless;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string DisplayNameKey { get; }

    public string DescriptionKey { get; }

    public string DisplayName => LocalizationService.Instance[DisplayNameKey];

    public string Description => LocalizationService.Instance[DescriptionKey];

    public string Extension { get; }

    public IReadOnlyList<string> FFmpegArguments { get; }

    public bool IsLossless { get; }

    public override string ToString() => DisplayName;

    public static ExportFormat ResolveForPreset(AudioPreset preset, ExportFormat selectedFormat)
    {
        return preset.IsArchiveExport ? Flac : selectedFormat;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
    }

    public static ExportFormat Wav24 { get; } = new(
        "wav24",
        "ExportFormat_Wav24_Name",
        ".wav",
        "ExportFormat_Wav24_Description",
        new[] { "-c:a", "pcm_s24le" },
        isLossless: true);

    public static ExportFormat Flac { get; } = new(
        "flac",
        "ExportFormat_Flac_Name",
        ".flac",
        "ExportFormat_Flac_Description",
        new[] { "-c:a", "flac", "-compression_level", "8" },
        isLossless: true);

    public static ExportFormat PremierePro { get; } = new(
        "premiere_pro",
        "ExportFormat_Premiere_Name",
        ".wav",
        "ExportFormat_Premiere_Description",
        new[] { "-ar", "48000", "-c:a", "pcm_s24le" },
        isLossless: true);

    public static ExportFormat Mp3_320 { get; } = new(
        "mp3_320",
        "ExportFormat_Mp3_Name",
        ".mp3",
        "ExportFormat_Mp3_Description",
        new[] { "-c:a", "libmp3lame", "-b:a", "320k" },
        isLossless: false);

    public static ExportFormat Aac_256 { get; } = new(
        "aac_256",
        "ExportFormat_Aac_Name",
        ".m4a",
        "ExportFormat_Aac_Description",
        new[] { "-c:a", "aac", "-b:a", "256k" },
        isLossless: false);

    public static ExportFormat Opus_160 { get; } = new(
        "opus_160",
        "ExportFormat_Opus160_Name",
        ".opus",
        "ExportFormat_Opus160_Description",
        new[] { "-c:a", "libopus", "-b:a", "160k", "-vbr", "on" },
        isLossless: false);

    public static ExportFormat Opus_192 { get; } = new(
        "opus_192",
        "ExportFormat_Opus192_Name",
        ".opus",
        "ExportFormat_Opus192_Description",
        new[] { "-c:a", "libopus", "-b:a", "192k", "-vbr", "on" },
        isLossless: false);

    public static IReadOnlyList<ExportFormat> All { get; } = new[]
    {
        Wav24,
        Flac,
        PremierePro,
        Mp3_320,
        Aac_256,
        Opus_160,
        Opus_192
    };
}
