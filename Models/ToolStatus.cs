namespace AudioQualityEnhancer.Models;

public sealed record ToolStatus(
    string Name,
    string ExecutablePath,
    string Source,
    bool IsAvailable,
    string? VersionLine,
    string? ErrorMessage)
{
    public string DisplayText => IsAvailable
        ? $"{Name}: gefunden ({Source})"
        : $"{Name}: nicht gefunden";
}
