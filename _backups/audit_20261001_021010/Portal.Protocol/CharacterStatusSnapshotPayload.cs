namespace Portal.Protocol;

/// <summary>
/// Protocol V1 character status snapshot payload.
/// Represents the complete authoritative character status/state pushed by
/// the server via the <c>character.status.snapshot</c> event. Not a delta:
/// the payload is the full authoritative status.
///
/// Keystone currently owns no buff / debuff / condition system, so the
/// payload only carries statuses that genuinely exist there — the
/// authoritative <c>CharacterState</c> value and the derived
/// <c>is_alive()</c> flag.
/// </summary>
public sealed class CharacterStatusSnapshotPayload
{
    /// <summary>
    /// Authoritative Keystone CharacterState value:
    /// "standing", "resting", "meditating", "combating", or "dead".
    /// Null when Keystone has no state recorded.
    /// </summary>
    public string? State { get; init; }

    /// <summary>Authoritative CharacterData.is_alive() flag.</summary>
    public bool IsAlive { get; init; }

    public CharacterStatusSnapshotPayload()
    {
    }

    public CharacterStatusSnapshotPayload(string? state, bool isAlive)
    {
        State = state;
        IsAlive = isAlive;
    }
}
