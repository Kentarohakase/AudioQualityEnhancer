using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class YtDlpDownloadServiceTests
{
    [Fact]
    public void BuildArguments_DownloadsBestAudioAndRemuxesWithoutReencoding()
    {
        var args = YtDlpDownloadService.BuildArguments(new YtDlpDownloadRequest(
            "https://example.com/watch?v=abc",
            @"C:\Tools",
            @"C:\out\%(title)s.%(ext)s",
            @"C:\out\chapters\%(section_number)s.%(ext)s",
            SplitChapters: false,
            RemoveSponsorSegments: false));

        Assert.Contains("--no-playlist", args);
        Assert.Contains("bestaudio/best", args);
        Assert.Contains("--embed-metadata", args);
        Assert.Contains("--embed-thumbnail", args);
        Assert.DoesNotContain("--audio-format", args);
        Assert.DoesNotContain("--split-chapters", args);
        Assert.DoesNotContain("--sponsorblock-remove", args);
        Assert.Equal("https://example.com/watch?v=abc", args[^1]);

        AssertFollowedBy(args, "--remux-video", "ogg/m4a/mka");
        AssertFollowedBy(args, "--ffmpeg-location", @"C:\Tools");
        AssertFollowedBy(args, "-o", @"C:\out\%(title)s.%(ext)s");
    }

    [Fact]
    public void BuildArguments_AddsSplitChaptersAndSponsorBlockWhenRequested()
    {
        var args = YtDlpDownloadService.BuildArguments(new YtDlpDownloadRequest(
            "https://example.com/v",
            @"C:\Tools",
            @"C:\out\%(title)s.%(ext)s",
            @"C:\out\chapters\%(section_number)s.%(ext)s",
            SplitChapters: true,
            RemoveSponsorSegments: true));

        Assert.Contains("--split-chapters", args);
        Assert.Contains(@"chapter:C:\out\chapters\%(section_number)s.%(ext)s", args);
        AssertFollowedBy(args, "--sponsorblock-remove", "default");
    }

    [Fact]
    public void BuildArguments_DownloadsWholePlaylistAndIgnoresChapterSplitWhenRequested()
    {
        var args = YtDlpDownloadService.BuildArguments(new YtDlpDownloadRequest(
            "https://example.com/playlist",
            @"C:\Tools",
            @"C:\out\%(title)s.%(ext)s",
            @"C:\out\chapters\%(section_number)s.%(ext)s",
            SplitChapters: true,
            RemoveSponsorSegments: false,
            DownloadPlaylist: true));

        Assert.Contains("--yes-playlist", args);
        Assert.DoesNotContain("--no-playlist", args);
        Assert.DoesNotContain("--split-chapters", args);
        AssertFollowedBy(args, "--playlist-end", "100");
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", true)]
    [InlineData("http://example.com/a", true)]
    [InlineData("ftp://example.com/a", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLikelyValidUrl_AcceptsHttpAndHttpsOnly(string? url, bool expected)
    {
        Assert.Equal(expected, YtDlpDownloadService.IsLikelyValidUrl(url));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", "https://www.youtube.com/watch?v=abc")]
    [InlineData("  https://example.com/x  ", "https://example.com/x")]
    [InlineData("look: https://example.com/y here", "https://example.com/y")]
    [InlineData("no link in here", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractFirstUrl_ReturnsFirstHttpUrlOrNull(string? text, string? expected)
    {
        Assert.Equal(expected, YtDlpDownloadService.ExtractFirstUrl(text));
    }

    [Fact]
    public void CreateExitErrorMessage_AddsFriendlyDetailForNoNetwork()
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
        var result = new ProcessResult(
            1,
            StandardOutput: string.Empty,
            StandardError: "ERROR: Unable to download webpage: getaddrinfo failed",
            Duration: TimeSpan.FromMilliseconds(1));

        var message = YtDlpDownloadService.CreateExitErrorMessage(result);

        Assert.Contains("yt-dlp code 1", message, StringComparison.Ordinal);
        Assert.Contains("internet connection", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateExitErrorMessage_KeepsGenericMessageWhenOutputIsUnknown()
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
        var result = new ProcessResult(
            2,
            StandardOutput: string.Empty,
            StandardError: "some unexpected failure",
            Duration: TimeSpan.FromMilliseconds(1));

        var message = YtDlpDownloadService.CreateExitErrorMessage(result);

        Assert.Equal("Download failed (yt-dlp code 2).", message);
    }

    private static void AssertFollowedBy(IReadOnlyList<string> args, string flag, string expectedValue)
    {
        var index = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == flag)
            {
                index = i;
                break;
            }
        }

        Assert.True(index >= 0, $"Expected argument '{flag}' to be present.");
        Assert.True(index + 1 < args.Count, $"Expected a value after '{flag}'.");
        Assert.Equal(expectedValue, args[index + 1]);
    }
}
