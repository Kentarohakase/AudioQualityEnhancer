namespace AudioQualityEnhancer.Models;

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool WasCancelled = false,
    string? OutputPath = null);
