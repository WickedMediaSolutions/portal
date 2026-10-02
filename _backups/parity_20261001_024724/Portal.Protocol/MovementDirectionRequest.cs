namespace Portal.Protocol;

/// <summary>
/// Payload for a Portal-generated directional movement request sent to
/// Keystone.
///
/// Carries only the canonical direction string so Keystone remains fully
/// authoritative over exit resolution, traversal access, locks, destination
/// handling, and all movement hooks. No room or destination identifiers are
/// sent.
/// </summary>
public sealed class MovementDirectionRequest
{
    /// <summary>
    /// The canonical movement direction (e.g. "north", "southeast", "up").
    /// Must be one of the directions supported by Keystone's directional
    /// exits.
    /// </summary>
    public string Direction { get; init; } = string.Empty;

    public MovementDirectionRequest()
    {
    }

    public MovementDirectionRequest(string direction)
    {
        Direction = direction ?? throw new ArgumentNullException(nameof(direction));
    }
}
