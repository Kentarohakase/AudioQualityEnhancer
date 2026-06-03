using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken);
}

internal sealed record ProcessRunOptions(
    string FileName,
    IReadOnlyList<string> Arguments,
    Action<string>? StandardOutputLine = null,
    Action<string>? StandardErrorLine = null);
