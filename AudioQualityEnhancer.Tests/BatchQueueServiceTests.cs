using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class BatchQueueServiceTests
{
    [Fact]
    public void CreateItems_AddsMultipleSupportedFilesAndRejectsInvalidFiles()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var first = CreateFile(tempDirectory, "voice.mp3");
            var second = CreateFile(tempDirectory, "music.flac");
            var invalid = CreateFile(tempDirectory, "notes.txt");
            var service = new BatchQueueService(new FileNameService());

            var result = service.CreateItems(new[] { first, second, invalid }, Array.Empty<BatchProcessingItem>());

            Assert.Equal(2, result.AddedItems.Count);
            Assert.Single(result.RejectedPaths);
            Assert.Contains(result.AddedItems, item => item.SourcePath == Path.GetFullPath(first));
            Assert.Contains(result.AddedItems, item => item.SourcePath == Path.GetFullPath(second));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateItems_RejectsDuplicatePaths()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var source = CreateFile(tempDirectory, "track.wav");
            var service = new BatchQueueService(new FileNameService());
            var existing = new[] { new BatchProcessingItem(source) };

            var result = service.CreateItems(new[] { source }, existing);

            Assert.Empty(result.AddedItems);
            Assert.Single(result.RejectedPaths);

            existing[0].Dispose();
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildSummary_TracksQueueStatusTransitions()
    {
        var items = new[]
        {
            new BatchProcessingItem(@"C:\audio\ready.mp3") { Status = BatchProcessingStatus.Ready },
            new BatchProcessingItem(@"C:\audio\processing.mp3") { Status = BatchProcessingStatus.Processing },
            new BatchProcessingItem(@"C:\audio\validating.mp3") { Status = BatchProcessingStatus.Validating },
            new BatchProcessingItem(@"C:\audio\done.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed },
            new BatchProcessingItem(@"C:\audio\cancelled.mp3") { Status = BatchProcessingStatus.Cancelled }
        };

        try
        {
            var service = new BatchQueueService(new FileNameService());

            var summary = service.BuildSummary(items);

            Assert.Equal(6, summary.Total);
            Assert.Equal(1, summary.Ready);
            Assert.Equal(1, summary.Processing);
            Assert.Equal(1, summary.Validating);
            Assert.Equal(1, summary.Done);
            Assert.Equal(1, summary.Failed);
            Assert.Equal(1, summary.Cancelled);
            Assert.Equal(3, summary.Finished);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    [Fact]
    public void GetProcessableItems_ContinuesPastFailedEntries()
    {
        var items = new[]
        {
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed },
            new BatchProcessingItem(@"C:\audio\ready-one.mp3") { Status = BatchProcessingStatus.Ready },
            new BatchProcessingItem(@"C:\audio\ready-two.mp3") { Status = BatchProcessingStatus.Ready }
        };

        try
        {
            var service = new BatchQueueService(new FileNameService());

            var processable = service.GetProcessableItems(items);

            Assert.Equal(2, processable.Count);
            Assert.DoesNotContain(processable, item => item.Status == BatchProcessingStatus.Failed);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    [Fact]
    public void GetItemsByFilter_ReturnsStatusSpecificEntries()
    {
        var items = new[]
        {
            new BatchProcessingItem(@"C:\audio\ready.mp3") { Status = BatchProcessingStatus.Ready },
            new BatchProcessingItem(@"C:\audio\analyzing.mp3") { Status = BatchProcessingStatus.Analyzing },
            new BatchProcessingItem(@"C:\audio\processing.mp3") { Status = BatchProcessingStatus.Processing },
            new BatchProcessingItem(@"C:\audio\validating.mp3") { Status = BatchProcessingStatus.Validating },
            new BatchProcessingItem(@"C:\audio\done.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\warning.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed },
            new BatchProcessingItem(@"C:\audio\cancelled.mp3") { Status = BatchProcessingStatus.Cancelled }
        };

        try
        {
            items[4].SetComparisonReport(CreateComparisonReport(AudioComparisonStatus.Warning));
            var service = new BatchQueueService(new FileNameService());

            Assert.Single(service.GetItemsByFilter(items, BatchQueueFilter.Ready));
            Assert.Equal(3, service.GetItemsByFilter(items, BatchQueueFilter.Processing).Count);
            Assert.Equal(2, service.GetItemsByFilter(items, BatchQueueFilter.Done).Count);
            Assert.Single(service.GetItemsByFilter(items, BatchQueueFilter.Warnings));
            Assert.Single(service.GetItemsByFilter(items, BatchQueueFilter.Failed));
            Assert.Single(service.GetItemsByFilter(items, BatchQueueFilter.Cancelled));
            Assert.Equal(items.Length, service.GetItemsByFilter(items, BatchQueueFilter.All).Count);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    [Fact]
    public void ResetForRetry_ClearsProcessingResultAndKeepsAnalysis()
    {
        using var item = new BatchProcessingItem(@"C:\audio\track.mp3")
        {
            Status = BatchProcessingStatus.Failed,
            OutputPath = @"C:\audio\track_music.flac",
            ErrorMessage = "failed",
            Progress = 42
        };
        var stream = new AudioStreamInfo(1, 0, "mp3", "MP3", 128_000, 44_100, 2, TimeSpan.FromSeconds(20), string.Empty, string.Empty, string.Empty);
        var sourceInfo = new AudioInfo
        {
            SourcePath = item.SourcePath,
            Codec = "mp3",
            BitRate = 128_000,
            SampleRate = 44_100,
            Channels = 2,
            AudioStreams = new[] { stream },
            SelectedAudioStreamIndex = stream.StreamIndex,
            IsLikelyLossy = true
        };
        item.SetAudioInfo(sourceInfo);
        item.SetAudioDiagnostics(new AudioDiagnostics { MeanVolumeDb = -18 });
        item.SetAnalysisReport(new AudioAnalysisReport(90, AudioAnalysisStatus.Good, "good", "summary", Array.Empty<AudioAnalysisFinding>(), Array.Empty<AudioAnalysisRecommendation>()));
        item.SetOutputInfo(new AudioInfo { SourcePath = item.OutputPath, Codec = "flac" });
        item.SetOutputDiagnostics(new AudioDiagnostics { MeanVolumeDb = -16 });
        item.SetComparisonReport(CreateComparisonReport(AudioComparisonStatus.Critical));
        var service = new BatchQueueService(new FileNameService());

        var reset = service.ResetForRetry(item);

        Assert.True(reset);
        Assert.Equal(BatchProcessingStatus.Ready, item.Status);
        Assert.Equal(0, item.Progress);
        Assert.Equal(string.Empty, item.ErrorMessage);
        Assert.Equal(string.Empty, item.OutputPath);
        Assert.NotNull(item.AudioInfo);
        Assert.NotNull(item.AudioDiagnostics);
        Assert.NotNull(item.AnalysisReport);
        Assert.Null(item.OutputInfo);
        Assert.Null(item.OutputDiagnostics);
        Assert.Null(item.ComparisonReport);
    }

    [Fact]
    public void ResetForRetry_MarksUnanalyzedFailedItemAsPending()
    {
        using var item = new BatchProcessingItem(@"C:\audio\broken.mp3")
        {
            Status = BatchProcessingStatus.Failed,
            ErrorMessage = "analysis failed",
            Progress = 12
        };
        var service = new BatchQueueService(new FileNameService());

        var reset = service.ResetForRetry(item);

        Assert.True(reset);
        Assert.Equal(BatchProcessingStatus.Pending, item.Status);
        Assert.Equal(string.Empty, item.ErrorMessage);
        Assert.Equal(0, item.Progress);
    }

    [Fact]
    public void MarkProcessingStarted_ClearsTransientState()
    {
        using var item = new BatchProcessingItem(@"C:\audio\track.mp3")
        {
            Status = BatchProcessingStatus.Ready,
            ErrorMessage = "old error",
            Progress = 55
        };
        var service = new BatchQueueService(new FileNameService());

        service.MarkProcessingStarted(item);

        Assert.Equal(BatchProcessingStatus.Processing, item.Status);
        Assert.Equal(string.Empty, item.ErrorMessage);
        Assert.Equal(0, item.Progress);
    }

    [Fact]
    public void MarkValidationStarted_UsesDedicatedValidationStatus()
    {
        using var item = new BatchProcessingItem(@"C:\audio\track.mp3")
        {
            Status = BatchProcessingStatus.Processing,
            Progress = 95
        };
        var service = new BatchQueueService(new FileNameService());

        service.MarkValidationStarted(item);

        Assert.Equal(BatchProcessingStatus.Validating, item.Status);
        Assert.Equal(95, item.Progress);
    }

    [Fact]
    public void GetRetryableItems_ReturnsFailedAndCancelledOnly()
    {
        var items = new[]
        {
            new BatchProcessingItem(@"C:\audio\ready.mp3") { Status = BatchProcessingStatus.Ready },
            new BatchProcessingItem(@"C:\audio\done.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed },
            new BatchProcessingItem(@"C:\audio\cancelled.mp3") { Status = BatchProcessingStatus.Cancelled }
        };

        try
        {
            var service = new BatchQueueService(new FileNameService());

            var retryable = service.GetRetryableItems(items);

            Assert.Equal(2, retryable.Count);
            Assert.Contains(retryable, item => item.Status == BatchProcessingStatus.Failed);
            Assert.Contains(retryable, item => item.Status == BatchProcessingStatus.Cancelled);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    [Fact]
    public void FindNextVisibleItem_UsesFilteredItemsAndPreferredIndex()
    {
        var items = new[]
        {
            new BatchProcessingItem(@"C:\audio\done-one.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\ready.mp3") { Status = BatchProcessingStatus.Ready },
            new BatchProcessingItem(@"C:\audio\done-two.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed }
        };

        try
        {
            var service = new BatchQueueService(new FileNameService());

            var firstDone = service.FindNextVisibleItem(items, BatchQueueFilter.Done, preferredIndex: 0);
            var secondDone = service.FindNextVisibleItem(items, BatchQueueFilter.Done, preferredIndex: 1);
            var clampedDone = service.FindNextVisibleItem(items, BatchQueueFilter.Done, preferredIndex: 99);
            var noWarnings = service.FindNextVisibleItem(items, BatchQueueFilter.Warnings, preferredIndex: 0);

            Assert.Same(items[0], firstDone);
            Assert.Same(items[2], secondDone);
            Assert.Same(items[2], clampedDone);
            Assert.Null(noWarnings);
        }
        finally
        {
            foreach (var item in items)
            {
                item.Dispose();
            }
        }
    }

    [Fact]
    public void CalculateOverallProgress_CombinesCompletedItemsAndCurrentProgress()
    {
        var progress = BatchQueueService.CalculateOverallProgress(itemIndex: 1, totalItems: 4, itemProgress: 50);

        Assert.Equal(37.5, progress);
    }

    [Fact]
    public void FileNameService_CreatesUniqueOutputPathsForBatchItems()
    {
        var tempDirectory = TestPaths.CreateTempDirectory();

        try
        {
            var first = CreateFile(tempDirectory, "song.mp3");
            var second = CreateFile(tempDirectory, "song.flac");
            File.WriteAllText(Path.Combine(tempDirectory, "song_music.flac"), "existing");
            var service = new FileNameService();

            var firstOutput = service.CreateUniqueOutputPath(first, tempDirectory, "music", ".flac");
            File.WriteAllText(firstOutput, "reserved");
            var secondOutput = service.CreateUniqueOutputPath(second, tempDirectory, "music", ".flac");

            Assert.NotEqual(firstOutput, secondOutput);
            Assert.DoesNotContain(new[] { first, second }, output => Path.GetFullPath(output) == Path.GetFullPath(firstOutput));
            Assert.DoesNotContain(new[] { first, second }, output => Path.GetFullPath(output) == Path.GetFullPath(secondOutput));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void BatchProcessingItem_KeepsSelectedAudioStreamPerItem()
    {
        var first = new AudioStreamInfo(1, 0, "aac", "AAC", 128_000, 48_000, 2, TimeSpan.FromSeconds(30), "deu", "Deutsch", string.Empty);
        var second = new AudioStreamInfo(2, 1, "ac3", "AC-3", 384_000, 48_000, 6, TimeSpan.FromSeconds(30), "eng", "English", string.Empty);
        using var item = new BatchProcessingItem(@"C:\audio\movie.mkv");
        using var info = new AudioInfo
        {
            SourcePath = item.SourcePath,
            Codec = first.Codec,
            BitRate = first.BitRate,
            SampleRate = first.SampleRate,
            Channels = first.Channels,
            Duration = first.Duration,
            AudioStreams = new[] { first, second },
            SelectedAudioStreamIndex = first.StreamIndex,
            IsLikelyLossy = true
        };

        item.SetAudioInfo(info);
        item.SetAudioDiagnostics(new AudioDiagnostics());
        item.SetAnalysisReport(new AudioAnalysisReport(100, AudioAnalysisStatus.Excellent, "ok", "ok", Array.Empty<AudioAnalysisFinding>(), Array.Empty<AudioAnalysisRecommendation>()));

        item.SelectAudioStream(second);

        Assert.Equal(second.StreamIndex, item.SelectedAudioStream?.StreamIndex);
        Assert.Equal("ac3", item.AudioInfo?.Codec);
        Assert.Equal(6, item.AudioInfo?.Channels);
        Assert.Null(item.AudioDiagnostics);
        Assert.Null(item.AnalysisReport);
    }

    private static string CreateFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        return path;
    }

    private static AudioComparisonReport CreateComparisonReport(AudioComparisonStatus status)
    {
        return new AudioComparisonReport(
            status,
            status.ToString(),
            "summary",
            @"C:\audio\output.flac",
            outputInfo: null,
            outputDiagnostics: null,
            Array.Empty<AudioComparisonFinding>(),
            Array.Empty<AudioComparisonMetric>(),
            outputDiagnosticsSkipped: false);
    }
}
