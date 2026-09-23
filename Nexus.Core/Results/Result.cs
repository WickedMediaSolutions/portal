namespace Nexus.Core.Results;

/// <summary>
/// Represents the outcome of an operation: either success or failure with one or more errors.
/// </summary>
public class Result
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The errors associated with a failed operation.
    /// Empty when <see cref="IsSuccess"/> is true.
    /// </summary>
    public IReadOnlyList<Error> Errors { get; }

    protected Result(IReadOnlyList<Error> errors)
    {
        Errors = errors;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => new(Array.Empty<Error>());

    /// <summary>
    /// Creates a failed result with a single error.
    /// </summary>
    public static Result Failure(Error error) => new(new[] { error });

    /// <summary>
    /// Creates a failed result with multiple errors.
    /// </summary>
    public static Result Failure(IEnumerable<Error> errors) => new(errors.ToList());

    /// <summary>
    /// Creates a failed result with a single error from code and message.
    /// </summary>
    public static Result Failure(string code, string message) => Failure(new Error(code, message));
}