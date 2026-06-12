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
        Assert.Equal("pcm_s24le", args[args.IndexOf("-c:a") + 1]);
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

    [Fact]
    public void BuildCacheKey_ChangesWhenFilterOptionsOrSelectedStreamChange()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var firstStream = new AudioStreamInfo(1, 0, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(30), "deu", "Deutsch", string.Empty);
            var secondStream = new AudioStreamInfo(2, 1, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(30), "eng", "English", string.Empty);
            var baseline = new ProcessingOptions
            {
                InputPath = inputPath,
                Preset = AudioPreset.Speech,
                EnableSpeechPresenceBoost = true,
                AudioStream = firstStream
            };
            var changedFilterOption = new ProcessingOptions
            {
                InputPath = inputPath,
                Preset = AudioPreset.Speech,
                EnableSpeechPresenceBoost = false,
                AudioStream = firstStream
            };
            var changedStream = new ProcessingOptions
            {
                InputPath = inputPath,
                Preset = AudioPreset.Speech,
                EnableSpeechPresenceBoost = true,
                AudioStream = secondStream
            };

            Assert.NotEqual(AudioProcessedPreviewService.BuildCacheKey(baseline), AudioProcessedPreviewService.BuildCacheKey(changedFilterOption));
            Assert.NotEqual(AudioProcessedPreviewService.BuildCacheKey(baseline), AudioProcessedPreviewService.BuildCacheKey(changedStream));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_WithTwoPassLoudness_MeasuresSegmentAndRendersLinear()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var runner = new TwoPassFakeRunner();
            var service = CreateService(runner, tempDirectory);

            var result = await service.RenderAsync(
                new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice, UseTwoPassLoudness = true },
                log: null,
                progress: null,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, runner.Invocations.Count);

            var measureArgs = runner.Invocations[0].Arguments;
            Assert.Contains("null", measureArgs);
            Assert.Contains(measureArgs, argument => argument.Contains("print_format=json", StringComparison.Ordinal));

            var renderArgs = runner.Invocations[1].Arguments;
            Assert.Contains(renderArgs, argument =>
                argument.Contains("measured_I=-23.5", StringComparison.Ordinal) &&
                argument.Contains("linear=true", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_FallsBackToSinglePassWhenMeasurementYieldsNoStats()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");
            var runner = new FakeProcessRunner { CreateOutputFile = true };
            var service = CreateService(runner, tempDirectory);

            var result = await service.RenderAsync(
                new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice, UseTwoPassLoudness = true },
                log: null,
                progress: null,
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(runner.LastOptions);
            Assert.Contains(runner.LastOptions.Arguments, argument =>
                argument.Contains("loudnorm=I=-16:TP=-1.5:LRA=9", StringComparison.Ordinal) &&
                !argument.Contains("measured_I", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildCacheKey_ReflectsTwoPassLoudnessOnlyForLoudnessPresets()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var inputPath = Path.Combine(tempDirectory, "voice.mp3");
            File.WriteAllText(inputPath, "fake media");

            var loudnessTwoPass = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice, UseTwoPassLoudness = true };
            var loudnessSinglePass = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.PodcastVoice, UseTwoPassLoudness = false };
            var noiseTwoPass = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.NoiseReduction, UseTwoPassLoudness = true };
            var noiseSinglePass = new ProcessingOptions { InputPath = inputPath, Preset = AudioPreset.NoiseReduction, UseTwoPassLoudness = false };

            Assert.NotEqual(AudioProcessedPreviewService.BuildCacheKey(loudnessTwoPass), AudioProcessedPreviewService.BuildCacheKey(loudnessSinglePass));
            Assert.Equal(AudioProcessedPreviewService.BuildCacheKey(noiseTwoPass), AudioProcessedPreviewService.BuildCacheKey(noiseSinglePass));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CleanupStalePreviews_DeletesOnlyOldProcessedPreviewFiles()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var stalePreview = Path.Combine(tempDirectory, "processed-preview-stale.wav");
            var freshPreview = Path.Combine(tempDirectory, "processed-preview-fresh.wav");
            var unrelated = Path.Combine(tempDirectory, "other-preview.wav");
            File.WriteAllText(stalePreview, "stale");
            File.WriteAllText(freshPreview, "fresh");
            File.WriteAllText(unrelated, "keep");
            File.SetLastWriteTimeUtc(stalePreview, DateTime.UtcNow.AddDays(-3));

            var deleted = AudioProcessedPreviewService.CleanupStalePreviews(tempDirectory, TimeSpan.FromDays(2));

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(stalePreview));
            Assert.True(File.Exists(freshPreview));
            Assert.True(File.Exists(unrelated));
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

    private static AudioProcessedPreviewService CreateService(IProcessRunner runner, string tempDirectory)
    {
        return new AudioProcessedPreviewService(new FFmpegService(new ToolDiscoveryService(), runner), tempDirectory);
    }

    private sealed class TwoPassFakeRunner : IProcessRunner
    {
        public List<ProcessRunOptions> Invocations { get; } = new();

        public Task<ProcessResult> RunAsync(ProcessRunOptions options, CancellationToken cancellationToken)
        {
            Invocations.Add(options);

            if (Invocations.Count == 1)
            {
                const string loudnormJson = """
                    { "input_i" : "-23.5", "input_tp" : "-4.2", "input_lra" : "3.1", "input_thresh" : "-34.6", "target_offset" : "0.3" }
                    """;
                return Task.FromResult(new ProcessResult(0, string.Empty, loudnormJson, TimeSpan.FromMilliseconds(1)));
            }

            File.WriteAllText(options.Arguments[^1], "fake preview");
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimeSpan.FromMilliseconds(1)));
        }
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
