namespace Portal.Protocol;

/// <summary>
/// Protocol V1 room state payload.
/// Pushed by the server via the <c>room.state</c> event to describe the room
/// the character is ACTUALLY standing in.
/// </summary>
/// <remarks>
/// This is the authoritative room "look", read entirely from the live
/// server-side room object. It is deliberately separate from the map
/// payloads: <c>map.snapshot</c> is fog-filtered navigational geometry,
/// whereas this carries the room's real name, its real description, its
/// real exits, the real runtime state of observable doors, and which
/// services the room offers. A client never has to reconstruct the current
/// room from map data.
/// </remarks>
public sealed class RoomStatePayload
{
    /// <summary>Server object id of the room, when the server exposes one.</summary>
    public string? RoomId { get; init; }

    /// <summary>Canonical world room id, when this room is part of the realm.</summary>
    public string? WorldRoomId { get; init; }

    /// <summary>Authoritative room name.</summary>
    public string? Name { get; init; }

    /// <summary>Authoritative room description, already stripped of server markup.</summary>
    public string? Description { get; init; }

    /// <summary>Every exit leaving the room, in server order.</summary>
    public IReadOnlyList<RoomExitRecord> Exits { get; init; } = Array.Empty<RoomExitRecord>();

    /// <summary>Services this room actually offers.</summary>
    public RoomServiceContext Services { get; init; } = new();

    public RoomStatePayload()
    {
    }
}

/// <summary>
/// Protocol V1 single exit record within a room state payload.
/// </summary>
/// <remarks>
/// Dynamic door state appears here — and ONLY here — restricted to doors
/// genuinely observable from the current room. The record intentionally
/// carries no destination data, so a closed or locked door can never leak
/// the undiscovered room beyond it.
/// </remarks>
public sealed class RoomExitRecord
{
    /// <summary>Canonical direction ("north", "up", ...), or null if not directional.</summary>
    public string? Direction { get; init; }

    /// <summary>Exit key as the server names it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Player-facing door name (e.g. "iron gate") when this is a door.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Whether this exit carries door metadata.</summary>
    public bool IsDoor { get; init; }

    /// <summary>Authoritative runtime open/closed state.</summary>
    public bool IsOpen { get; init; }

    /// <summary>Authoritative runtime locked/unlocked state.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Whether this door can be locked/unlocked at all.</summary>
    public bool HasLock { get; init; }

    /// <summary>Server's view of whether an Open request would succeed.</summary>
    public bool CanOpen { get; init; }

    /// <summary>Server's view of whether a Close request would succeed.</summary>
    public bool CanClose { get; init; }

    /// <summary>
    /// Server's view of whether a Lock request would succeed. Excludes key
    /// possession, which is character-specific and stays server-side.
    /// </summary>
    public bool CanLock { get; init; }

    /// <summary>Server's view of whether an Unlock request would succeed.</summary>
    public bool CanUnlock { get; init; }

    public RoomExitRecord()
    {
    }

    /// <summary>
    /// The door's authoritative state spelled out in words, e.g.
    /// "closed, unlocked". This is a display convenience only: the state
    /// itself is the server's, and the client never infers it.
    /// </summary>
    /// <remarks>
    /// Spelling the state out in words (rather than only colouring it) is
    /// what keeps the door panel readable without relying on colour.
    /// </remarks>
    public string DoorStateDisplay
    {
        get
        {
            if (!IsDoor)
                return "open passage";

            var open = IsOpen ? "open" : "closed";
            var locked = IsLocked ? "locked" : "unlocked";
            return $"{open}, {locked}";
        }
    }
}

/// <summary>
/// Protocol V1 room service context.
/// Tells the client exactly which contextual controls are legitimate here.
/// </summary>
public sealed class RoomServiceContext
{
    /// <summary>Shop ids linked to this room; empty when there are none.</summary>
    public IReadOnlyList<string> ShopIds { get; init; } = Array.Empty<string>();

    /// <summary>Whether banking is permitted in this room.</summary>
    public bool Bank { get; init; }

    /// <summary>
    /// Whether this is a designated healing/start room.
    /// </summary>
    /// <remarks>
    /// Informational only. A Keystone healing_room applies a passive
    /// regeneration multiplier while the character stands in it; there is
    /// no healer command, price or instant effect, so no action control is
    /// bound to this flag.
    /// </remarks>
    public bool HealingRoom { get; init; }

    public RoomServiceContext()
    {
    }

    public bool HasShops => ShopIds is { Count: > 0 };
}

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