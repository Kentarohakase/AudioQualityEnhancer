using AudioQualityEnhancer.Models;

namespace AudioQualityEnhancer.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoErrorMessageAndNoException()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void Failure_KeepsMessageAndException()
    {
        var exception = new InvalidOperationException("boom");
        var result = Result.Failure("failed", exception);

        Assert.True(result.IsFailure);
        Assert.Equal("failed", result.ErrorMessage);
        Assert.Same(exception, result.Exception);
    }

    [Fact]
    public void SuccessOfT_CarriesTheValue()
    {
        var result = Result<string>.Success("output.flac");

        Assert.True(result.IsSuccess);
        Assert.Equal("output.flac", result.Value);
    }

    /// <summary>
    /// A failing step can still produce a partial result - result validation attaches the
    /// report of a critical comparison, for instance. Consumers therefore have to check
    /// the flag rather than assume a value means success.
    /// </summary>
    [Fact]
    public void FailureOfT_CanStillCarryAValue()
    {
        var result = Result<string>.Failure("critical", value: "output.flac");

        Assert.True(result.IsFailure);
        Assert.Equal("critical", result.ErrorMessage);
        Assert.Equal("output.flac", result.Value);
    }

    [Fact]
    public void FailureOfT_WithoutAValueYieldsTheDefault()
    {
        var result = Result<string>.Failure("failed");

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
    }
}
