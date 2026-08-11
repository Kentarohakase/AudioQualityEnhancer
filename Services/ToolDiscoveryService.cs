using System.Diagnostics;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class ToolDiscoveryService
{
    // The probe only runs "<tool> -version". A binary that never answers (broken
    // download, unreachable network path) would otherwise block the startup check
    // forever, so it is bounded and reported as unavailable instead.
    internal static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

    // A failed probe used to be repeated on every call. Together with the bounded probe
    // that costs the full timeout each time, so failures are cached as well - but only
    // briefly, so a tool that is installed while the app runs is still picked up.
    internal static readonly TimeSpan FailedStatusCacheDuration = TimeSpan.FromSeconds(30);

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, ToolLocation> _locationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FailedStatus> _failedStatusCache = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveExecutable(string toolName)
    {
        return GetOrLocateTool(toolName).ExecutablePath;
    }

    public async Task<ToolStatus> GetStatusAsync(string toolName, CancellationToken cancellationToken, string versionArgument = "-version")
    {
        var location = GetOrLocateTool(toolName);

        lock (_cacheLock)
        {
            if (_statusCache.TryGetValue(toolName, out var cachedStatus) &&
                string.Equals(cachedStatus.ExecutablePath, location.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return cachedStatus;
            }

            if (_failedStatusCache.TryGetValue(toolName, out var cachedFailure) &&
                string.Equals(cachedFailure.Status.ExecutablePath, location.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                Stopwatch.GetElapsedTime(cachedFailure.Timestamp) < FailedStatusCacheDuration)
            {
                return cachedFailure.Status;
            }
        }

        var status = await ProbeToolAsync(toolName, location, versionArgument, cancellationToken);

        lock (_cacheLock)
        {
            if (status.IsAvailable)
            {
                _statusCache[toolName] = status;
                _failedStatusCache.Remove(toolName);
            }
            else
            {
                _failedStatusCache[toolName] = new FailedStatus(status, Stopwatch.GetTimestamp());
            }
        }

        return status;
    }

    private static async Task<ToolStatus> ProbeToolAsync(string toolName, ToolLocation location, string versionArgument, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = location.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add(versionArgument);

        using var probeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCancellation.CancelAfter(ProbeTimeout);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(probeCancellation.Token);
            var errorTask = process.StandardError.ReadToEndAsync(probeCancellation.Token);
            await process.WaitForExitAsync(probeCancellation.Token);

            var output = await outputTask;
            var error = await errorTask;
            var versionLine = FirstNonEmptyLine(output) ?? FirstNonEmptyLine(error);

            if (process.ExitCode == 0)
            {
                return new ToolStatus(toolName, location.ExecutablePath, location.SourceKey, true, versionLine, null);
            }

            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.SourceKey,
                false,
                versionLine,
                LocalizationService.Instance.Format("Error_ToolExitCodeFormat", toolName, process.ExitCode));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.SourceKey,
                false,
                null,
                LocalizationService.Instance.Format("Error_ToolTimeoutFormat", toolName));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.SourceKey,
                false,
                null,
                LocalizationService.Instance.Format("Error_ToolNotFoundFormat", toolName));
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
            // The probe result is already decided; cleanup must never throw on top of it.
        }
    }

    private ToolLocation GetOrLocateTool(string toolName)
    {
        lock (_cacheLock)
        {
            if (_locationCache.TryGetValue(toolName, out var cachedLocation) && File.Exists(cachedLocation.ExecutablePath))
            {
                return cachedLocation;
            }
        }

        var location = LocateTool(toolName);
        if (File.Exists(location.ExecutablePath))
        {
            lock (_cacheLock)
            {
                _locationCache[toolName] = location;
            }
        }
        else
        {
            // Keep unresolved tools uncached so a tool installed while the app
            // is running is picked up on the next call.
            lock (_cacheLock)
            {
                _locationCache.Remove(toolName);
                _statusCache.Remove(toolName);
            }
        }

        return location;
    }

    public static string GetUserToolsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioQualityEnhancer",
            "tools");
    }

    private static ToolLocation LocateTool(string toolName)
    {
        var exeName = $"{toolName}.exe";

        // A writable per-user tools folder takes precedence so an auto-updated tool
        // (the bundled app folder is read-only under asInvoker) is preferred.
        var userToolPath = Path.Combine(GetUserToolsDirectory(), exeName);
        if (File.Exists(userToolPath))
        {
            return new ToolLocation(userToolPath, "ToolSource_UserTools");
        }

        var appLocalPath = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(appLocalPath))
        {
            return new ToolLocation(appLocalPath, "ToolSource_AppFolder");
        }

        var toolsPath = Path.Combine(AppContext.BaseDirectory, "Tools", exeName);
        if (File.Exists(toolsPath))
        {
            return new ToolLocation(toolsPath, "ToolSource_ToolsFolder");
        }

        var pathTool = FindInPath(exeName);
        if (pathTool is not null)
        {
            return new ToolLocation(pathTool, "ToolSource_Path");
        }

        return new ToolLocation(exeName, "ToolSource_Path");
    }

    private static string? FindInPath(string exeName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string? FirstNonEmptyLine(string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return null;
    }

    private sealed record ToolLocation(string ExecutablePath, string SourceKey);

    private sealed record FailedStatus(ToolStatus Status, long Timestamp);
}
