namespace Nexus.Protocol;

/// <summary>
/// Protocol V1 session terminated event payload.
/// Pushed by the server to notify a client that a session has been terminated,
/// whether by explicit logout, timeout, or administrative action.
/// </summary>
public sealed class SessionTerminatedEvent
{
    /// <summary>The session identifier that was terminated.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Machine-readable reason code for the termination.</summary>
    public string ReasonCode { get; init; } = string.Empty;

    /// <summary>Human-readable description of the termination reason.</summary>
    public string ReasonMessage { get; init; } = string.Empty;

    public SessionTerminatedEvent()
    {
    }

    public SessionTerminatedEvent(string sessionId, string reasonCode, string reasonMessage)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ReasonCode = reasonCode ?? throw new ArgumentNullException(nameof(reasonCode));
        ReasonMessage = reasonMessage ?? throw new ArgumentNullException(nameof(reasonMessage));
    }
}