using AudioQualityEnhancer.Services;

namespace AudioQualityEnhancer.Tests;

public sealed class ToolDiscoveryServiceTests
{
    // where.exe is always on PATH on Windows and "/?" makes it print its help and exit 0,
    // which is the same shape as the "<tool> -version" probe without needing FFmpeg.
    // (cmd.exe is not usable here: "cmd /?" prints help but exits with 1.)
    private const string PathTool = "where";
    private const string PathToolProbeArgument = "/?";
    private const string MissingTool = "audioqualityenhancer-missing-tool";

    [Fact]
    public void ResolveExecutable_ReturnsTheFullPathForAToolOnPath()
    {
        var service = new ToolDiscoveryService();

        var path = service.ResolveExecutable(PathTool);

        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(File.Exists(path));
        Assert.Equal("where.exe", Path.GetFileName(path));
    }

    /// <summary>
    /// An unresolvable tool falls back to the bare executable name so the later process
    /// start produces the normal "not found" error instead of an empty command line.
    /// </summary>
    [Fact]
    public void ResolveExecutable_FallsBackToTheBareNameForAnUnknownTool()
    {
        var service = new ToolDiscoveryService();

        Assert.Equal($"{MissingTool}.exe", service.ResolveExecutable(MissingTool));
    }

    [Fact]
    public void ResolveExecutable_CachesTheLocation()
    {
        var service = new ToolDiscoveryService();

        Assert.Equal(service.ResolveExecutable(PathTool), service.ResolveExecutable(PathTool));
    }

    [Fact]
    public async Task GetStatusAsync_ReportsAToolFoundOnPath()
    {
        var service = new ToolDiscoveryService();

        var status = await service.GetStatusAsync(PathTool, CancellationToken.None, PathToolProbeArgument);

        Assert.True(status.IsAvailable);
        Assert.Equal(PathTool, status.Name);
        Assert.Equal("ToolSource_Path", status.SourceKey);
        Assert.False(string.IsNullOrWhiteSpace(status.VersionLine));
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task GetStatusAsync_CachesASuccessfulProbe()
    {
        var service = new ToolDiscoveryService();

        var first = await service.GetStatusAsync(PathTool, CancellationToken.None, PathToolProbeArgument);
        var second = await service.GetStatusAsync(PathTool, CancellationToken.None, PathToolProbeArgument);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsAMissingToolAsUnavailable()
    {
        var service = new ToolDiscoveryService();

        var status = await service.GetStatusAsync(MissingTool, CancellationToken.None);

        Assert.False(status.IsAvailable);
        Assert.Equal(MissingTool, status.Name);
        Assert.False(string.IsNullOrWhiteSpace(status.ErrorMessage));
    }

    /// <summary>
    /// Probing is bounded by a timeout, so repeating it for a tool that is missing or does
    /// not answer would cost that timeout on every call. The failure is cached instead.
    /// </summary>
    [Fact]
    public async Task GetStatusAsync_CachesAFailedProbe()
    {
        var service = new ToolDiscoveryService();

        var first = await service.GetStatusAsync(MissingTool, CancellationToken.None);
        var second = await service.GetStatusAsync(MissingTool, CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public void FailureCache_ExpiresSoonEnoughToPickUpAnInstalledTool()
    {
        // A tool installed while the app runs has to be picked up again, so the failure
        // must not be remembered for the whole session - but long enough that a repeated
        // status query does not pay the probe timeout again.
        Assert.True(ToolDiscoveryService.FailedStatusCacheDuration > TimeSpan.Zero);
        Assert.True(ToolDiscoveryService.FailedStatusCacheDuration <= TimeSpan.FromMinutes(5));
    }
}
