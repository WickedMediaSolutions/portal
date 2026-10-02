namespace Portal.Protocol;

/// <summary>
/// Protocol V1 room entity snapshot payload.
/// Pushed by the server via the <c>room.entity.snapshot</c> event to convey
/// the complete authoritative list of selectable entities in the character's
/// current room.
/// </summary>
public sealed class RoomEntitySnapshotPayload
{
    /// <summary>
    /// The complete list of entities currently visible/selectable in the room.
    /// </summary>
    public IReadOnlyList<RoomEntityRecord> Entities { get; init; } = Array.Empty<RoomEntityRecord>();

    public RoomEntitySnapshotPayload()
    {
    }

    public RoomEntitySnapshotPayload(IReadOnlyList<RoomEntityRecord> entities)
    {
        Entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }
}

/// <summary>
/// Protocol V1 individual room entity record.
/// Represents a single selectable/visible entity in the character's room.
/// </summary>
public sealed class RoomEntityRecord
{
    /// <summary>Stable unique identifier of the entity (str(object.id)).</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Authoritative display name of the entity.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether the entity is a mob/NPC (true) or a player character (false).</summary>
    public bool IsMob { get; init; }

    /// <summary>Whether the entity is dead.</summary>
    public bool IsDead { get; init; }

    public RoomEntityRecord()
    {
    }

    public RoomEntityRecord(string targetId, string name, bool isMob, bool isDead)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsMob = isMob;
        IsDead = isDead;
    }
}