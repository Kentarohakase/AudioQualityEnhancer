using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class AudioPreset : INotifyPropertyChanged
{
    public AudioPreset(
        string id,
        string nameKey,
        string descriptionKey,
        string qualityNoteKey,
        bool isCopyOnly = false,
        bool isArchiveExport = false,
        bool isEverydayExport = false)
    {
        Id = id;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        QualityNoteKey = qualityNoteKey;
        IsCopyOnly = isCopyOnly;
        IsArchiveExport = isArchiveExport;
        IsEverydayExport = isEverydayExport;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string NameKey { get; }

    public string DescriptionKey { get; }

    public string QualityNoteKey { get; }

    public string Name => LocalizationService.Instance[NameKey];

    public string Description => LocalizationService.Instance[DescriptionKey];

    public string QualityNote => LocalizationService.Instance[QualityNoteKey];

    public bool IsCopyOnly { get; }

    public bool IsArchiveExport { get; }

    public bool IsEverydayExport { get; }

    public override string ToString() => Name;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QualityNote)));
    }

    public static AudioPreset Music { get; } = new(
        "music",
        "Preset_Music_Name",
        "Preset_Music_Description",
        "Preset_Music_QualityNote");

    public static AudioPreset Speech { get; } = new(
        "speech",
        "Preset_Speech_Name",
        "Preset_Speech_Description",
        "Preset_Speech_QualityNote");

    public static AudioPreset PodcastVoice { get; } = new(
        "podcast_voice",
        "Preset_PodcastVoice_Name",
        "Preset_PodcastVoice_Description",
        "Preset_PodcastVoice_QualityNote");

    public static AudioPreset NoisySpeechCleanup { get; } = new(
        "noisy_speech",
        "Preset_NoisySpeech_Name",
        "Preset_NoisySpeech_Description",
        "Preset_NoisySpeech_QualityNote");

    public static AudioPreset NoiseReduction { get; } = new(
        "noise",
        "Preset_Noise_Name",
        "Preset_Noise_Description",
        "Preset_Noise_QualityNote");

    public static AudioPreset ExtractCopy { get; } = new(
        "copy",
        "Preset_Copy_Name",
        "Preset_Copy_Description",
        "Preset_Copy_QualityNote",
        isCopyOnly: true);

    public static AudioPreset ArchiveExport { get; } = new(
        "archive",
        "Preset_Archive_Name",
        "Preset_Archive_Description",
        "Preset_Archive_QualityNote",
        isArchiveExport: true);

    public static AudioPreset EverydayExport { get; } = new(
        "everyday",
        "Preset_Everyday_Name",
        "Preset_Everyday_Description",
        "Preset_Everyday_QualityNote",
        isEverydayExport: true);

    public static IReadOnlyList<AudioPreset> All { get; } = new[]
    {
        Music,
        Speech,
        PodcastVoice,
        NoisySpeechCleanup,
        NoiseReduction,
        ExtractCopy,
        ArchiveExport,
        EverydayExport
    };
}
