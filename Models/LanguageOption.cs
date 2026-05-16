using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class LanguageOption : INotifyPropertyChanged
{
    public LanguageOption(string code, string nameKey)
    {
        Code = code;
        NameKey = nameKey;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; }

    public string NameKey { get; }

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

    public static LanguageOption German { get; } = new("de", "Language_German");

    public static LanguageOption English { get; } = new("en", "Language_English");

    public static IReadOnlyList<LanguageOption> All { get; } = new[] { German, English };
}
