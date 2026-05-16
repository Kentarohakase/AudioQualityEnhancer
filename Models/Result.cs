namespace AudioQualityEnhancer.Models;

public class Result
{
    protected Result(bool isSuccess, string? errorMessage, Exception? exception)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public static Result Success()
    {
        return new Result(true, null, null);
    }

    public static Result Failure(string errorMessage, Exception? exception = null)
    {
        return new Result(false, errorMessage, exception);
    }
}

public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, string? errorMessage, Exception? exception)
        : base(isSuccess, errorMessage, exception)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value, null, null);
    }

    public static Result<T> Failure(string errorMessage, Exception? exception = null, T? value = default)
    {
        return new Result<T>(false, value, errorMessage, exception);
    }
}
