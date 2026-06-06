namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents the outcome of an operation that may succeed with a value of type
/// <typeparamref name="T"/> or fail with an error message.
/// </summary>
public readonly struct Result<T>
{
    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The success value. Only meaningful when <see cref="IsSuccess"/> is true.</summary>
    public T Value { get; }

    /// <summary>The error message. Only meaningful when <see cref="IsSuccess"/> is false.</summary>
    public string? Error { get; }

    private Result(bool isSuccess, T value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>Creates a successful result wrapping <paramref name="value"/>.</summary>
    public static Result<T> Success(T value) => new(true, value, null);

    /// <summary>Creates a failed result with the given <paramref name="error"/> message.</summary>
    public static Result<T> Failure(string error) => new(false, default!, error);
}
