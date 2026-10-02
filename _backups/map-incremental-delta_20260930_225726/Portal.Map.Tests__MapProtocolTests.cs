using System.Text.Json;
using Portal.Protocol;
using Portal.State;

namespace Portal.Map.Tests;

/// <summary>
/// Covers deserialization of Keystone's four map payloads and the fog-of-war
/// guarantees of the protocol models themselves.
/// </summary>
public sealed class MapProtocolTests
{
    [Fact]
    public void Snapshot_deserializes_every_field_from_the_real_payload_shape()
    {
        // This JSON is transcribed from Keystone world.data.character_map.
        const string payloadJson = """
        {
          "type": "map.snapshot",
          "current_room_id": "ash_homestead",
          "current_area_id": "mordrath_ashmarch",
          "current_position": { "x": 12, "y": 34, "z": 0 },
          "current_z": 0,
          "rooms": [
            { "room_id": "ash_homestead", "area_id": "mordrath_ashmarch",
              "name": "The Homestead", "x": 12, "y": 34, "z": 0,
              "district": "ash_plain", "markers": ["safe", "settlement"] }
          ],
          "edges": [
            { "from": "ash_homestead", "to": "ash_field", "direction": "north", "door": false }
          ],
          "frontier": [
            { "from": "ash_homestead", "direction": "east", "state": "unexplored" }
          ],
          "areas": ["mordrath_ashmarch"],
          "floors": [-1, 0, 1],
          "filters": { "area_id": null, "z": null },
          "discovered_count": 12,
          "shown_count": 1
        }
        """;

        var snapshot = JsonSerializer.Deserialize<MapSnapshotPayload>(
            payloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(snapshot);
        Assert.Equal("ash_homestead", snapshot!.CurrentRoomId);
        Assert.Equal("mordrath_ashmarch", snapshot.CurrentAreaId);
        Assert.Equal(12, snapshot.CurrentPosition!.X);
        Assert.Equal(34, snapshot.CurrentPosition.Y);
        Assert.Equal(0, snapshot.CurrentPosition.Z);
        Assert.Equal(0, snapshot.CurrentZ);
        Assert.Equal(12, snapshot.DiscoveredCount);
        Assert.Equal(1, snapshot.ShownCount);
        Assert.Equal(new[] { -1, 0, 1 }, snapshot.Floors);

        var room = Assert.Single(snapshot.Rooms);
        Assert.Equal("The Homestead", room.Name);
        Assert.Equal("ash_plain", room.District);
        Assert.Contains("settlement", room.Markers);

        var edge = Assert.Single(snapshot.Edges);
        Assert.Equal(MapDirection.North, edge.DirectionKind);
        Assert.False(edge.Door);

        var stub = Assert.Single(snapshot.Frontier);
        Assert.Equal(MapDirection.East, stub.DirectionKind);
        Assert.Equal("unexplored", stub.State);
    }

    [Fact]
    public void Room_discovered_deserializes()
    {
        const string json = """
        {
          "type": "map.room.discovered",
          "room": { "room_id": "ash_field", "area_id": "mordrath_ashmarch",
                    "name": "The Ashfield", "x": 12, "y": 35, "z": 0,
                    "district": "ash_plain", "markers": ["safe"] },
          "discovered_count": 13
        }
        """;

        var payload = JsonSerializer.Deserialize<MapRoomDiscoveredPayload>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(payload);
        Assert.Equal("ash_field", payload!.Room.RoomId);
        Assert.Equal(13, payload.DiscoveredCount);
        Assert.Equal(35, payload.Room.Y);
    }

    [Fact]
    public void Position_deserializes_discovered_and_undiscovered_shapes()
    {
        const string discovered = """
        { "type": "map.position", "current_room_id": "ash_field",
          "current_area_id": "mordrath_ashmarch",
          "current_position": { "x": 12, "y": 35, "z": -1 },
          "current_z": -1, "is_discovered": true }
        """;

        var found = JsonSerializer.Deserialize<MapPositionPayload>(
            discovered, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(found);
        Assert.True(found!.IsDiscovered);
        Assert.Equal(-1, found.CurrentZ);
        Assert.Equal(12, found.CurrentPosition!.X);

        // Keystone omits the coordinates entirely for an undiscovered room.
        const string unknown = """
        { "type": "map.position", "current_room_id": "ash_vault",
          "current_area_id": null, "current_position": null,
          "current_z": null, "is_discovered": false }
        """;

        var hidden = JsonSerializer.Deserialize<MapPositionPayload>(
            unknown, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(hidden);
        Assert.False(hidden!.IsDiscovered);
        Assert.Null(hidden.CurrentPosition);
        Assert.Null(hidden.CurrentZ);
    }

    [Fact]
    public void Map_state_deserializes_counters()
    {
        const string json = """
        { "type": "map.state", "discovered_count": 42,
          "total_generated_rooms": 7943, "current_area_id": "mordrath_ashmarch" }
        """;

        var payload = JsonSerializer.Deserialize<MapStatePayload>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(payload);
        Assert.Equal(42, payload!.DiscoveredCount);
        Assert.Equal(7943, payload.TotalGeneratedRooms);
        Assert.Equal("mordrath_ashmarch", payload.CurrentAreaId);
    }

    [Fact]
    public void Snapshot_survives_a_full_serializer_round_trip()
    {
        var original = MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("a", 1, 2, 0) },
            edges: new[] { MapPayloads.Edge("a", "b", "north", door: true) },
            frontier: new[] { MapPayloads.Frontier("a", "east") },
            currentRoomId: "a",
            currentAreaId: "test_area",
            currentZ: 0);

        var restored = MapPayloads.RoundTrip(original);

        Assert.Equal("a", restored.CurrentRoomId);
        Assert.Single(restored.Rooms);
        Assert.Single(restored.Edges);
        Assert.True(restored.Edges.First().Door);
        Assert.Single(restored.Frontier);
    }

    /// <summary>
    /// The most important protocol-level guarantee: a frontier record must not
    /// be able to carry ANY hidden destination information.
    /// </summary>
    [Fact]
    public void Frontier_record_has_no_hidden_destination_field()
    {
        var properties = typeof(MapFrontierRecord)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        // Exactly the fields Keystone discloses, plus the parsed helper.
        Assert.Equal(new[] { "Direction", "DirectionKind", "From", "State" },
            properties.OrderBy(p => p, StringComparer.Ordinal).ToArray());

        // And specifically, none of the spoiler-bearing names may exist.
        Assert.DoesNotContain("To", properties);
        Assert.DoesNotContain("ToRoomId", properties);
        Assert.DoesNotContain("RoomId", properties);
        Assert.DoesNotContain("Name", properties);
        Assert.DoesNotContain("X", properties);
        Assert.DoesNotContain("Y", properties);
        Assert.DoesNotContain("Z", properties);
        Assert.DoesNotContain("AreaId", properties);
        Assert.DoesNotContain("District", properties);
        Assert.DoesNotContain("Coordinates", properties);
        Assert.DoesNotContain("Markers", properties);
    }

    /// <summary>
    /// The model must not absorb a destination even if a buggy or hostile
    /// server sent one: unknown JSON properties are ignored, never surfaced.
    /// </summary>
    [Fact]
    public void Frontier_ignores_an_injected_destination_and_never_exposes_it()
    {
        const string hostile = """
        {
          "type": "map.snapshot",
          "current_room_id": "a", "current_area_id": "test_area",
          "current_position": null, "current_z": null,
          "rooms": [], "edges": [],
          "frontier": [
            { "from": "a", "direction": "north", "state": "unexplored",
              "to": "SECRET_ROOM", "name": "The Vault", "x": 999, "y": 999,
              "z": -26, "area_id": "secret_area", "district": "secret_district" }
          ],
          "areas": [], "floors": [], "filters": {},
          "discovered_count": 1, "shown_count": 1
        }
        """;

        var snapshot = JsonSerializer.Deserialize<MapSnapshotPayload>(
            hostile, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(snapshot);
        var stub = Assert.Single(snapshot!.Frontier);

        // The model simply has nowhere to put the injected data.
        Assert.Equal("a", stub.From);
        Assert.Equal(MapDirection.North, stub.DirectionKind);

        var state = new MapState();
        state.ApplySnapshot(snapshot);

        // And the leaked id never becomes a room the map can draw or reveal.
        Assert.DoesNotContain("SECRET_ROOM", state.Rooms.Keys);
        Assert.Empty(state.Rooms);
    }

    [Fact]
    public void Portal_never_references_the_canonical_realm_map_file()
    {
        // The Portal assemblies must contain no reference to the canonical
        // 9.9MB dataset. Portal consumes filtered Keystone events only.
        var offenders = new List<string>();

        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Portal.*.dll"))
        {
            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(dll));
            if (text.Contains("realm_map.json", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("world/generated/maps", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(Path.GetFileName(dll));
            }
        }

        Assert.Empty(offenders);
    }
}