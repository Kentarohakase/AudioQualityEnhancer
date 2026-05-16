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
            new BatchProcessingItem(@"C:\audio\done.mp3") { Status = BatchProcessingStatus.Done },
            new BatchProcessingItem(@"C:\audio\failed.mp3") { Status = BatchProcessingStatus.Failed },
            new BatchProcessingItem(@"C:\audio\cancelled.mp3") { Status = BatchProcessingStatus.Cancelled }
        };

        try
        {
            var service = new BatchQueueService(new FileNameService());

            var summary = service.BuildSummary(items);

            Assert.Equal(5, summary.Total);
            Assert.Equal(1, summary.Ready);
            Assert.Equal(1, summary.Processing);
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

    private static string CreateFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        return path;
    }
}
