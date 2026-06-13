using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class AppUpdateServiceTests
{
    [Theory]
    [InlineData("v0.17.0", "0.17.0")]
    [InlineData("0.17.0", "0.17.0")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("v0.17", "0.17.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not-a-version", null)]
    public void ParseVersion_StripsPrefixAndNormalizesToThreeParts(string? tag, string? expected)
    {
        Assert.Equal(expected, AppUpdateService.ParseVersion(tag)?.ToString());
    }

    [Theory]
    [InlineData("0.16.0", "0.17.0", true)]
    [InlineData("0.16.0", "0.16.1", true)]
    [InlineData("0.16.0", "0.16.0", false)]
    [InlineData("0.17.0", "0.16.9", false)]
    [InlineData("1.0.0", "0.99.0", false)]
    public void IsNewer_ComparesMajorMinorPatch(string current, string latest, bool expected)
    {
        Assert.Equal(expected, AppUpdateService.IsNewer(Version.Parse(current), Version.Parse(latest)));
    }
}
