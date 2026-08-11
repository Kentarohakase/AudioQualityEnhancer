using System.Diagnostics;
using System.Text;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

internal sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(2);

    // The output readers normally finish right after the process exits. A child process
    // that inherited the pipes can keep them open, so the drain is bounded: generous on
    // the normal path (the last lines carry the loudness measurements) and short after
    // a cancellation, where the remaining output is discarded anyway.
    private static readonly TimeSpan ReaderDrainTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CancelDrainTimeout = TimeSpan.FromSeconds(2);

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
        using var timeoutCancellation = new CancellationTokenSource();
        using var exitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
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
                    () =>
                    {
                        Interlocked.Exchange(ref timedOutFlag, 1);
                        timeoutCancellation.Cancel();
                    },
                    watchdogStop.Token);
            }

            await process.WaitForExitAsync(exitCancellation.Token).ConfigureAwait(false);
            await TryWaitForReadersAsync(process, outputClosed.Task, errorClosed.Task, ReaderDrainTimeout).ConfigureAwait(false);

            return CreateResult(process, stdout, stderr, startedAt, wasCancelled: false, timedOut: timedOutFlag == 1);
        }
        catch (OperationCanceledException)
        {
            var timedOut = timedOutFlag == 1;
            TryKill(process);
            await TryWaitForExitAsync(process).ConfigureAwait(false);
            await TryWaitForReadersAsync(process, outputClosed.Task, errorClosed.Task, CancelDrainTimeout).ConfigureAwait(false);
            return CreateResult(
                process,
                stdout,
                stderr,
                startedAt,
                wasCancelled: !timedOut && cancellationToken.IsCancellationRequested,
                timedOut);
        }
        finally
        {
            watchdogStop.Cancel();
            if (watchdogTask is not null)
            {
                await watchdogTask.ConfigureAwait(false);
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
                await Task.Delay(interval, stopToken).ConfigureAwait(false);
                if (HasExited(process))
                {
                    return;
                }

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
            Snapshot(stdout),
            Snapshot(stderr),
            DateTimeOffset.Now - startedAt,
            wasCancelled,
            TimedOut: timedOut);
    }

    private static string Snapshot(StringBuilder value)
    {
        lock (value)
        {
            return value.ToString();
        }
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

    private static async Task TryWaitForExitAsync(Process process)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!HasExited(process) && Stopwatch.GetElapsedTime(startedAt) < TerminationWaitTimeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }
    }

    private static async Task TryWaitForReadersAsync(Process process, Task outputClosed, Task errorClosed, TimeSpan timeout)
    {
        try
        {
            await Task.WhenAll(outputClosed, errorClosed).WaitAsync(timeout).ConfigureAwait(false);
            return;
        }
        catch
        {
            // Best effort drain; a reader that never closes must not hang the run.
        }

        TryCancelOutputRead(process);
        TryCancelErrorRead(process);
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void TryCancelOutputRead(Process process)
    {
        try
        {
            process.CancelOutputRead();
        }
        catch
        {
            // The reader is already closed or was never started.
        }
    }

    private static void TryCancelErrorRead(Process process)
    {
        try
        {
            process.CancelErrorRead();
        }
        catch
        {
            // The reader is already closed or was never started.
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
