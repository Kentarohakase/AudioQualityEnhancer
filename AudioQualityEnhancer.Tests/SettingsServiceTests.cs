using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsJsonIsCorrupt()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory, "settings.json");
            File.WriteAllText(settingsPath, "{ broken json");
            var service = new SettingsService(settingsPath);

            var settings = service.Load();

            Assert.Equal(new AppSettings().Language, settings.Language);
            Assert.Equal(new AppSettings().Theme, settings.Theme);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Save_CreatesDirectoryAndWritesReadableSettings()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var settingsPath = Path.Combine(tempDirectory, "nested", "settings.json");
            var service = new SettingsService(settingsPath);
            var settings = new AppSettings { Language = "en", Theme = "dark" };

            service.Save(settings);
            var loaded = service.Load();

            Assert.Equal("en", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Save_DoesNotThrowWhenSettingsPathIsNotWritable()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var service = new SettingsService(tempDirectory);

            var exception = Record.Exception(() => service.Save(new AppSettings { Language = "en" }));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
