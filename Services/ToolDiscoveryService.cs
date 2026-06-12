using System.Diagnostics;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class ToolDiscoveryService
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, ToolLocation> _locationCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ToolStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);

    public string ResolveExecutable(string toolName)
    {
        return GetOrLocateTool(toolName).ExecutablePath;
    }

    public async Task<ToolStatus> GetStatusAsync(string toolName, CancellationToken cancellationToken)
    {
        var location = GetOrLocateTool(toolName);

        lock (_cacheLock)
        {
            if (_statusCache.TryGetValue(toolName, out var cachedStatus) &&
                string.Equals(cachedStatus.ExecutablePath, location.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return cachedStatus;
            }
        }

        var status = await ProbeToolAsync(toolName, location, cancellationToken);
        if (status.IsAvailable)
        {
            lock (_cacheLock)
            {
                _statusCache[toolName] = status;
            }
        }

        return status;
    }

    private static async Task<ToolStatus> ProbeToolAsync(string toolName, ToolLocation location, CancellationToken cancellationToken)
    {
        try
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
            process.StartInfo.ArgumentList.Add("-version");

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;
            var versionLine = FirstNonEmptyLine(output) ?? FirstNonEmptyLine(error);

            if (process.ExitCode == 0)
            {
                return new ToolStatus(toolName, location.ExecutablePath, location.Source, true, versionLine, null);
            }

            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.Source,
                false,
                versionLine,
                LocalizationService.Instance.Format("Error_ToolExitCodeFormat", toolName, process.ExitCode));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.Source,
                false,
                null,
                LocalizationService.Instance.Format("Error_ToolNotFoundFormat", toolName));
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

    private static ToolLocation LocateTool(string toolName)
    {
        var exeName = $"{toolName}.exe";
        var appLocalPath = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(appLocalPath))
        {
            return new ToolLocation(appLocalPath, "App-Ordner");
        }

        var toolsPath = Path.Combine(AppContext.BaseDirectory, "Tools", exeName);
        if (File.Exists(toolsPath))
        {
            return new ToolLocation(toolsPath, "Tools-Ordner");
        }

        var pathTool = FindInPath(exeName);
        if (pathTool is not null)
        {
            return new ToolLocation(pathTool, "PATH");
        }

        return new ToolLocation(exeName, "PATH");
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

    private sealed record ToolLocation(string ExecutablePath, string Source);
}
