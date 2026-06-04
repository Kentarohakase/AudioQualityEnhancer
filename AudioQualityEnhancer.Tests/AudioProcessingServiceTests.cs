using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioProcessingServiceTests
{
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
