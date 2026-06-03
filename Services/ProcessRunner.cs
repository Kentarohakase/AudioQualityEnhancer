using System.Diagnostics;
using System.Text;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = options.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in options.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedAt = DateTimeOffset.Now;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputClosed.TrySetResult();
                return;
            }

            lock (stdout)
            {
                stdout.AppendLine(e.Data);
            }

            options.StandardOutputLine?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorClosed.TrySetResult();
                return;
            }

            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }

            options.StandardErrorLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputClosed.Task, errorClosed.Task);

            return CreateResult(process, stdout, stderr, startedAt, wasCancelled: false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await TryWaitForReadersAsync(outputClosed.Task, errorClosed.Task);
            return CreateResult(process, stdout, stderr, startedAt, wasCancelled: true);
        }
    }

    private static ProcessResult CreateResult(
        Process process,
        StringBuilder stdout,
        StringBuilder stderr,
        DateTimeOffset startedAt,
        bool wasCancelled)
    {
        return new ProcessResult(
            TryGetExitCode(process, wasCancelled ? -1 : 0),
            stdout.ToString(),
            stderr.ToString(),
            DateTimeOffset.Now - startedAt,
            wasCancelled);
    }

    private static int TryGetExitCode(Process process, int fallback)
    {
        try
        {
            return process.HasExited ? process.ExitCode : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static async Task TryWaitForReadersAsync(Task outputClosed, Task errorClosed)
    {
        try
        {
            await Task.WhenAll(outputClosed, errorClosed).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best effort drain after cancellation.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup after cancellation.
        }
    }
}
