using System.Net;
using System.Net.Http;
using System.Text;
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
    [InlineData("https://github.com/Kentarohakase/AudioQualityEnhancer/releases/tag/v0.18.0", "https://github.com/Kentarohakase/AudioQualityEnhancer/releases/tag/v0.18.0")]
    [InlineData("http://github.com/Kentarohakase/AudioQualityEnhancer/releases", AppUpdateService.ReleasesPageUrl)]
    [InlineData("https://example.com/releases", AppUpdateService.ReleasesPageUrl)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", AppUpdateService.ReleasesPageUrl)]
    [InlineData("C:\\Windows\\System32\\cmd.exe", AppUpdateService.ReleasesPageUrl)]
    [InlineData("", AppUpdateService.ReleasesPageUrl)]
    [InlineData(null, AppUpdateService.ReleasesPageUrl)]
    public void ResolveReleaseUrl_AcceptsOnlyHttpsProjectLinks(string? url, string expected)
    {
        Assert.Equal(expected, AppUpdateService.ResolveReleaseUrl(url));
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

    [Fact]
    public async Task CheckAsync_ReportsANewerRelease()
    {
        const string releaseUrl = "https://github.com/Kentarohakase/AudioQualityEnhancer/releases/tag/v0.18.0";
        using var client = CreateClient(Release("v0.18.0", releaseUrl));

        var update = await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal("0.18.0", update!.Version);
        Assert.Equal(releaseUrl, update.Url);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenTheCurrentVersionIsUpToDate()
    {
        using var client = CreateClient(Release("v0.17.0", "https://github.com/Kentarohakase/AudioQualityEnhancer/releases/tag/v0.17.0"));

        Assert.Null(await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_FallsBackToTheReleasesPageForAnUntrustedLink()
    {
        using var client = CreateClient(Release("v0.18.0", "http://example.com/download.exe"));

        var update = await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None);

        Assert.NotNull(update);
        Assert.Equal(AppUpdateService.ReleasesPageUrl, update!.Url);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullForAnErrorResponse()
    {
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullForAMalformedPayload()
    {
        using var client = CreateClient(Json("not json at all"));

        Assert.Null(await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenTheTagIsMissing()
    {
        using var client = CreateClient(Json("""{"html_url":"https://github.com/Kentarohakase/AudioQualityEnhancer"}"""));

        Assert.Null(await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None));
    }

    /// <summary>A cancelled check has to surface as a cancellation, not as "no update".</summary>
    [Fact]
    public async Task CheckAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), cancellation.Token));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenTheHostIsUnreachable()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline")));

        Assert.Null(await new AppUpdateService(client).CheckAsync(new Version(0, 17, 0), CancellationToken.None));
    }

    private static HttpClient CreateClient(HttpResponseMessage response)
    {
        return new HttpClient(new FakeHttpMessageHandler(_ => response));
    }

    private static HttpResponseMessage Release(string tag, string url)
    {
        return Json($$"""{"tag_name":"{{tag}}","html_url":"{{url}}"}""");
    }

    private static HttpResponseMessage Json(string payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }
    }
}
