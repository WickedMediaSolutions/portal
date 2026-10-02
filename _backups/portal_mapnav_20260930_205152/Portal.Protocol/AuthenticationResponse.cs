namespace Portal.Protocol;

/// <summary>
/// Protocol V1 authentication response payload.
/// Returned by the server in response to an <see cref="AuthenticationRequest"/>.
/// </summary>
public sealed class AuthenticationResponse
{
    /// <summary>True if authentication succeeded and a session was established.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// The authenticated session information.
    /// Present only when <see cref="Success"/> is true.
    /// </summary>
    public SessionInfo? Session { get; init; }

    /// <summary>
    /// Machine-readable error code when authentication fails.
    /// Null when <see cref="Success"/> is true.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error description when authentication fails.
    /// Null when <see cref="Success"/> is true.
    /// </summary>
    public string? ErrorMessage { get; init; }

    public AuthenticationResponse()
    {
    }

    public AuthenticationResponse(bool success, SessionInfo? session, string? errorCode, string? errorMessage)
    {
        Success = success;
        Session = session;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}