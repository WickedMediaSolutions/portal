namespace Portal.Networking;

/// <summary>
/// Event arguments carrying the previous and current <see cref="ConnectionState"/>
/// during a state transition.
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>The state before the transition.</summary>
    public ConnectionState OldState { get; }

    /// <summary>The state after the transition.</summary>
    public ConnectionState NewState { get; }

    /// <summary>
    /// Creates a new <see cref="ConnectionStateChangedEventArgs"/>.
    /// </summary>
    public ConnectionStateChangedEventArgs(ConnectionState oldState, ConnectionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}