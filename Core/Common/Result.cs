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

    /// <summary>Creates a successful typed result (static factory lives on non-generic <see cref="Result"/> to satisfy CA1000).</summary>
    public static Result<T> Success<T>(T value) => new Result<T>(true, value);

    /// <summary>Creates a failed typed result.</summary>
    public static Result<T> Failure<T>(string error, Exception? exception = null) =>
        new Result<T>(false, default, error, exception);
}

/// <summary>
/// Represents the result of an operation that returns a value or an error.
/// </summary>
public class Result<T> : Result
{
    public T? Value { get; }

    internal Result(bool isSuccess, T? value, string? error = null, Exception? exception = null)
        : base(isSuccess, error, exception)
    {
        Value = value;
    }

    public bool TryGetValue(out T value)
    {
        value = Value!;
        return IsSuccess && Value != null;
    }
}
