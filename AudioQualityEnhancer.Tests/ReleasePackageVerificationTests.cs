using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace AudioQualityEnhancer.Tests;

public sealed class ReleasePackageVerificationTests
{
    [Fact]
    public void VerifyReleasePackage_FailsWhenThirdPartyNoticeIsEmpty()
    {
        const string version = "unit-empty-notice";
        var artifactsDirectory = Path.Combine(TestPaths.RepositoryRoot, "artifacts");
        var zipPath = Path.Combine(artifactsDirectory, $"AudioQualityEnhancer-{version}-win-x64.zip");
        Directory.CreateDirectory(artifactsDirectory);

        try
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "AudioQualityEnhancer.exe", "test");
                AddEntry(archive, "README.md", "test");
                AddEntry(archive, "LICENSE", "test");
                AddEntry(archive, "CHANGELOG.md", "test");
                AddEntry(archive, "THIRD_PARTY_NOTICES.md", string.Empty);
                AddEntry(archive, "Tools/README.md", "test");
            }
            WriteChecksumFile(zipPath);

            var result = RunVerifyScript(version);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("THIRD_PARTY_NOTICES.md is missing or empty", result.Error + result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            DeleteChecksumFile(zipPath);
        }
    }

    [Fact]
    public void VerifyReleasePackage_FailsWhenChecksumDoesNotMatch()
    {
        const string version = "unit-bad-checksum";
        var artifactsDirectory = Path.Combine(TestPaths.RepositoryRoot, "artifacts");
        var zipPath = Path.Combine(artifactsDirectory, $"AudioQualityEnhancer-{version}-win-x64.zip");
        Directory.CreateDirectory(artifactsDirectory);

        try
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                AddEntry(archive, "AudioQualityEnhancer.exe", "test");
                AddEntry(archive, "README.md", "test");
                AddEntry(archive, "LICENSE", "test");
                AddEntry(archive, "CHANGELOG.md", "test");
                AddEntry(archive, "THIRD_PARTY_NOTICES.md", "notice");
                AddEntry(archive, "Tools/README.md", "test");
            }
            File.WriteAllText($"{zipPath}.sha256.txt", $"{new string('0', 64)}  {Path.GetFileName(zipPath)}");

            var result = RunVerifyScript(version);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("SHA256 checksum mismatch", result.Error + result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            DeleteChecksumFile(zipPath);
        }
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteChecksumFile(string zipPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath))).ToLowerInvariant();
        File.WriteAllText($"{zipPath}.sha256.txt", $"{hash}  {Path.GetFileName(zipPath)}");
    }

    private static void DeleteChecksumFile(string zipPath)
    {
        var checksumPath = $"{zipPath}.sha256.txt";
        if (File.Exists(checksumPath))
        {
            File.Delete(checksumPath);
        }
    }

    private static ScriptResult RunVerifyScript(string version)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = TestPaths.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(Path.Combine(TestPaths.RepositoryRoot, "scripts", "verify-release-package.ps1"));
        process.StartInfo.ArgumentList.Add("-Version");
        process.StartInfo.ArgumentList.Add(version);

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, output, error);
    }

    private sealed record ScriptResult(int ExitCode, string Output, string Error);
}
