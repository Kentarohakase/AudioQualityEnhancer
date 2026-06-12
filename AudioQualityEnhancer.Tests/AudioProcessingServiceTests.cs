using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioProcessingServiceTests
{
    [Fact]
    public void EstimateOutputSizeBytes_ScalesWithExportFormat()
    {
        using var info = new AudioInfo
        {
            Codec = "mp3",
            Duration = TimeSpan.FromSeconds(100),
            SampleRate = 48_000,
            Channels = 2,
            FileSizeBytes = 1_000_000
        };

        var wavEstimate = AudioProcessingService.EstimateOutputSizeBytes(
            new ProcessingOptions { Preset = AudioPreset.Music, ExportFormat = ExportFormat.Wav24 }, info);
        var mp3Estimate = AudioProcessingService.EstimateOutputSizeBytes(
            new ProcessingOptions { Preset = AudioPreset.Music, ExportFormat = ExportFormat.Mp3_320 }, info);

        Assert.Equal(100L * 48_000 * 2 * 3, wavEstimate);
        Assert.Equal(100L * 40_000, mp3Estimate);
        Assert.True(wavEstimate > mp3Estimate);
    }

    [Fact]
    public void EstimateOutputSizeBytes_FallsBackToSourceSizeForCopyAndUnknownDuration()
    {
        using var copyInfo = new AudioInfo { Codec = "mp3", FileSizeBytes = 5_000_000, Duration = TimeSpan.FromSeconds(60) };
        using var unknownDuration = new AudioInfo { Codec = "mp3", FileSizeBytes = 3_000_000 };

        Assert.Equal(5_000_000, AudioProcessingService.EstimateOutputSizeBytes(
            new ProcessingOptions { Preset = AudioPreset.ExtractCopy, ExportFormat = ExportFormat.Flac }, copyInfo));
        Assert.Equal(3_000_000, AudioProcessingService.EstimateOutputSizeBytes(
            new ProcessingOptions { Preset = AudioPreset.Music, ExportFormat = ExportFormat.Flac }, unknownDuration));
    }

    [Fact]
    public void EnsureSufficientDiskSpace_PassesForSmallEstimates()
    {
        var result = AudioProcessingService.EnsureSufficientDiskSpace(Path.GetTempPath(), 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EnsureSufficientDiskSpace_FailsForAbsurdEstimates()
    {
        var result = AudioProcessingService.EnsureSufficientDiskSpace(Path.GetTempPath(), long.MaxValue / 2);

        Assert.True(result.IsFailure);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void EnsureOutputDirectoryWritable_CreatesWritableDirectoryAndRemovesProbeFile()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();
        var outputDirectory = Path.Combine(tempDirectory, "exports");

        try
        {
            var result = AudioProcessingService.EnsureOutputDirectoryWritable(outputDirectory);

            Assert.True(result.IsSuccess);
            Assert.True(Directory.Exists(outputDirectory));
            Assert.Empty(Directory.GetFiles(outputDirectory, $"{FileNameService.TemporaryFilePrefix}write-test-*.tmp"));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void EnsureOutputDirectoryWritable_RejectsPathThatIsAFile()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var filePath = Path.Combine(tempDirectory, "not-a-directory");
            File.WriteAllText(filePath, "occupied");

            var result = AudioProcessingService.EnsureOutputDirectoryWritable(filePath);

            Assert.True(result.IsFailure);
            Assert.NotNull(result.Exception);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
