using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class ToolStatus
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
    }

    public string Name { get; }

    public string ExecutablePath { get; }

    public string Source { get; }

    public bool IsAvailable { get; }

    public string? VersionLine { get; }

    public string? ErrorMessage { get; }

    public string DisplayText => IsAvailable
        ? LocalizationService.Instance.Format("ToolStatus_Found", Name, Source)
        : LocalizationService.Instance.Format("ToolStatus_NotFound", Name);
}
