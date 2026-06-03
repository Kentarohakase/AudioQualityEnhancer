using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesSuccessfulOutput()
    {
        var runner = new ProcessRunner();
        var outputLines = new List<string>();

        var result = await runner.RunAsync(
            new ProcessRunOptions(GetCommandProcessorPath(), new[] { "/c", "echo hello" }, outputLines.Add),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.WasCancelled);
        Assert.Contains("hello", result.StandardOutput);
        Assert.Contains(outputLines, line => line.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReturnsFailedExitCode()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessRunOptions(GetCommandProcessorPath(), new[] { "/c", "exit /b 7" }),
            CancellationToken.None);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task RunAsync_CapturesErrorOutput()
    {
        var runner = new ProcessRunner();
        var errorLines = new List<string>();

        var result = await runner.RunAsync(
            new ProcessRunOptions(
                GetCommandProcessorPath(),
                new[] { "/c", "echo problem 1>&2" },
                StandardErrorLine: errorLines.Add),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("problem", result.StandardError);
        Assert.Contains(errorLines, line => line.Contains("problem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ReturnsCancelledResultWhenTokenIsCancelled()
    {
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var result = await runner.RunAsync(
            new ProcessRunOptions(GetCommandProcessorPath(), new[] { "/c", "ping -n 10 127.0.0.1 > nul" }),
            cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.True(result.Duration < TimeSpan.FromSeconds(5));
    }

    private static string GetCommandProcessorPath()
    {
        return Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
    }
}
