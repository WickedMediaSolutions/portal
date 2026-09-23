namespace Nexus.Networking;

/// <summary>
/// Strongly-typed representation of the WebSocket connection lifecycle state.
/// Used for state reporting and guarded state transitions.
/// </summary>
public enum ConnectionState
{
    /// <summary>No active connection; ready to connect.</summary>
    Disconnected,

    /// <summary>Connection handshake in progress.</summary>
    Connecting,

    /// <summary>Connection established and operational.</summary>
    Connected,

    /// <summary>Graceful disconnect in progress.</summary>
    Disconnecting,

    /// <summary>
    /// Automatic reconnection in progress.
    /// Foundation for future reconnect logic; not fully implemented at this layer.
    /// </summary>
    Reconnecting,

    /// <summary>
    /// Connection terminated due to an unrecoverable error.
    /// Must be explicitly reset before reconnecting.
    /// </summary>
    Faulted
}