using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AudioQualityEnhancer.Services;

public sealed record AppUpdateInfo(string Version, string Url);

// Checks GitHub for a newer release and reports it; the app stays a portable ZIP,
// so this only notifies and links to the download (no silent self-replacement).
public sealed class AppUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Kentarohakase/AudioQualityEnhancer/releases/latest";
    internal const string ReleasesPageUrl = "https://github.com/Kentarohakase/AudioQualityEnhancer/releases/latest";

    private readonly HttpClient _httpClient;

    public AppUpdateService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
    }

    internal AppUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>Returns info about a newer release, or null if up to date or unavailable.</summary>
    public async Task<AppUpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AudioQualityEnhancer", "update-check"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var latest = ParseVersion(tag);
            if (latest is null || !IsNewer(currentVersion, latest))
            {
                return null;
            }

            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            return new AppUpdateInfo(latest.ToString(), string.IsNullOrWhiteSpace(url) ? ReleasesPageUrl : url!);
        }
        catch
        {
            // Update checks are best effort and must never disrupt startup (offline, rate limit, ...).
            return null;
        }
    }

    internal static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(trimmed, out var version) ? Normalize(version) : null;
    }

    internal static bool IsNewer(Version current, Version latest)
    {
        return Normalize(latest) > Normalize(current);
    }

    private static Version Normalize(Version version)
    {
        return new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
    }
}
