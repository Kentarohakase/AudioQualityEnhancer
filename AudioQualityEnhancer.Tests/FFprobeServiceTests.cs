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
            Assert.Single(info.AudioStreams);
            Assert.Equal(0, info.AudioStreams[0].AudioStreamIndex);
            Assert.Equal("0:0", info.SelectedAudioStream?.FFmpegMapSpecifier);
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
            Assert.Equal(900000, info.AudioStreams[0].BitRate);
            Assert.Equal(TimeSpan.FromSeconds(30.25), info.AudioStreams[0].Duration);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ParseAudioInfo_ReadsMultipleAudioStreamsWithMetadata()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "movie.mkv");
            File.WriteAllText(inputPath, "fake media bytes");
            var json = """
                {
                  "streams": [
                    { "index": 0, "codec_type": "video", "codec_name": "h264" },
                    {
                      "index": 1,
                      "codec_type": "audio",
                      "codec_name": "aac",
                      "codec_long_name": "AAC",
                      "bit_rate": "192000",
                      "sample_rate": "48000",
                      "channels": 2,
                      "duration": "120.0",
                      "tags": { "language": "deu", "title": "Deutsch" }
                    },
                    {
                      "index": 3,
                      "codec_type": "audio",
                      "codec_name": "ac3",
                      "codec_long_name": "ATSC A/52A",
                      "bit_rate": "384000",
                      "sample_rate": "48000",
                      "channels": 6,
                      "tags": { "language": "eng", "handler_name": "Surround" }
                    }
                  ],
                  "format": {
                    "format_name": "matroska,webm",
                    "duration": "125.0"
                  }
                }
                """;

            using var info = FFprobeService.ParseAudioInfo(inputPath, json);

            Assert.NotNull(info);
            Assert.True(info.HasMultipleAudioStreams);
            Assert.Equal(2, info.AudioStreams.Count);
            Assert.Equal("aac", info.Codec);
            Assert.Equal(1, info.SelectedAudioStreamIndex);
            Assert.Equal("Deutsch", info.SelectedAudioStream?.Title);
            Assert.Equal("deu", info.AudioStreams[0].Language);
            Assert.Equal("eng", info.AudioStreams[1].Language);
            Assert.Equal("Surround", info.AudioStreams[1].HandlerName);
            Assert.Equal(3, info.AudioStreams[1].StreamIndex);
            Assert.Equal(1, info.AudioStreams[1].AudioStreamIndex);
            Assert.Equal(TimeSpan.FromSeconds(125), info.AudioStreams[1].Duration);

            using var selectedInfo = info.WithSelectedAudioStream(info.AudioStreams[1]);
            Assert.Equal("ac3", selectedInfo.Codec);
            Assert.Equal(3, selectedInfo.SelectedAudioStreamIndex);
            Assert.Equal(6, selectedInfo.Channels);
            Assert.True(selectedInfo.IsLikelyLossy);
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
