using System.Diagnostics;
using System.Text;
using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Services;

public sealed class ToolDiscoveryService
{
    public string ResolveExecutable(string toolName)
    {
        return LocateTool(toolName).ExecutablePath;
    }

    public async Task<ToolStatus> GetStatusAsync(string toolName, CancellationToken cancellationToken)
    {
        var location = LocateTool(toolName);

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
                $"{toolName} wurde mit Exit Code {process.ExitCode} beendet.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new ToolStatus(
                toolName,
                location.ExecutablePath,
                location.Source,
                false,
                null,
                $"{toolName}.exe wurde nicht gefunden. Installiere FFmpeg oder lege ffmpeg.exe und ffprobe.exe neben die App oder in den Tools-Ordner.");
        }
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
