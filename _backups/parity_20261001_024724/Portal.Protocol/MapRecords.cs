using System.Text.Json.Serialization;

namespace Portal.Protocol;

/// <summary>
/// <b>Wire naming note for all map payload models.</b>
/// </summary>
/// <remarks>
/// <para>
/// Every other Keystone payload uses camelCase (<c>characterId</c>,
/// <c>raceId</c>, <c>targetId</c>), which matches Portal's serializer default of
/// <c>JsonNamingPolicy.CamelCase</c>. The MAP payloads are the exception:
/// Keystone's <c>world.data.character_map</c> builds plain Python dicts keyed
/// <c>room_id</c> / <c>current_room_id</c> / <c>discovered_count</c>, and
/// <c>portal_bridge.serializer</c> forwards them verbatim into the envelope
/// payload, so they arrive as snake_case.
/// </para>
/// <para>
/// The multi-word properties therefore carry an explicit
/// <see cref="JsonPropertyNameAttribute"/>. Without it every map payload would
/// silently deserialize to an EMPTY object against the live server and the map
/// would simply never populate. Single-word keys (x, y, z, name, district,
/// markers, rooms, edges, frontier, areas, floors, filters, from, to,
/// direction, door, state) are identical under both conventions and need no
/// attribute.
/// </para>
/// <para>
/// Map payloads are server-to-client only — Portal never sends one — so
/// naming them explicitly has no round-trip or request-side impact.
/// </para>
/// </remarks>
internal static class MapProtocolNaming
{
    /// <summary>Documents the convention; present for discoverability.</summary>
    public const string Convention = "snake_case";
}

/// <summary>
/// One DISCOVERED room as transmitted by Keystone.
/// </summary>
/// <remarks>
/// Identity is the <see cref="RoomId"/> (<c>world_room_id</c>) and nothing
/// else. Six frozen Phase 1 coordinate cells overlap across the two faction
/// projections, and room names can change with world content, so a room must
/// never be keyed by name, by coordinate, or by database object id.
/// </remarks>
public sealed class MapRoomRecord
{
    /// <summary>Stable canonical <c>world_room_id</c>. The only valid room key.</summary>
    [JsonPropertyName("room_id")]
    public string RoomId { get; init; } = string.Empty;

    /// <summary>The <c>area_id</c> that owns this room.</summary>
    [JsonPropertyName("area_id")]
    public string AreaId { get; init; } = string.Empty;

    /// <summary>Authoritative display name of the discovered room.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Authoritative world X.</summary>
    public int X { get; init; }

    /// <summary>Authoritative world Y.</summary>
    public int Y { get; init; }

    /// <summary>Authoritative world Z (floor).</summary>
    public int Z { get; init; }

    /// <summary>District identifier, when the room has one.</summary>
    public string? District { get; init; }

    /// <summary>Static room markers Keystone sent, e.g. "bank", "healing", "city".</summary>
    public IReadOnlyList<string> Markers { get; init; } = Array.Empty<string>();

