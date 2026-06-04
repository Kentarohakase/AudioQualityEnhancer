using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class FFmpegServiceTests
{
    [Fact]
    public void FormatCommand_QuotesWhitespaceAndEscapesQuotesForLogs()
    {
        var command = FFmpegService.FormatCommand(new[] { "-i", @"C:\audio files\voice ""raw"".wav", "-c:a", "flac" });

        Assert.Contains("\"C:\\audio files\\voice \\\"raw\\\".wav\"", command);
        Assert.Contains("-c:a flac", command);
    }

    [Fact]
    public void CreateExitErrorMessage_AddsFriendlyDetailForLockedOutput()
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
        var result = new ProcessResult(
            1,
            StandardOutput: string.Empty,
            StandardError: "Permission denied",
            Duration: TimeSpan.FromMilliseconds(1));

        var message = FFmpegService.CreateExitErrorMessage(result);

        Assert.Contains("FFmpeg exited with code 1", message, StringComparison.Ordinal);
        Assert.Contains("locked or not writable", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateExitErrorMessage_KeepsGenericMessageWhenOutputIsUnknown()
    {
        LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
        var result = new ProcessResult(
            7,
            StandardOutput: string.Empty,
            StandardError: "unexpected failure",
            Duration: TimeSpan.FromMilliseconds(1));

        var message = FFmpegService.CreateExitErrorMessage(result);

        Assert.Equal("FFmpeg exited with code 7. Details are in the log.", message);
    }
}
