using System.ComponentModel;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class ToolStatus : INotifyPropertyChanged
{
    public ToolStatus(
        string name,
        string executablePath,
        string source,
        bool isAvailable,
        string? versionLine,
        string? errorMessage)
    {
        Name = name;
        ExecutablePath = executablePath;
        Source = source;
        IsAvailable = isAvailable;
        VersionLine = versionLine;
        ErrorMessage = errorMessage;
        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string ExecutablePath { get; }

    public string Source { get; }

    public bool IsAvailable { get; }

    public string? VersionLine { get; }

    public string? ErrorMessage { get; }

    public string DisplayText => IsAvailable
        ? LocalizationService.Instance.Format("ToolStatus_Found", Name, Source)
        : LocalizationService.Instance.Format("ToolStatus_NotFound", Name);

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Item[]")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }
}