    /// <summary>True when the room carries the named marker.</summary>
    public bool HasMarker(string marker) =>
        !string.IsNullOrEmpty(marker) &&
        Markers.Any(m => string.Equals(m, marker, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One discovered-to-discovered connection.
/// </summary>
public sealed class MapEdgeRecord
{
    /// <summary>Source <c>world_room_id</c> (the exit's origin).</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Destination <c>world_room_id</c> (always a discovered room).</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>Keystone's canonical direction token, e.g. "north" or "up".</summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>
    /// True when the real connection passes through a door. This says nothing
    /// about whether the door is open, locked or jammed: Keystone's payload does
    /// not carry that, and Portal never infers it.
    /// </summary>
    public bool Door { get; init; }

    /// <summary>The parsed <see cref="Direction"/> as an enum. Unknown tokens map to Unknown.</summary>
    public MapDirection DirectionKind => MapDirections.Parse(Direction);
}

/// <summary>
/// An UNEXPLORED exit: there is a passage in this direction, but what lies
/// beyond it is unknown to the character.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type intentionally has no destination field of any kind.</b>
/// Keystone discloses only the source room and the direction. There is
/// deliberately no <c>To</c>, <c>ToRoomId</c>, <c>Name</c>, <c>X</c>, <c>Y</c>,
/// <c>Z</c>, <c>AreaId</c> or <c>District</c> â€” adding one would turn fog of war
/// into a spoiler. The renderer draws a frontier stub from the source room and
/// must never synthesise a phantom destination room from this record.
/// </para>
/// </remarks>
public sealed class MapFrontierRecord
{
    /// <summary>The DISCOVERED room the unexplored exit leads out of.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Keystone's canonical direction token for the unexplored exit.</summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>
    /// Keystone's frontier marker token ("unexplored"). Carried through for
    /// display only; it never identifies the hidden destination.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The parsed <see cref="Direction"/> as an enum. Unknown tokens map to Unknown.</summary>
    public MapDirection DirectionKind => MapDirections.Parse(Direction);
}

/// <summary>
/// Identifies one frontier stub by its SOURCE ROOM and DIRECTION — and nothing
/// else.
/// </summary>
/// <remarks>
/// This is the deterministic identity of a frontier stub, and it is
/// deliberately the only thing a stub can be keyed by: the destination is
/// hidden from the character by definition, so there is no id to key on.
/// Reusing this key for both <c>frontier_add</c> and <c>frontier_remove</c>
/// means a replayed event adds nothing and removes nothing twice.
/// </remarks>
public sealed class MapFrontierKey
{
    /// <summary>The DISCOVERED room the unexplored exit leads out of.</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Keystone's canonical direction token for the unexplored exit.</summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>The parsed <see cref="Direction"/> as an enum. Unknown maps to Unknown.</summary>
    public MapDirection DirectionKind => MapDirections.Parse(Direction);
}

/// <summary>
/// The deterministic identity of a frontier stub: (<c>From</c>, <c>Direction</c>).
/// </summary>
/// <remarks>
/// Compared with ordinal string equality in both directions, so a stub is only
/// ever the same stub when the server said the same source and the same
/// direction.
/// </remarks>
public static class MapFrontierIdentity
{
    /// <summary>The stable key for one frontier stub.</summary>
    public static (string From, string Direction) Of(string? from, string? direction) =>
        (from ?? string.Empty, direction ?? string.Empty);

    /// <summary>The stable key for a frontier record.</summary>
    public static (string From, string Direction) Of(MapFrontierRecord stub) =>
        (stub.From, stub.Direction);

    /// <summary>
    /// The stable key for a removal, which carries exactly the same identity as
    /// the stub it removes — so add and remove can never disagree about what
    /// "the stub at (a, north)" means.
    /// </summary>
    public static (string From, string Direction) Of(MapFrontierKey key) =>
        (key.From, key.Direction);
}

/// <summary>
/// The deterministic identity of an edge: (<c>From</c>, <c>Direction</c>, <c>To</c>).
/// </summary>
/// <remarks>
/// This is the logical identity of a connection, not reference equality on the
/// deserialized object: the same passage arriving again — from a replay, a
/// reconnect or a duplicate delivery — maps to the same key and is stored once.
/// </remarks>
public static class MapEdgeIdentity
{
    /// <summary>The stable key for one edge.</summary>
    public static (string From, string Direction, string To) Of(MapEdgeRecord edge) =>
        (edge.From, edge.Direction, edge.To);

    /// <summary>Stable ordering so the edge collection never reorders itself.</summary>
    public static int Compare(
        (string From, string Direction, string To) a,
        (string From, string Direction, string To) b)
    {
        var byFrom = string.CompareOrdinal(a.From, b.From);
        if (byFrom != 0) return byFrom;

        var byDirection = string.CompareOrdinal(a.Direction, b.Direction);
        return byDirection != 0 ? byDirection : string.CompareOrdinal(a.To, b.To);
    }
}

/// <summary>An integer world coordinate triple used for the player's current position.</summary>
public sealed class MapPosition
{
    /// <summary>World X.</summary>
    public int X { get; init; }

    /// <summary>World Y.</summary>
    public int Y { get; init; }

    /// <summary>World Z (floor).</summary>
    public int Z { get; init; }
}

/// <summary>The area/floor filters Keystone applied when it built a snapshot.</summary>
public sealed class MapFilterState
{
    /// <summary>The applied area filter, or null when unfiltered.</summary>
    [JsonPropertyName("area_id")]
    public string? AreaId { get; init; }

    /// <summary>The applied floor filter, or null when unfiltered.</summary>
    public int? Z { get; init; }
}







