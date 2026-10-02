namespace Portal.Protocol;

/// <summary>
/// Protocol V1 movement failed event payload.
/// Pushed by the server via the <c>movement.failed</c> event when a
/// directional movement request cannot be fulfilled.
/// </summary>
public sealed class MovementFailedPayload
{
    /// <summary>
    /// The canonical normalized direction that failed (e.g. "north").
    /// </summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>
    /// Authoritative human-readable failure message describing why the
    /// movement was rejected.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    public MovementFailedPayload()
    {
    }

    public MovementFailedPayload(string direction, string message)
    {
        Direction = direction ?? throw new ArgumentNullException(nameof(direction));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}