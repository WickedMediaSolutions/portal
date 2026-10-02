using System.Text.Json.Serialization;

namespace Portal.Protocol;

/// <summary>
/// Protocol V1 <c>map.room.discovered</c> payload — the DELTA describing one
/// room the character has just entered for the first time.
/// </summary>
/// <remarks>
/// Keystone emits this only AFTER a genuine arrival, so the room it carries is
/// always legitimately discovered. The payload is not just a room: it is
/// everything about the client's graph that this discovery CHANGED, so the map
/// updates immediately and no full <c>map.snapshot</c> is needed between moves.
/// <para>
/// Keystone remains authoritative: Portal applies exactly the edges and frontier
/// stubs listed here and never derives any connection of its own. Applying the
/// same event twice is a no-op, because rooms are keyed by
/// <c>world_room_id</c>, edges by (<c>from</c>, <c>direction</c>, <c>to</c>) and
/// frontier stubs by (<c>from</c>, <c>direction</c>).
/// </para>
/// </remarks>
public sealed class MapRoomDiscoveredPayload
{
    /// <summary>The newly discovered room, with its authoritative identity, name and coordinates.</summary>
    public MapRoomRecord Room { get; init; } = new();

    /// <summary>
    /// Connections that have just become legal, each with BOTH endpoints
    /// already discovered. This includes the passage just walked, its
    /// reciprocal, and every other edge to an already-discovered neighbour.
    /// An edge naming an unknown room is discarded rather than drawn.
    /// </summary>
    public IReadOnlyList<MapEdgeRecord> Edges { get; init; } = Array.Empty<MapEdgeRecord>();

    /// <summary>
    /// Frontier stubs that this discovery invalidated, keyed by
    /// <c>(From, Direction)</c>. These were directions on OTHER discovered rooms
    /// that stood for passages leading into the room just discovered.
    /// </summary>
    [JsonPropertyName("frontier_remove")]
    public IReadOnlyList<MapFrontierKey> FrontierRemove { get; init; } = Array.Empty<MapFrontierKey>();

    /// <summary>
    /// Newly exposed unexplored exits OF THE DISCOVERED ROOM. Each carries only
    /// a source room and a direction — never a destination.
    /// </summary>
    [JsonPropertyName("frontier_add")]
    public IReadOnlyList<MapFrontierRecord> FrontierAdd { get; init; } = Array.Empty<MapFrontierRecord>();

    /// <summary>The areas this delta touches, so a filter can gain an area on discovery.</summary>
    public IReadOnlyList<string> Areas { get; init; } = Array.Empty<string>();

    /// <summary>Total discovered-room count after this discovery.</summary>
    [JsonPropertyName("discovered_count")]
    public int DiscoveredCount { get; init; }
}

/// <summary>
/// Protocol V1 <c>map.position</c> payload â€” where the character is standing now.
/// </summary>
/// <remarks>
/// Position is reported only when the current room is discovered. When it is
/// not, <see cref="CurrentPosition"/> and <see cref="CurrentZ"/> are null rather
/// than zero, so an undiscovered room's coordinates are never leaked.
/// </remarks>
public sealed class MapPositionPayload
{
    /// <summary>The <c>world_room_id</c> the character now occupies.</summary>
    [JsonPropertyName("current_room_id")]
    public string? CurrentRoomId { get; init; }

    /// <summary>The <c>area_id</c> the character now occupies.</summary>
    [JsonPropertyName("current_area_id")]
    public string? CurrentAreaId { get; init; }

    /// <summary>The character's current position, or null when not yet discovered.</summary>
    [JsonPropertyName("current_position")]
    public MapPosition? CurrentPosition { get; init; }

    /// <summary>The character's current floor, or null when not yet discovered.</summary>
    [JsonPropertyName("current_z")]
    public int? CurrentZ { get; init; }

    /// <summary>
    /// True when Keystone considers the current room discovered. When false the
    /// position fields are intentionally absent.
    /// </summary>
    [JsonPropertyName("is_discovered")]
    public bool IsDiscovered { get; init; }
}

/// <summary>
/// Protocol V1 <c>map.state</c> payload â€” a discovery COUNTERS summary.
/// </summary>
/// <remarks>
/// This event carries counts only and deliberately no geography: it is the
/// server's own statement of how much of the realm the character knows.
/// </remarks>
public sealed class MapStatePayload
{
    /// <summary>How many generated rooms this character has discovered.</summary>
    [JsonPropertyName("discovered_count")]
    public int DiscoveredCount { get; init; }

    /// <summary>How many generated rooms the realm contains in total.</summary>
    [JsonPropertyName("total_generated_rooms")]
    public int TotalGeneratedRooms { get; init; }

    /// <summary>The character's current area, when Keystone reports one.</summary>
    [JsonPropertyName("current_area_id")]
    public string? CurrentAreaId { get; init; }
}





