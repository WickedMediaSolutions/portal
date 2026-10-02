using Portal.Protocol;
using Portal.State;

namespace Portal.Map.Tests;

/// <summary>
/// Covers the client's map model: snapshot replacement, discovery, position,
/// filtering, adjacency and reconnect behaviour.
/// </summary>
public sealed class MapStateTests
{
    /// <summary>A small connected 2x2 block plus an unexplored exit.</summary>
    private static MapSnapshotPayload SmallWorld() => MapPayloads.Snapshot(
        rooms: new[]
        {
            MapPayloads.Room("a", 0, 0, 0, "test_area", "Alpha", "d1", markers: "safe"),
            MapPayloads.Room("b", 1, 0, 0, "test_area", "Beta", "d1", markers: new[] { "safe", "bank" }),
            MapPayloads.Room("c", 0, 1, 0, "test_area", "Gamma", "d1", markers: "safe"),
            MapPayloads.Room("d", 0, 0, -1, "test_area", "Below", "d1", markers: "safe")
        },
        edges: new[]
        {
            MapPayloads.Edge("a", "b", "east"),
            MapPayloads.Edge("a", "c", "north"),
            MapPayloads.Edge("a", "d", "down"),
            MapPayloads.Edge("b", "c", "northwest", door: true)
        },
        frontier: new[] { MapPayloads.Frontier("b", "north") },
        currentRoomId: "a",
        currentAreaId: "test_area",
        currentZ: 0,
        floors: new[] { -1, 0 });

    [Fact]
    public void Snapshot_populates_rooms_edges_frontier_and_position()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        Assert.True(state.HasSnapshot);
        Assert.Equal(4, state.Rooms.Count);
        Assert.Equal(4, state.Edges.Count);
        Assert.Equal("a", state.CurrentRoomId);
        Assert.Equal("test_area", state.CurrentAreaId);
        Assert.Equal(0, state.CurrentZ);

