namespace Portal.Protocol;

/// <summary>
/// Payload for a Portal-generated target selection command sent to Keystone.
///
/// Contains only the authoritative target identifier so Keystone remains
/// fully authoritative over target validation, state, and emission.
/// </summary>
public sealed class TargetSelectRequest
{
    /// <summary>
    /// The stable Keystone database primary key of the intended target,
    /// serialised as a string (str(object.id)).
    /// </summary>
    public string TargetId { get; init; } = string.Empty;

    public TargetSelectRequest()
    {
    }

    public TargetSelectRequest(string targetId)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }
}