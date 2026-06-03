using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioProcessedPreviewServiceTests
{
    [Fact]
    public void BuildRenderArguments_UsesTwentySecondsSelectedStreamAndWavPreview()
    {
        var stream = new AudioStreamInfo(4, 1, "aac", "AAC", 192_000, 48_000, 2, TimeSpan.FromSeconds(30), "eng", "English", string.Empty);

        var args = AudioProcessedPreviewService.BuildRenderArguments("input.mkv", "preview.wav", "highpass=f=80", stream).ToList();

        Assert.Contains("0:4", args);
        Assert.DoesNotContain("0:a:0", args);
        Assert.Equal("20", args[args.IndexOf("-t") + 1]);
        Assert.Equal("highpass=f=80", args[args.IndexOf("-af") + 1]);
        Assert.Equal("pcm_s16le", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("wav", args[args.IndexOf("-f") + 1]);
        Assert.Equal("preview.wav", args[^1]);
    }

    [Theory]
    [MemberData(nameof(SpeechPreviewPresets))]
    public void PreviewFilterGraph_MatchesSinglePassExportPreview(AudioPreset preset)
    {
        var options = new ProcessingOptions
        {
            InputPath = "input.mp3",
            Preset = preset,
            ExportFormat = ExportFormat.Aac_256,
            UseTwoPassLoudness = false
        };

        var filterPlan = AudioFilterPlanner.BuildPlan(options);
        var exportPreview = AudioProcessingService.BuildFilterPreview(options);

        Assert.Equal(exportPreview, filterPlan.FilterGraph);
    }

    [Fact]
    public async Task RenderAsync_FailsWhenInputFileIsMissing()
    {
        var runner = new FakeProcessRunner();
        var service = CreateService(runner, TestPaths.CreateTempDirectory());

        var result = await service.RenderAsync(
            new ProcessingOptions { InputPath = "missing.mp3", Preset = AudioPreset.Music },
            log: null,
            progress: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(runner.LastOptions);
    }

    [Fact]
    public async Task RenderAsync_FailsWhenFFmpegCannotStart()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var runner = new FakeProcessRunner { ExceptionToThrow = new FileNotFoundException("ffmpeg missing") };
            var service = CreateService(runner, tempDirectory);

            var result = await service.RenderAsync(
                new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice },
                log: null,
                progress: null,
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.NotNull(runner.LastOptions);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_CapturesArgumentsAndReturnsCreatedPreview()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var runner = new FakeProcessRunner { CreateOutputFile = true };
            var service = CreateService(runner, tempDirectory);

            var result = await service.RenderAsync(
                new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice },
                log: null,
                progress: null,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(File.Exists(result.Value.OutputPath));
            Assert.NotNull(runner.LastOptions);
            Assert.Contains(runner.LastOptions.Arguments, argument => argument.Contains("loudnorm=I=-16:TP=-1.5:LRA=9", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildCacheKey_IsStableAndChangesWhenPresetFiltersChange()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var options = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice };
            var sameOptions = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice };
            var changedPreset = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.NoisySpeechCleanup };

            Assert.Equal(AudioProcessedPreviewService.BuildCacheKey(options), AudioProcessedPreviewService.BuildCacheKey(sameOptions));
            Assert.NotEqual(AudioProcessedPreviewService.BuildCacheKey(options), AudioProcessedPreviewService.BuildCacheKey(changedPreset));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    public static TheoryData<AudioPreset> SpeechPreviewPresets => new()
    {
        AudioPreset.PodcastVoice,
        AudioPreset.NoisySpeechCleanup
    };

    private static AudioProcessedPreviewService CreateService(FakeProcessRunner runner, string tempDirectory)
    {
        return new AudioProcessedPreviewService(new FFmpegService(new ToolDiscoveryService(), runner), tempDirectory);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public ProcessRunOptions? LastOptions { get; private set; }

        public bool CreateOutputFile { get; init; }

        public Exception? ExceptionToThrow { get; init; }

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken)
        {
            LastOptions = options;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (CreateOutputFile)
            {
                File.WriteAllText(options.Arguments[^1], "fake preview");
            }

            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimeSpan.FromMilliseconds(1)));
        }
    }
}
