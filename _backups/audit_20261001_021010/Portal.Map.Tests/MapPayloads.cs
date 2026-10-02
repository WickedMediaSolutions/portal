using Portal.Protocol;

namespace Portal.Map.Tests;

/// <summary>
/// Builders for Keystone map payloads.
/// </summary>
/// <remarks>
/// The shapes here are transcribed from Keystone's
/// <c>world.data.character_map</c> so the tests exercise the REAL wire contract
/// rather than an idealised one. Every payload is built by hand: no test reads
/// any canonical world file, which is precisely the property being asserted.
/// </remarks>
internal static class MapPayloads
{
    public static MapRoomRecord Room(
        string roomId, int x, int y, int z = 0,
        string areaId = "test_area",
        string? name = null,
        string? district = null,
        params string[] markers) => new()
    {
        RoomId = roomId,
        AreaId = areaId,
        Name = name ?? roomId.Replace('_', ' '),
        X = x,
        Y = y,
        Z = z,
        District = district,
        Markers = markers
    };

    public static MapEdgeRecord Edge(string from, string to, string direction, bool door = false) =>
        new() { From = from, To = to, Direction = direction, Door = door };

    /// <summary>
    /// A frontier entry exactly as Keystone emits it: source room, direction and
    /// the "unexplored" state token. There is deliberately no destination.
    /// </summary>
    public static MapFrontierRecord Frontier(string from, string direction) =>
        new() { From = from, Direction = direction, State = "unexplored" };

    /// <summary>
    /// A frontier REMOVAL exactly as Keystone emits it: the source room and the
    /// direction whose stub this discovery has just invalidated. There is
    /// deliberately no destination here either.
    /// </summary>
    public static MapFrontierKey FrontierKey(string from, string direction) =>
        new() { From = from, Direction = direction };

    /// <summary>
    /// A <c>map.room.discovered</c> DELTA, transcribed from Keystone's
    /// <c>world.data.character_map.build_room_discovery_delta</c>: the new room,
    /// the connections that have just become legal, the frontier stubs this
    /// discovery invalidates, and the new room's own unexplored exits.
    /// </summary>
    public static MapRoomDiscoveredPayload Discovered(
        MapRoomRecord room,
        IEnumerable<MapEdgeRecord>? edges = null,
        IEnumerable<MapFrontierKey>? frontierRemove = null,
        IEnumerable<MapFrontierRecord>? frontierAdd = null,
        int discoveredCount = 0) => new()
        {
            Room = room,
            Edges = (edges ?? Array.Empty<MapEdgeRecord>()).ToArray(),
            FrontierRemove = (frontierRemove ?? Array.Empty<MapFrontierKey>()).ToArray(),
            FrontierAdd = (frontierAdd ?? Array.Empty<MapFrontierRecord>()).ToArray(),
            Areas = room.AreaId is { Length: > 0 } ? new[] { room.AreaId } : Array.Empty<string>(),
            DiscoveredCount = discoveredCount
        };

    public static MapSnapshotPayload Snapshot(
        IEnumerable<MapRoomRecord>? rooms = null,
        IEnumerable<MapEdgeRecord>? edges = null,
        IEnumerable<MapFrontierRecord>? frontier = null,
        string? currentRoomId = null,
        string? currentAreaId = null,
        int? currentZ = null,
        IEnumerable<int>? floors = null,
        int discoveredCount = 0,
        int shownCount = 0)
    {
        var roomList = (rooms ?? Array.Empty<MapRoomRecord>()).ToArray();

        return new MapSnapshotPayload
        {
            Rooms = roomList,
            Edges = (edges ?? Array.Empty<MapEdgeRecord>()).ToArray(),
            Frontier = (frontier ?? Array.Empty<MapFrontierRecord>()).ToArray(),
            CurrentRoomId = currentRoomId,
            CurrentAreaId = currentAreaId,
            CurrentPosition = currentRoomId is null
                ? null
                : new MapPosition
                {
                    X = roomList.FirstOrDefault(r => r.RoomId == currentRoomId)?.X ?? 0,
                    Y = roomList.FirstOrDefault(r => r.RoomId == currentRoomId)?.Y ?? 0,
                    Z = currentZ ?? roomList.FirstOrDefault(r => r.RoomId == currentRoomId)?.Z ?? 0
                },
            CurrentZ = currentZ,
            Floors = (floors ?? Array.Empty<int>()).ToArray(),
            Areas = roomList.Select(r => r.AreaId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray(),
            DiscoveredCount = discoveredCount > 0 ? discoveredCount : roomList.Length,
            ShownCount = shownCount > 0 ? shownCount : roomList.Length
        };
    }

    /// <summary>Serializes a payload and parses it through the real envelope path.</summary>
    public static T RoundTrip<T>(T payload) where T : class
    {
        var envelope = new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Event,
            MessageType = "map.snapshot",
            SequenceNumber = 1,
            Payload = ProtocolSerializer.SerializePayload(payload)
        };

        var json = ProtocolSerializer.Serialize(envelope);
        var parsed = ProtocolSerializer.Deserialize(json);

        Assert.True(parsed.IsSuccess, parsed.Errors.FirstOrDefault()?.Message);

        var result = ProtocolSerializer.DeserializePayload<T>(parsed.Value);
        Assert.True(result.IsSuccess, result.Errors.FirstOrDefault()?.Message);

        return result.Value;
    }
}