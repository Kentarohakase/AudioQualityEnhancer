using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Models;

public sealed class ToolStatus
{
    public ToolStatus(
        string name,
        string executablePath,
        string sourceKey,
        bool isAvailable,
        string? versionLine,
        string? errorMessage)
    {
        Name = name;
        ExecutablePath = executablePath;
        SourceKey = sourceKey;
        IsAvailable = isAvailable;
        VersionLine = versionLine;
        ErrorMessage = errorMessage;
    }

    public string Name { get; }

    public string ExecutablePath { get; }

    /// <summary>Resource key of the place the tool was found in, resolved on display.</summary>
    public string SourceKey { get; }

    public bool IsAvailable { get; }

    public string? VersionLine { get; }

    public string? ErrorMessage { get; }

    public string DisplayText => IsAvailable
        ? LocalizationService.Instance.Format("ToolStatus_Found", Name, LocalizationService.Instance[SourceKey])
        : LocalizationService.Instance.Format("ToolStatus_NotFound", Name);
}
