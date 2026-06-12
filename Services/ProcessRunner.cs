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
        var lastActivityTimestamp = Stopwatch.GetTimestamp();
        var timedOutFlag = 0;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputClosed.TrySetResult();
                return;
            }

            Interlocked.Exchange(ref lastActivityTimestamp, Stopwatch.GetTimestamp());
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

            Interlocked.Exchange(ref lastActivityTimestamp, Stopwatch.GetTimestamp());
            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }

            options.StandardErrorLine?.Invoke(e.Data);
        };

        using var watchdogStop = new CancellationTokenSource();
        Task? watchdogTask = null;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (options.InactivityTimeout is { } inactivityTimeout)
            {
                watchdogTask = WatchForInactivityAsync(
                    process,
                    inactivityTimeout,
                    () => Interlocked.Read(ref lastActivityTimestamp),
                    () => Interlocked.Exchange(ref timedOutFlag, 1),
                    watchdogStop.Token);
            }

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputClosed.Task, errorClosed.Task);

            return CreateResult(process, stdout, stderr, startedAt, wasCancelled: false, timedOut: timedOutFlag == 1);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await TryWaitForReadersAsync(outputClosed.Task, errorClosed.Task);
            return CreateResult(process, stdout, stderr, startedAt, wasCancelled: true, timedOut: timedOutFlag == 1);
        }
        finally
        {
            watchdogStop.Cancel();
            if (watchdogTask is not null)
            {
                await watchdogTask;
            }
        }
    }

    // Kills the process when both streams stay silent for the configured timeout.
    // FFmpeg is invoked with periodic progress output, so prolonged silence means
    // a genuinely stuck process, not a long-running encode.
    private static async Task WatchForInactivityAsync(
        Process process,
        TimeSpan timeout,
        Func<long> getLastActivityTimestamp,
        Action markTimedOut,
        CancellationToken stopToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Clamp(timeout.TotalMilliseconds / 4, 50, 5000));

        try
        {
            while (!stopToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stopToken);
                var idleTime = Stopwatch.GetElapsedTime(getLastActivityTimestamp());
                if (idleTime >= timeout)
                {
                    markTimedOut();
                    TryKill(process);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The process finished before the watchdog fired.
        }
    }

    private static ProcessResult CreateResult(
        Process process,
        StringBuilder stdout,
        StringBuilder stderr,
        DateTimeOffset startedAt,
        bool wasCancelled,
        bool timedOut)
    {
        return new ProcessResult(
            TryGetExitCode(process, wasCancelled || timedOut ? -1 : 0),
            stdout.ToString(),
            stderr.ToString(),
            DateTimeOffset.Now - startedAt,
            wasCancelled,
            TimedOut: timedOut);
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
