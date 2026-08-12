using System.Globalization;
using AudioQualityEnhancer.Models;
using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AudioInfoTests
{
    /// <summary>
    /// AudioInfo subscribes to the localization service in its constructor so its display
    /// strings follow a language change. That service lives for the whole session, so an
    /// instance that is never disposed stays reachable through it for just as long. This
    /// pins the contract that makes disposal matter.
    /// </summary>
    [Fact]
    public void Dispose_StopsListeningForLanguageChanges()
    {
        var info = new AudioInfo { Codec = "mp3" };
        var notifications = 0;
        var original = LocalizationService.Instance.Culture;

        try
        {
            // Start from a known culture. Another test class may have left the process on
            // English, and assigning the same culture again raises no change at all.
            LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("de");
            info.PropertyChanged += (_, _) => notifications++;

            LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("en");
            var whileSubscribed = notifications;
            Assert.True(whileSubscribed > 0, "A language change has to reach a live instance.");

            info.Dispose();
            LocalizationService.Instance.Culture = CultureInfo.GetCultureInfo("de");

            Assert.Equal(whileSubscribed, notifications);
        }
        finally
        {
            LocalizationService.Instance.Culture = original;
            info.Dispose();
        }
    }

    [Fact]
    public void WithSelectedAudioStream_ReturnsTheSameInstanceWithoutStreams()
    {
        using var info = new AudioInfo { Codec = "mp3" };

        // Returning this is what keeps BatchProcessingItem.SelectAudioStream from handing
        // an instance to SetAudioInfo that would then be disposed and stored at once.
        Assert.Same(info, info.WithSelectedAudioStream(null));
    }
}
