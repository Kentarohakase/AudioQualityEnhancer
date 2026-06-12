using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class LoudnessTargetOption : INotifyPropertyChanged
{
    public LoudnessTargetOption(string id, string nameKey, string? integratedLufs)
    {
        Id = id;
        NameKey = nameKey;
        IntegratedLufs = integratedLufs;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string NameKey { get; }

    /// <summary>Loudness target in LUFS, or null to use the preset default.</summary>
    public string? IntegratedLufs { get; }

    public string Name => LocalizationService.Instance[NameKey];

    public override string ToString() => Name;

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Item[]")
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public static LoudnessTargetOption Auto { get; } = new("auto", "Loudness_Auto", null);

    public static LoudnessTargetOption Streaming { get; } = new("streaming", "Loudness_Streaming", "-14");

    public static LoudnessTargetOption Podcast { get; } = new("podcast", "Loudness_Podcast", "-16");

    public static LoudnessTargetOption Broadcast { get; } = new("broadcast", "Loudness_Broadcast", "-23");

    public static IReadOnlyList<LoudnessTargetOption> All { get; } = new[] { Auto, Streaming, Podcast, Broadcast };
}