        var stub = Assert.Single(state.GetFrontier("b"));
        Assert.Equal(MapDirection.North, stub.DirectionKind);
        Assert.True(state.HasFrontier("b"));
    }

    [Fact]
    public void Rooms_are_keyed_by_world_room_id_not_by_name_or_coordinates()
    {
        var state = new MapState();

        // Two distinct rooms sharing a name AND exact coordinates — the frozen
        // cross-faction Phase 1 overlap case. Keying by anything other than
        // world_room_id would silently merge them into one.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("mor_overlap", 2, 18, 0, "mordrath_capital", "Shared Name"),
                MapPayloads.Room("val_overlap", 2, 18, 0, "valroian_capital", "Shared Name")
            }));

        Assert.Equal(2, state.Rooms.Count);
        Assert.True(state.TryGetRoom("mor_overlap", out var mor));
        Assert.True(state.TryGetRoom("val_overlap", out var val));

        Assert.Equal("mordrath_capital", mor.AreaId);
        Assert.Equal("valroian_capital", val.AreaId);
        Assert.NotEqual(mor.RoomId, val.RoomId);
    }

    [Fact]
    public void Edges_whose_endpoint_is_undiscovered_are_discarded()
    {
        var state = new MapState();

        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("a", 0, 0) },
            // "b" is not in the snapshot, so this edge must be dropped rather
            // than drawn as a connection to an unknown room.
            edges: new[] { MapPayloads.Edge("a", "b", "north") }));

        Assert.Empty(state.Edges);
        Assert.Empty(state.GetOutgoingEdges("a"));
    }

    [Fact]
    public void Door_and_vertical_connections_are_identified()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        Assert.True(state.HasDoorConnection("b"));
        Assert.False(state.HasDoorConnection("a"));

        Assert.True(state.HasVerticalConnection("a"));
        Assert.False(state.HasVerticalConnection("b"));
    }

    [Fact]
    public void Map_room_discovered_adds_the_room_without_inventing_topology()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("e", 2, 2, 0, "test_area", "Epsilon"),
            DiscoveredCount = 5
        });

        Assert.Equal(5, state.Rooms.Count);
        Assert.Equal(5, state.DiscoveredCount);
        Assert.True(state.TryGetRoom("e", out _));

        // Discovery must not invent edges: those stay exactly as the server
        // reported them until the next authoritative snapshot.
        Assert.Equal(4, state.Edges.Count);
    }

    [Fact]
    public void Discovery_clears_the_frontier_stub_in_the_direction_travelled()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("a", 5, 5, 0) },
            frontier: new[] { MapPayloads.Frontier("a", "north") },
            currentRoomId: "a",
            currentAreaId: "test_area",
            currentZ: 0));

        Assert.True(state.HasFrontier("a"));

        // Keystone names the stub it has just resolved. Portal removes exactly
        // that key and guesses nothing: it never works out for itself which
        // passage the player walked.
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("new", 5, 8, 0),
            edges: new[] { MapPayloads.Edge("a", "new", "north") },
            frontierRemove: new[] { MapPayloads.FrontierKey("a", "north") },
            discoveredCount: 2));

        Assert.False(state.HasFrontier("a"));
    }

    [Fact]
    public void Discovery_does_not_clear_frontier_stubs_in_other_directions()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("a", 5, 5, 0) },
            frontier: new[]
            {
                MapPayloads.Frontier("a", "north"),
                MapPayloads.Frontier("a", "east"),
                MapPayloads.Frontier("a", "down")
            },
            currentRoomId: "a",
            currentAreaId: "test_area",
            currentZ: 0));

        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("new", 5, 9, 0),
            frontierRemove: new[] { MapPayloads.FrontierKey("a", "north") },
            discoveredCount: 2));

        var remaining = state.GetFrontier("a");
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, s => s.DirectionKind == MapDirection.North);
        Assert.Contains(remaining, s => s.DirectionKind == MapDirection.East);
        Assert.Contains(remaining, s => s.DirectionKind == MapDirection.Down);
    }

    [Fact]
    public void Map_position_moves_the_marker_without_adding_rooms()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());
        var roomCountBefore = state.Rooms.Count;

        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "b",
            CurrentAreaId = "test_area",
            CurrentPosition = new MapPosition { X = 1, Y = 0, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true
        });

        Assert.Equal("b", state.CurrentRoomId);
        Assert.Equal(0, state.CurrentZ);
        Assert.Equal(roomCountBefore, state.Rooms.Count);
        Assert.True(state.IsCurrentRoom("b"));
        Assert.False(state.IsCurrentRoom("a"));
    }

    [Fact]
    public void Map_position_for_an_undiscovered_room_never_yields_coordinates()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "somewhere_unknown",
            CurrentAreaId = null,
            CurrentPosition = null,
            CurrentZ = null,
            IsDiscovered = false
        });

        Assert.Equal("somewhere_unknown", state.CurrentRoomId);

        // Unknown means unknown, never a fabricated 0,0,0.
        Assert.Null(state.CurrentZ);
        Assert.False(state.TryGetRoom("somewhere_unknown", out _));
    }

    [Fact]
    public void Map_state_reconciles_counters_without_touching_geometry()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        state.ApplyState(new MapStatePayload
        {
            DiscoveredCount = 1200,
            TotalGeneratedRooms = 7943,
            CurrentAreaId = "test_area"
        });

        Assert.Equal(1200, state.DiscoveredCount);
        Assert.Equal(7943, state.TotalGeneratedRooms);
        Assert.Equal(4, state.Rooms.Count);
        Assert.Equal(4, state.Edges.Count);
        Assert.Equal("a", state.CurrentRoomId);
    }

    // ── Area filtering ───────────────────────────────────────────────────

    [Fact]
    public void Area_filter_restricts_the_visible_set_without_discarding_rooms()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("m1", 0, 0, 0, "mordrath_capital"),
                MapPayloads.Room("m2", 1, 0, 0, "mordrath_capital"),
                MapPayloads.Room("v1", 9, 9, 0, "valroian_capital")
            },
            currentRoomId: "m1",
            currentAreaId: "mordrath_capital",
            currentZ: 0));

        state.SelectedAreaId = "valroian_capital";

        Assert.Single(state.VisibleRooms);
        Assert.Equal("v1", state.VisibleRooms[0].RoomId);

        // Filtering is display-only: nothing is lost.
        Assert.Equal(3, state.Rooms.Count);
        Assert.True(state.IsAreaFilterLocked);

        state.SelectedAreaId = "mordrath_capital";
        Assert.Equal(2, state.VisibleRooms.Count);
    }

    [Fact]
    public void Area_filter_follows_the_player_until_the_user_pins_one()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("m1", 0, 0, 0, "mordrath_capital"),
                MapPayloads.Room("v1", 5, 5, 0, "valroian_capital")
            },
            currentRoomId: "m1",
            currentAreaId: "mordrath_capital",
            currentZ: 0));

        Assert.Equal("mordrath_capital", state.SelectedAreaId);
        Assert.True(state.AreaFilterFollowsPlayer);

        // Player walks into another area: the view follows by default.
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "v1",
            CurrentAreaId = "valroian_capital",
            CurrentPosition = new MapPosition { X = 5, Y = 5, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true
        });

        Assert.Equal("valroian_capital", state.SelectedAreaId);

        // But an explicit choice pins the filter and movement must not override it.
        state.SelectedAreaId = "mordrath_capital";
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "v1",
            CurrentAreaId = "valroian_capital",
            CurrentPosition = new MapPosition { X = 5, Y = 5, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true
        });

        Assert.Equal("mordrath_capital", state.SelectedAreaId);
    }

    // ── Floor / Z filtering ─────────────────────────────────────────────

    [Fact]
    public void Floor_filter_selects_exactly_one_z_level()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        state.SelectedZ = -1;
        Assert.Single(state.VisibleRooms);
        Assert.Equal("d", state.VisibleRooms[0].RoomId);
        Assert.Equal(-1, state.VisibleRooms[0].Z);

        state.SelectedZ = 0;
        Assert.Equal(3, state.VisibleRooms.Count);

        state.SelectedZ = null;
        Assert.Equal(4, state.VisibleRooms.Count);
    }

    [Fact]
    public void Floors_for_an_area_are_the_real_occupied_levels_not_a_dense_range()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("deep", 0, 0, -26, "deep_area"),
                MapPayloads.Room("mid", 0, 0, -13, "deep_area"),
                MapPayloads.Room("top", 0, 0, 14, "deep_area")
            },
            currentRoomId: "mid",
            currentAreaId: "deep_area",
            currentZ: -13,
            floors: new[] { -26, -13, 14 }));

        state.SelectedAreaId = "deep_area";

        // The realm spans -26..+14, but only these three levels exist here.
        Assert.Equal(new[] { -26, -13, 14 }, state.FloorsForSelectedArea);
        Assert.DoesNotContain(0, state.FloorsForSelectedArea);
    }

    [Fact]
    public void Up_and_down_edges_are_recognised_as_vertical_not_horizontal()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        var vertical = state.GetOutgoingEdges("a")
            .Where(e => MapDirections.IsVertical(e.DirectionKind))
            .ToArray();

        var single = Assert.Single(vertical);
        Assert.Equal(MapDirection.Down, single.DirectionKind);
        Assert.Equal("d", single.To);

        // The renderer branches on IsVertical to draw a chevron rather than a
        // misleading 2-D line, so this classification is what protects the
        // map's honesty about floors.
        Assert.True(MapDirections.IsVertical(state.GetOutgoingEdges("a")
            .First(e => e.To == "d").DirectionKind));
        Assert.False(MapDirections.IsHorizontal(state.GetOutgoingEdges("a")
            .First(e => e.To == "d").DirectionKind));
    }

    // ── Adjacent-room navigation (no pathfinding) ────────────────────────

    [Fact]
    public void Adjacent_direction_resolves_only_for_a_real_discovered_edge()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        Assert.True(state.TryGetAdjacentDirection("a", "b", out var east));
        Assert.Equal(MapDirection.East, east);

        Assert.True(state.TryGetAdjacentDirection("a", "c", out var north));
        Assert.Equal(MapDirection.North, north);
    }

    [Fact]
    public void Adjacent_direction_refuses_non_adjacent_undiscovered_and_same_rooms()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        // "d" is one floor DOWN, not a horizontal neighbour of "b".
        Assert.False(state.TryGetAdjacentDirection("b", "d", out _));

        // An undiscovered destination can never be stepped into.
        Assert.False(state.TryGetAdjacentDirection("b", "not_discovered", out _));

        // Staying put is not a movement.
        Assert.False(state.TryGetAdjacentDirection("a", "a", out _));
    }

    // ── Reconnect ───────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_replaces_all_previous_state_so_no_stale_data_survives()
    {
        var state = new MapState();

        // First session: a small discovered world with a frontier stub.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("old1", 0, 0, 0, "old_area"),
                MapPayloads.Room("old2", 1, 0, 0, "old_area")
            },
            edges: new[] { MapPayloads.Edge("old1", "old2", "east") },
            frontier: new[] { MapPayloads.Frontier("old2", "west") },
            currentRoomId: "old1",
            currentAreaId: "old_area",
            currentZ: 0));

        Assert.Equal(2, state.Rooms.Count);

        // Reconnect: Keystone re-sends a complete snapshot for a world that is
        // unrelated to the previous session.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("new1", 50, 50, -7, "new_area") },
            currentRoomId: "new1",
            currentAreaId: "new_area",
            currentZ: -7));

        Assert.Single(state.VisibleRooms);
        Assert.Contains("new1", state.Rooms.Keys);

        // Nothing from the previous session may survive.
        Assert.DoesNotContain("old1", state.Rooms.Keys);
        Assert.DoesNotContain("old2", state.Rooms.Keys);
        Assert.Empty(state.Edges);
        Assert.Empty(state.Frontier);
        Assert.Equal("new1", state.CurrentRoomId);
        Assert.Equal(-7, state.CurrentZ);
        Assert.Equal(new[] { "new_area" }, state.Areas);
    }

    [Fact]
    public void Reset_clears_everything_for_logout()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());
        state.SelectedAreaId = "test_area";
        state.SelectedZ = 0;

        state.Reset();

        Assert.Empty(state.Rooms);
        Assert.Empty(state.Edges);
        Assert.Empty(state.Frontier);
        Assert.Empty(state.Areas);
        Assert.Empty(state.Floors);
        Assert.Null(state.CurrentRoomId);
        Assert.Null(state.CurrentZ);
        Assert.False(state.HasSnapshot);
        Assert.Null(state.SelectedAreaId);
        Assert.Null(state.SelectedZ);
    }

    // ── Rendering model / performance ───────────────────────────────────

    [Fact]
    public void Rendering_model_places_rooms_at_their_real_coordinates()
    {
        var state = new MapState();
        state.ApplySnapshot(SmallWorld());

        Assert.True(state.TryGetRoom("b", out var b));
        Assert.Equal(1, b.X);
        Assert.Equal(0, b.Y);
        Assert.Equal(0, b.Z);

        // Geometry comes from x/y/z, never from the order rooms arrived in.
        // The default view follows the player's floor (Z 0), so "d" (Z -1) is
        // correctly NOT drawn on this floor even though it is discovered.
        Assert.Equal(new[] { "a", "c", "b" },
            state.VisibleRooms.OrderBy(r => r.X).ThenBy(r => r.Y)
                .Select(r => r.RoomId).ToArray());

        // Clearing the floor filter brings the other level into view.
        state.SelectedZ = null;
        Assert.Equal(new[] { "a", "d", "c", "b" },
            state.VisibleRooms.OrderBy(r => r.X).ThenBy(r => r.Y)
                .Select(r => r.RoomId).ToArray());
    }

    [Fact]
    public void Full_reveal_map_of_7943_rooms_applies_and_filters_within_budget()
    {
        const int RoomCount = 7943;   // the real generated realm size
        const int EdgeCount = 19570;  // the real generated edge count

        var rooms = new MapRoomRecord[RoomCount];
        for (var i = 0; i < RoomCount; i++)
        {
            rooms[i] = MapPayloads.Room(
                $"room_{i}", i % 180, (i / 180) % 180, i % 40,
                $"area_{i % 34}",
                markers: i % 97 == 0 ? "bank" : "safe");
        }

        var edges = new MapEdgeRecord[EdgeCount];
        for (var i = 0; i < EdgeCount; i++)
        {
            edges[i] = MapPayloads.Edge(
                $"room_{i % RoomCount}",
                $"room_{(i + 1) % RoomCount}",
                i % 5 == 0 ? "up" : "east",
                door: i % 37 == 0);
        }

        var snapshot = MapPayloads.Snapshot(
            rooms: rooms,
            edges: edges,
            frontier: new[] { MapPayloads.Frontier("room_0", "north") },
            currentRoomId: "room_0",
            currentAreaId: "area_0",
            currentZ: 0);

        var state = new MapState();

        var applyWatch = System.Diagnostics.Stopwatch.StartNew();
        state.ApplySnapshot(snapshot);
        applyWatch.Stop();

        Assert.Equal(RoomCount, state.Rooms.Count);
        Assert.Equal(EdgeCount, state.Edges.Count);

        // Generous ceiling: the point is that cost is linear, not that it is
        // microseconds-fast.
        Assert.True(applyWatch.ElapsedMilliseconds < 5000,
            $"Full-reveal apply took {applyWatch.ElapsedMilliseconds} ms");

        // Filtering a fully revealed map must also stay responsive.
        var filterWatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            state.SelectedAreaId = $"area_{i % 34}";
            _ = state.VisibleRooms.Count;
        }
        filterWatch.Stop();

        Assert.True(filterWatch.ElapsedMilliseconds < 5000,
            $"20 area-filter passes took {filterWatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Single_discovered_room_renders_without_error()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("only", 3, 4, 0) },
            currentRoomId: "only",
            currentAreaId: "test_area",
            currentZ: 0));

        Assert.Single(state.VisibleRooms);
        Assert.True(state.TryGetRoom("only", out var room));
        Assert.Equal(3, room.X);
        Assert.Equal(4, room.Y);
    }
}