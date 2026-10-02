using System.Text.Json.Serialization;

namespace Portal.Protocol;

/// <summary>
/// Protocol V1 <c>map.snapshot</c> payload â€” Keystone's complete, FILTERED view
/// of one character's discovered map.
/// </summary>
/// <remarks>
/// <para>
/// Keystone builds and filters this payload server-side before transmission
/// (see Keystone <c>world.data.character_map</c>). Portal therefore never holds
/// the whole realm map and must never attempt to reconstruct it: the snapshot
/// contains ONLY
/// </para>
/// <list type="bullet">
/// <item><description>rooms the character has discovered,</description></item>
/// <item><description>edges between two discovered rooms,</description></item>
/// <item><description>frontier entries (source room + direction only) marking
/// exits into rooms the character has NOT discovered,</description></item>
/// <item><description>the player's current room/area/position,</description></item>
/// <item><description>markers of discovered rooms only.</description></item>
/// </list>
/// <para>
/// A frontier entry deliberately carries NO destination id, name, district or
/// coordinates. Portal models it accordingly â€” see <see cref="MapFrontierRecord"/>.
/// </para>
/// </remarks>
public sealed class MapSnapshotPayload
{
    /// <summary>The <c>world_room_id</c> the character currently occupies.</summary>
    [JsonPropertyName("current_room_id")]
    public string? CurrentRoomId { get; init; }

    /// <summary>The <c>area_id</c> the character currently occupies.</summary>
    [JsonPropertyName("current_area_id")]
    public string? CurrentAreaId { get; init; }

    /// <summary>
    /// The character's current authoritative world position, or null when the
    /// current room is not (yet) discovered. Null never means "0,0,0".
    /// </summary>
    [JsonPropertyName("current_position")]
    public MapPosition? CurrentPosition { get; init; }

    /// <summary>The character's current floor (world Z), or null when unknown.</summary>
    [JsonPropertyName("current_z")]
    public int? CurrentZ { get; init; }

    /// <summary>Every discovered room included in this snapshot.</summary>
    public IReadOnlyList<MapRoomRecord> Rooms { get; init; } = Array.Empty<MapRoomRecord>();

    /// <summary>Every discovered-to-discovered connection included in this snapshot.</summary>
    public IReadOnlyList<MapEdgeRecord> Edges { get; init; } = Array.Empty<MapEdgeRecord>();

    /// <summary>Unexplored exits, each carrying only a source room and a direction.</summary>
    public IReadOnlyList<MapFrontierRecord> Frontier { get; init; } = Array.Empty<MapFrontierRecord>();

    /// <summary>The area ids represented in this snapshot.</summary>
    public IReadOnlyList<string> Areas { get; init; } = Array.Empty<string>();

    /// <summary>The Z levels the current area actually occupies.</summary>
    public IReadOnlyList<int> Floors { get; init; } = Array.Empty<int>();

    /// <summary>The area/floor filters Keystone applied when building this snapshot.</summary>
    public MapFilterState Filters { get; init; } = new();

    /// <summary>Total number of rooms this character has discovered.</summary>
    [JsonPropertyName("discovered_count")]
    public int DiscoveredCount { get; init; }

    /// <summary>Number of discovered rooms actually included in this snapshot.</summary>
    [JsonPropertyName("shown_count")]
    public int ShownCount { get; init; }
}





