namespace Nexus.Core.Results;

/// <summary>
/// Represents the outcome of an operation that produces a value on success.
/// </summary>
/// <typeparam name="T">The type of the value produced on success.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    /// <summary>
    /// The value produced by a successful operation.
    /// Accessing this when <see cref="Result.IsFailure"/> throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed result.");

    private Result(T value) : base(Array.Empty<Error>())
    {
        _value = value;
    }

    private Result(IReadOnlyList<Error> errors) : base(errors)
    {
        _value = default;
    }

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with a single error.
    /// </summary>
    public new static Result<T> Failure(Error error) => new(new[] { error });

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    public new static Result<T> Failure(IEnumerable<Error> errors) => new(errors.ToList());

    /// <summary>
    /// Creates a failed result with a single error from code and message.
    /// </summary>
    public new static Result<T> Failure(string code, string message) => Failure(new Error(code, message));

    /// <summary>
    /// Implicitly converts a value to a successful result.
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Implicitly converts an error to a failed result.
    /// </summary>
    public static implicit operator Result<T>(Error error) => Failure(error);
}