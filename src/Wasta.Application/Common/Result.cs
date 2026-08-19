namespace Wasta.Application.Common;

/// <summary>
/// An expected failure carrying a stable code. Handlers return these rather
/// than throwing, so the ordinary paths - wrong password, email taken - do not
/// travel as exceptions. Genuine faults still throw.
/// </summary>
public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(string code, string message) => new(false, new Error(code, message));

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    public static Result<T> Failure<T>(string code, string message) =>
        new(default!, false, new Error(code, message));
}

public sealed class Result<T> : Result
{
    internal Result(T value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    private readonly T _value;

    public T Value => IsSuccess
        ? _value
        : throw new InvalidOperationException("Cannot read the value of a failed result.");
}
