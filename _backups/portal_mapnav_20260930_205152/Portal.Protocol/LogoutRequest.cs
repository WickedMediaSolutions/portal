namespace Portal.Protocol;

/// <summary>
/// Protocol V1 logout request payload.
/// Sent by a client to terminate an existing authenticated session.
/// </summary>
public sealed class LogoutRequest
{
    /// <summary>The session identifier to terminate.</summary>
    public string SessionId { get; init; } = string.Empty;

    public LogoutRequest()
    {
    }

    public LogoutRequest(string sessionId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }
}