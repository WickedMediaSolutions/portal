namespace Portal.Protocol;

/// <summary>
/// Represents a protocol-level error transmitted across the wire.
/// This is the wire format for errors; it is distinct from
/// <see cref="Portal.Core.Results.Error"/> which is the internal
/// application-level error representation.
/// </summary>
public sealed class ProtocolError
{
    /// <summary>
    /// Machine-readable error code (e.g., "INVALID_VERSION", "UNKNOWN_MESSAGE_TYPE",
    /// "MISSING_REQUIRED_FIELD", "MALFORMED_JSON", "INVALID_PAYLOAD").
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Human-readable error description.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional additional diagnostic detail.</summary>
    public string? Details { get; init; }

    public ProtocolError()
    {
    }

    public ProtocolError(string code, string message, string? details = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Details = details;
    }

    /// <summary>Creates a <see cref="Portal.Core.Results.Error"/> from this protocol error.</summary>
    public Core.Results.Error ToCoreError() =>
        new(Code, Details is not null ? $"{Message} | {Details}" : Message);

    public override string ToString() =>
        Details is not null
            ? $"[{Code}] {Message} - {Details}"
            : $"[{Code}] {Message}";
}