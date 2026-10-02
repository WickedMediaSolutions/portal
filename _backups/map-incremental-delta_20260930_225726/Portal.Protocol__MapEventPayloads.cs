using System.Text.Json.Serialization;

namespace Portal.Protocol;

/// <summary>
/// Protocol V1 <c>map.room.discovered</c> payload â€” one room the character has
/// just entered for the first time.
/// </summary>
/// <remarks>
/// Keystone emits this only AFTER a genuine arrival, so the room it carries is
/// always legitimately discovered. The payload is deliberately minimal: it is a
/// NEW ROOM plus a counter. Edges and frontier reconciliation remain Keystone's
/// job and arrive with the next authoritative <c>map.snapshot</c>; Portal does
/// not fabricate them here.
/// </remarks>
public sealed class MapRoomDiscoveredPayload
{
    /// <summary>The newly discovered room, with its authoritative identity, name and coordinates.</summary>
    public MapRoomRecord Room { get; init; } = new();

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





