using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class ThemeOption : INotifyPropertyChanged
{
    public ThemeOption(AppTheme theme, string nameKey)
    {
        Theme = theme;
        NameKey = nameKey;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppTheme Theme { get; }

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

    public static ThemeOption Light { get; } = new(AppTheme.Light, "Theme_Light");

    public static ThemeOption Dark { get; } = new(AppTheme.Dark, "Theme_Dark");

    public static IReadOnlyList<ThemeOption> All { get; } = new[] { Light, Dark };
}
