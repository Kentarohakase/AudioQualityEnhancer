using System.ComponentModel;
using System.Windows;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class ThemeService : INotifyPropertyChanged
{
    private static readonly Uri LightThemeUri = new("pack://application:,,,/Resources/Themes/LightTheme.xaml", UriKind.Absolute);
    private static readonly Uri DarkThemeUri = new("pack://application:,,,/Resources/Themes/DarkTheme.xaml", UriKind.Absolute);

    public static ThemeService Instance { get; } = new();

    private AppTheme _current = AppTheme.Light;
    private ResourceDictionary? _activeDictionary;

    private ThemeService()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppTheme Current => _current;

    public void Apply(AppTheme theme)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            _current = theme;
            return;
        }

        if (_activeDictionary is not null)
        {
            resources.MergedDictionaries.Remove(_activeDictionary);
        }

        var dictionary = new ResourceDictionary { Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri };
        resources.MergedDictionaries.Insert(0, dictionary);
        _activeDictionary = dictionary;

        if (_current != theme)
        {
            _current = theme;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        }
    }

    public static AppTheme Parse(string? raw)
    {
        return string.Equals(raw, "Dark", StringComparison.OrdinalIgnoreCase) ? AppTheme.Dark : AppTheme.Light;
    }
}
