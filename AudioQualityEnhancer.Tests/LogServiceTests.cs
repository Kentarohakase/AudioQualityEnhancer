using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class LogServiceTests
{
    [Fact]
    public void Write_TrimsOldestLinesWhenBufferGrowsTooLarge()
    {
        var service = new LogService();
        var padding = new string('x', 1000);
        for (var i = 0; i < 1200; i++)
        {
            service.Info($"{i:0000} {padding}");
        }

        var text = service.CurrentText;
        Assert.True(text.Length <= LogService.MaxBufferLength + padding.Length + 64, $"log buffer was not trimmed (length {text.Length})");
        Assert.StartsWith(LogService.TruncationMarker, text, StringComparison.Ordinal);
        Assert.Contains("1199 ", text);
        Assert.DoesNotContain("] 0000 ", text);
    }

    [Fact]
    public void Write_KeepsShortLogsUntrimmed()
    {
        var service = new LogService();
        service.Info("first");
        service.Warning("second");

        Assert.DoesNotContain(LogService.TruncationMarker, service.CurrentText);
        Assert.Contains("first", service.CurrentText);
        Assert.Contains("second", service.CurrentText);
    }

    [Fact]
    public void Write_MasksSensitiveValues()
    {
        var service = new LogService();
        service.Info("calling api with token=abc123 done");

        Assert.Contains("token=***", service.CurrentText);
        Assert.DoesNotContain("abc123", service.CurrentText);
    }
}
