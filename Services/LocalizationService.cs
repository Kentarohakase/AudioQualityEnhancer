using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace AudioQualityEnhancer.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    private readonly ResourceManager _resources =
        new("AudioQualityEnhancer.Resources.Strings", typeof(LocalizationService).Assembly);
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (value is null || Equals(_culture, value))
            {
                return;
            }

            _culture = value;
            Thread.CurrentThread.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        }
    }

    public string this[string key] => _resources.GetString(key, _culture) ?? $"!{key}!";

    public string Format(string key, params object?[] args) =>
        string.Format(_culture, this[key], args);
}
