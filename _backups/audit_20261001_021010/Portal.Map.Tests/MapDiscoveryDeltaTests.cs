using Portal.Protocol;
using Portal.State;

namespace Portal.Map.Tests;

/// <summary>
/// Covers the incremental <c>map.room.discovered</c> DELTA contract: a single
/// event must leave the client's graph in the same state a full
/// <c>map.snapshot</c> would have produced, with no reconnect in between.
/// </summary>
/// <remarks>
/// Every payload here is transcribed from Keystone's
/// <c>world.data.character_map.build_room_discovery_delta</c>. The properties
/// under test are that the new room appears, the newly legal edges are drawn
/// immediately, the resolved frontier stubs disappear, the new room's own
/// unexplored exits appear, and all of it deduplicates and survives replay.
/// </remarks>
public sealed class MapDiscoveryDeltaTests
{
    /// <summary>
    /// Login state: two discovered rooms, two edges, and one unexplored passage
    /// north out of the field gate into an unknown orchard.
    /// </summary>
    private static MapState LoginState()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("homestead", 10, 20, 0, "ashmarch", "The Homestead"),
                MapPayloads.Room("field_gate", 10, 23, 0, "ashmarch", "The Field Gate"),
            },
            edges: new[]
            {
                MapPayloads.Edge("homestead", "field_gate", "north"),
                MapPayloads.Edge("field_gate", "homestead", "south"),
            },
            frontier: new[] { MapPayloads.Frontier("field_gate", "north") },
            currentRoomId: "homestead",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 2,
            shownCount: 2));
        return state;
    }

    /// <summary>The delta Keystone sends when the player walks north into the orchard.</summary>
    private static MapRoomDiscoveredPayload NorthIntoOrchard() => MapPayloads.Discovered(
        MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard", "ash_plain",
            markers: "safe"),
        edges: new[]
        {
            MapPayloads.Edge("field_gate", "orchard", "north"),
            MapPayloads.Edge("orchard", "field_gate", "south"),
        },
        frontierRemove: new[] { MapPayloads.FrontierKey("field_gate", "north") },
        frontierAdd: new[]
        {
            MapPayloads.Frontier("orchard", "north"),
            MapPayloads.Frontier("orchard", "east"),
        },
        discoveredCount: 3);

    // ── 1-5: room, edge, frontier removal, frontier addition, adjacent-Go ──

    [Fact]
    public void Discovery_adds_the_room()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(NorthIntoOrchard());

        Assert.Equal(3, state.Rooms.Count);
        Assert.True(state.TryGetRoom("orchard", out var orchard));
        Assert.Equal("The Orchard", orchard!.Name);
        Assert.Equal("ashmarch", orchard.AreaId);
        Assert.Equal(3, state.DiscoveredCount);
    }

    [Fact]
    public void Discovery_adds_the_new_legal_edges_without_a_snapshot()
    {
        var state = LoginState();
        Assert.Equal(2, state.Edges.Count);

        state.ApplyRoomDiscovered(NorthIntoOrchard());

        // No map.snapshot, no reconnect: the passage is drawn from this event.
        Assert.Equal(4, state.Edges.Count);
        Assert.Contains(state.Edges, e =>
            e.From == "field_gate" && e.Direction == "north" && e.To == "orchard");
        Assert.Contains(state.Edges, e =>
            e.From == "orchard" && e.Direction == "south" && e.To == "field_gate");
    }

    [Fact]
    public void Discovery_removes_the_resolved_frontier_stub()
    {
        var state = LoginState();
        Assert.True(state.HasFrontier("field_gate"));

        state.ApplyRoomDiscovered(NorthIntoOrchard());

        Assert.False(state.HasFrontier("field_gate"));
        Assert.False(state.Frontier.ContainsKey("field_gate"));
    }

    [Fact]
    public void Discovery_adds_the_new_rooms_own_frontier_stubs()
    {
        var state = LoginState();
        Assert.False(state.HasFrontier("orchard"));

        state.ApplyRoomDiscovered(NorthIntoOrchard());

        var stubs = state.GetFrontier("orchard");
        Assert.Equal(2, stubs.Count);
        Assert.Contains(stubs, s => s.DirectionKind == MapDirection.North);
        Assert.Contains(stubs, s => s.DirectionKind == MapDirection.East);
        Assert.All(stubs, s => Assert.Equal("unexplored", s.State));
    }

    [Fact]
    public void Adjacent_go_becomes_available_immediately_in_both_directions()
    {
        var state = LoginState();
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "field_gate",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 23, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        state.ApplyRoomDiscovered(NorthIntoOrchard());

        // Reusing the exact directions Keystone reported, with no snapshot.
        Assert.True(state.TryGetAdjacentDirection("orchard", "field_gate", out var back));
        Assert.Equal(MapDirection.South, back);
        Assert.True(state.TryGetAdjacentDirection("field_gate", "orchard", out var forth));
        Assert.Equal(MapDirection.North, forth);
    }

    // ── 6-8: deduplication and replay ─────────────────────────────────────

    [Fact]
    public void Repeated_discovery_event_never_duplicates_edges_or_stubs()
    {
        var state = LoginState();
        var delta = NorthIntoOrchard();

        state.ApplyRoomDiscovered(delta);
        var edgesAfterFirst = state.Edges.Count;
        var stubsAfterFirst = state.GetFrontier("orchard").Count;

        state.ApplyRoomDiscovered(delta);
        state.ApplyRoomDiscovered(delta);

        Assert.Equal(3, state.Rooms.Count);
        Assert.Equal(edgesAfterFirst, state.Edges.Count);
        Assert.Equal(stubsAfterFirst, state.GetFrontier("orchard").Count);

        // Identity, not reference equality: one edge per logical connection.
        Assert.Equal(
            state.Edges.Count,
            state.Edges.Select(e => (e.From, e.Direction, e.To)).Distinct().Count());
        Assert.Equal(
            stubsAfterFirst,
            state.GetFrontier("orchard")
                .Select(s => (s.From, s.Direction)).Distinct().Count());
    }

    [Fact]
    public void Duplicate_edges_inside_one_event_are_collapsed()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            edges: new[]
            {
                MapPayloads.Edge("field_gate", "orchard", "north"),
                MapPayloads.Edge("field_gate", "orchard", "north"),
                MapPayloads.Edge("orchard", "field_gate", "south"),
            },
            discoveredCount: 3));

        // The two login edges plus the two distinct delta edges. The duplicated
        // field_gate->orchard is stored exactly once.
        Assert.Equal(4, state.Edges.Count);
        Assert.Single(state.Edges, e =>
            e.From == "field_gate" && e.Direction == "north" && e.To == "orchard");
    }

    [Fact]
    public void Repeated_frontier_add_of_the_same_stub_is_collapsed()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            frontierAdd: new[]
            {
                MapPayloads.Frontier("orchard", "north"),
                MapPayloads.Frontier("orchard", "north"),
            },
            discoveredCount: 3));

        Assert.Single(state.GetFrontier("orchard"));
    }

    [Fact]
    public void Consecutive_discoveries_keep_growing_the_graph_without_duplicates()
    {
        var state = LoginState();
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "field_gate",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 23, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });
        state.ApplyRoomDiscovered(NorthIntoOrchard());

        // Second consecutive new discovery: north again, out of the orchard.
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "orchard",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("mill_road", 13, 26, 0, "ashmarch", "Mill Road"),
            edges: new[]
            {
                MapPayloads.Edge("orchard", "mill_road", "east"),
                MapPayloads.Edge("mill_road", "orchard", "west"),
            },
            frontierRemove: new[] { MapPayloads.FrontierKey("orchard", "east") },
            frontierAdd: new[] { MapPayloads.Frontier("mill_road", "east") },
            discoveredCount: 4));

        Assert.Equal(4, state.Rooms.Count);
        Assert.Equal(6, state.Edges.Count);
        // The orchard's east stub is gone; only its north stub remains, and the
        // mill road contributes its own.
        Assert.Single(state.GetFrontier("orchard"));
        Assert.Equal(MapDirection.North, state.GetFrontier("orchard")[0].DirectionKind);
        Assert.Single(state.GetFrontier("mill_road"));
        Assert.Equal(4, state.DiscoveredCount);

        // Everything remains uniquely identified after two deltas.
        Assert.Equal(
            state.Edges.Count,
            state.Edges.Select(e => (e.From, e.Direction, e.To)).Distinct().Count());
    }

    // ── 9-10: vertical and door edges ─────────────────────────────────────

    [Fact]
    public void Vertical_discovery_delivers_a_real_vertical_edge()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("cellar_stair", 7, 7, 0, "ashmarch", "Cellar Stair")
            },
            frontier: new[] { MapPayloads.Frontier("cellar_stair", "down") },
            currentRoomId: "cellar_stair",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 1,
            shownCount: 1));

        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("cellar_vault", 7, 7, -1, "ashmarch", "Cellar Vault"),
            edges: new[]
            {
                MapPayloads.Edge("cellar_stair", "cellar_vault", "down"),
                MapPayloads.Edge("cellar_vault", "cellar_stair", "up"),
            },
            frontierRemove: new[] { MapPayloads.FrontierKey("cellar_stair", "down") },
            discoveredCount: 2));

        // The stub is gone and the stair is a real, vertical, two-way edge.
        Assert.False(state.HasFrontier("cellar_stair"));
        var down = Assert.Single(state.GetOutgoingEdges("cellar_stair"));
        Assert.Equal(MapDirection.Down, down.DirectionKind);
        Assert.True(MapDirections.IsVertical(down.DirectionKind));
        Assert.False(MapDirections.IsHorizontal(down.DirectionKind));
        Assert.Equal("cellar_vault", down.To);
        Assert.True(state.HasVerticalConnection("cellar_stair"));

        var up = Assert.Single(state.GetOutgoingEdges("cellar_vault"));
        Assert.Equal(MapDirection.Up, up.DirectionKind);
        Assert.Equal(-1, state.Rooms["cellar_vault"].Z - state.Rooms["cellar_stair"].Z);

        // The new floor is exposed, and only because a room on it was found.
        Assert.Equal(new[] { -1, 0 }, state.Floors);

        // Vertical Go works immediately, using the authoritative direction.
        Assert.True(state.TryGetAdjacentDirection(
            "cellar_vault", "cellar_stair", out var upDirection));
        Assert.Equal(MapDirection.Up, upDirection);
    }

    [Fact]
    public void Door_discovery_keeps_the_static_door_marker_on_the_new_edge()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("vault_antechamber", 4, 4, 0, "ashmarch", "Vault Antechamber")
            },
            frontier: new[] { MapPayloads.Frontier("vault_antechamber", "north") },
            currentRoomId: "vault_antechamber",
            currentAreaId: "ashmarch",
            currentZ: 0,
            discoveredCount: 1,
            shownCount: 1));

        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("vault_inner", 4, 7, 0, "ashmarch", "Vault Inner Chamber"),
            edges: new[]
            {
                MapPayloads.Edge("vault_antechamber", "vault_inner", "north", door: true)
            },
            frontierRemove: new[] { MapPayloads.FrontierKey("vault_antechamber", "north") },
            discoveredCount: 2));

        var door = Assert.Single(state.GetOutgoingEdges("vault_antechamber"));
        Assert.True(door.Door);
        Assert.Equal("vault_inner", door.To);
        Assert.True(state.HasDoorConnection("vault_antechamber"));

        // The marker is STATIC geometry only: the edge type carries no runtime
        // lock/open state, exactly as in map.snapshot.
        Assert.Equal(
            new[] { "Direction", "DirectionKind", "Door", "From", "To" },
            typeof(MapEdgeRecord)
                .GetProperties()
                .Where(p => p.DeclaringType == typeof(MapEdgeRecord))
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray());
    }

    // ── 11: a hidden destination stays hidden ─────────────────────────────

    [Fact]
    public void A_hidden_destination_is_never_created_or_named_by_the_delta()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(NorthIntoOrchard());

        // The orchard has unexplored exits, but nothing about where they lead.
        var stubs = state.GetFrontier("orchard");
        Assert.NotEmpty(stubs);

        foreach (var stub in stubs)
        {
            // A stub always hangs off a KNOWN room, and never stands for a
            // destination room: Portal resolves no frontier stub into a room.
            Assert.True(state.TryGetRoom(stub.From, out _));
            Assert.Equal("unexplored", stub.State);

            var edge = state.GetOutgoingEdges(stub.From)
                .FirstOrDefault(e => e.Direction == stub.Direction);
            Assert.Null(edge);
        }

        // And the only rooms on the map are the three Keystone disclosed.
        Assert.Equal(
            new[] { "field_gate", "homestead", "orchard" },
            state.Rooms.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void An_edge_naming_an_undiscovered_room_is_discarded_not_drawn()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            edges: new[]
            {
                // The orchard does not know this destination, so this is not a
                // real connection and must not become one locally.
                MapPayloads.Edge("orchard", "hidden_hollow", "north"),
            },
            discoveredCount: 3));

        Assert.False(state.TryGetRoom("hidden_hollow", out _));
        Assert.DoesNotContain(state.Edges, e => e.To == "hidden_hollow");
        Assert.Equal(2, state.Edges.Count);
    }

    [Fact]
    public void A_frontier_add_naming_an_unknown_source_room_is_ignored()
    {
        var state = LoginState();
        state.ApplyRoomDiscovered(MapPayloads.Discovered(
            MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            frontierAdd: new[]
            {
                MapPayloads.Frontier("somewhere_never_discovered", "north"),
            },
            discoveredCount: 3));

        Assert.False(state.Frontier.ContainsKey("somewhere_never_discovered"));
        Assert.False(state.TryGetRoom("somewhere_never_discovered", out _));
    }
}