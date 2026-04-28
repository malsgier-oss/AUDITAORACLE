namespace WorkAudit.Core.Common;

/// <summary>
/// Represents the result of an operation that can succeed or fail with an error message.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public Exception? Exception { get; }

    protected Result(bool isSuccess, string? error = null, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    public static Result Success() => new Result(true);
    public static Result Failure(string error, Exception? exception = null) => new Result(false, error, exception);
}

/// <summary>
/// Represents the result of an operation that returns a value or an error.
/// </summary>
public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error = null, Exception? exception = null)
        : base(isSuccess, error, exception)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new Result<T>(true, value);
    public static new Result<T> Failure(string error, Exception? exception = null) => new Result<T>(false, default, error, exception);

    public bool TryGetValue(out T value)
    {
        value = Value!;
        return IsSuccess && Value != null;
    }
}
