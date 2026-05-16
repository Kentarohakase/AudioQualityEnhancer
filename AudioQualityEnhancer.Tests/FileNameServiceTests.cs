using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class FileNameServiceTests
{
    [Theory]
    [InlineData("track.mp3")]
    [InlineData("track.WAV")]
    [InlineData("clip.FLAC")]
    [InlineData("voice.m4a")]
    [InlineData("video.MP4")]
    [InlineData("video.mkv")]
    [InlineData("audio.mka")]
    public void IsSupportedInputFile_AcceptsSupportedExtensions(string fileName)
    {
        var service = new FileNameService();

        Assert.True(service.IsSupportedInputFile(fileName));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("")]
    public void IsSupportedInputFile_RejectsUnsupportedExtensions(string fileName)
    {
        var service = new FileNameService();

        Assert.False(service.IsSupportedInputFile(fileName));
    }

    [Fact]
    public void CreateUniqueOutputPath_CreatesDirectoryAndAvoidsExistingTargets()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var service = new FileNameService();
            var inputPath = Path.Combine(tempDirectory, "song.mp3");
            File.WriteAllText(inputPath, "source");
            File.WriteAllText(Path.Combine(tempDirectory, "song_music.flac"), "existing");

            var outputPath = service.CreateUniqueOutputPath(inputPath, tempDirectory, "music", ".flac");

            Assert.EndsWith("song_music_001.flac", outputPath, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateTemporaryOutputPath_UsesTempSubfolderAndPreservesExtension()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var service = new FileNameService();
            var finalPath = Path.Combine(tempDirectory, "result.wav");

            var tempPath = service.CreateTemporaryOutputPath(tempDirectory, finalPath);

            Assert.StartsWith(Path.Combine(tempDirectory, "Temp"), tempPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(".wav", Path.GetExtension(tempPath));
            Assert.True(Directory.Exists(Path.Combine(tempDirectory, "Temp")));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("mp3", ".mp3", "MP3")]
    [InlineData("aac", ".m4a", "M4A/AAC")]
    [InlineData("alac", ".m4a", "M4A/ALAC")]
    [InlineData("flac", ".flac", "FLAC")]
    [InlineData("opus", ".opus", "Opus")]
    [InlineData("vorbis", ".ogg", "Ogg Vorbis")]
    [InlineData("pcm_s24le", ".wav", "WAV/PCM")]
    [InlineData("ac3", ".mka", "Matroska Audio")]
    public void SuggestCopyOutput_ReturnsCompatibleContainer(string codec, string extension, string friendlyName)
    {
        var service = new FileNameService();
        using var info = new AudioInfo { Codec = codec };

        var suggestion = service.SuggestCopyOutput(info);

        Assert.NotNull(suggestion);
        Assert.Equal(extension, suggestion.Extension);
        Assert.Equal(friendlyName, suggestion.FriendlyName);
        Assert.DoesNotContain("!", suggestion.Reason);
    }

    [Fact]
    public void SuggestCopyOutput_ReturnsNullForUnknownCodec()
    {
        var service = new FileNameService();
        using var info = new AudioInfo { Codec = "unknown_codec" };

        Assert.Null(service.SuggestCopyOutput(info));
    }
}
