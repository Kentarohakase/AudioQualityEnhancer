using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class FFprobeServiceTests
{
    [Fact]
    public void ParseAudioInfo_UsesFirstAudioStreamFromVideoContainer()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "clip.mp4");
            File.WriteAllText(inputPath, "fake media bytes");
            var json = """
                {
                  "streams": [
                    { "codec_type": "video", "codec_name": "h264" },
                    {
                      "codec_type": "audio",
                      "codec_name": "aac",
                      "codec_long_name": "AAC (Advanced Audio Coding)",
                      "bit_rate": "192000",
                      "sample_rate": "48000",
                      "channels": 2,
                      "duration": "12.5"
                    }
                  ],
                  "format": {
                    "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
                    "bit_rate": "250000",
                    "duration": "13.0"
                  }
                }
                """;

            using var info = FFprobeService.ParseAudioInfo(inputPath, json);

            Assert.NotNull(info);
            Assert.Equal("aac", info.Codec);
            Assert.Equal("AAC (Advanced Audio Coding)", info.CodecLongName);
            Assert.Equal(192000, info.BitRate);
            Assert.Equal(48000, info.SampleRate);
            Assert.Equal(2, info.Channels);
            Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
            Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", info.Container);
            Assert.True(info.IsLikelyLossy);
            Assert.True(info.FileSizeBytes > 0);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ParseAudioInfo_FallsBackToFormatValuesWhenStreamValuesAreMissing()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "song.flac");
            File.WriteAllText(inputPath, "fake media bytes");
            var json = """
                {
                  "streams": [
                    {
                      "codec_type": "audio",
                      "codec_name": "flac",
                      "sample_rate": 44100,
                      "channels": "2"
                    }
                  ],
                  "format": {
                    "format_name": "flac",
                    "bit_rate": 900000,
                    "duration": 30.25
                  }
                }
                """;

            using var info = FFprobeService.ParseAudioInfo(inputPath, json);

            Assert.NotNull(info);
            Assert.Equal("flac", info.Codec);
            Assert.Equal(900000, info.BitRate);
            Assert.Equal(44100, info.SampleRate);
            Assert.Equal(2, info.Channels);
            Assert.Equal(TimeSpan.FromSeconds(30.25), info.Duration);
            Assert.False(info.IsLikelyLossy);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ParseAudioInfo_ReturnsNullWhenNoAudioStreamExists()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "video.mp4");
            File.WriteAllText(inputPath, "fake media bytes");
            var json = """
                {
                  "streams": [
                    { "codec_type": "video", "codec_name": "h264" }
                  ],
                  "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2" }
                }
                """;

            var info = FFprobeService.ParseAudioInfo(inputPath, json);

            Assert.Null(info);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
