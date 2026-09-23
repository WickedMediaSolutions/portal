namespace Nexus.Core.Results;

/// <summary>
/// Represents an error with a machine-readable code and a human-readable message.
/// </summary>
public sealed class Error
{
    /// <summary>
    /// A machine-readable error code (e.g., "VALIDATION_FAILED", "NOT_FOUND").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// A human-readable description of the error.
    /// </summary>
    public string Message { get; }

    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public override string ToString() => $"[{Code}] {Message}";
}