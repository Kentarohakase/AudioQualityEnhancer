using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class AudioProfileSuggestion
{
    public AudioProfileSuggestion(
        string id,
        AudioPreset preset,
        ExportFormat? exportFormat,
        int priority,
        string titleKey,
        string reasonKey,
        string noteKey)
    {
        Id = id;
        Preset = preset;
        ExportFormat = exportFormat;
        Priority = priority;
        TitleKey = titleKey;
        ReasonKey = reasonKey;
        NoteKey = noteKey;
    }

    public string Id { get; }

    public AudioPreset Preset { get; }

    public ExportFormat? ExportFormat { get; }

    public int Priority { get; }

    public string TitleKey { get; }

    public string ReasonKey { get; }

    public string NoteKey { get; }

    public string Title => LocalizationService.Instance[TitleKey];

    public string Reason => LocalizationService.Instance[ReasonKey];

    public string Note => LocalizationService.Instance[NoteKey];

    public string TargetDisplay => ExportFormat is null
        ? LocalizationService.Instance.Format("ProfileAdvice_TargetStreamCopyFormat", Preset.Name)
        : LocalizationService.Instance.Format("ProfileAdvice_TargetFormat", Preset.Name, ExportFormat.DisplayName);
}
