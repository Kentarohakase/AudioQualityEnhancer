using System.Diagnostics;

namespace AudioQualityEnhancer.Tests;

public sealed class RepositoryHygieneTests
{
    [Fact]
    public void TrackedTextFiles_DoNotContainNonProductAttributionTerms()
    {
        var files = GetTrackedFiles();
        var blockedTerms = GetBlockedTerms();
        var failures = new List<string>();

        foreach (var relativePath in files.Where(IsTextFile))
        {
            var fullPath = Path.Combine(TestPaths.RepositoryRoot, relativePath);
            var content = File.ReadAllText(fullPath).ToLowerInvariant();

            foreach (var term in blockedTerms)
            {
                if (content.Contains(term, StringComparison.Ordinal))
                {
                    failures.Add($"{relativePath}: {term}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ApplicationManifest_UsesAsInvokerExecutionLevel()
    {
        var projectPath = Path.Combine(TestPaths.RepositoryRoot, "AudioQualityEnhancer.csproj");
        var manifestPath = Path.Combine(TestPaths.RepositoryRoot, "app.manifest");

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", File.ReadAllText(projectPath), StringComparison.Ordinal);
        var manifest = File.ReadAllText(manifestPath);
        Assert.Contains("requestedExecutionLevel", manifest, StringComparison.Ordinal);
        Assert.Contains("level=\"asInvoker\"", manifest, StringComparison.Ordinal);
        Assert.Contains("uiAccess=\"false\"", manifest, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetTrackedFiles()
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = TestPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("ls-files");

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, error);

        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('/', Path.DirectorySeparatorChar))
            .ToArray();
    }

    private static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is
            ".cs" or ".xaml" or ".resx" or ".md" or ".yml" or ".yaml" or
            ".ps1" or ".csproj" or ".slnx" or ".gitignore" or ".txt";
    }

    private static IReadOnlyList<string> GetBlockedTerms()
    {
        return new[]
        {
            Word('c', 'o', 'd', 'e', 'x'),
            Word('c', 'h', 'a', 't', 'g', 'p', 't'),
            Word('o', 'p', 'e', 'n', 'a', 'i'),
            Word('c', 'l', 'a', 'u', 'd', 'e'),
            Word('c', 'o', 'p', 'i', 'l', 'o', 't'),
            Word('g', 'e', 'n', 'e', 'r', 'a', 't', 'e', 'd', ' ', 'b', 'y'),
            Word('c', 'o', '-', 'a', 'u', 't', 'h', 'o', 'r', 'e', 'd', '-', 'b', 'y')
        };
    }

    private static string Word(params char[] chars) => new(chars);
}
