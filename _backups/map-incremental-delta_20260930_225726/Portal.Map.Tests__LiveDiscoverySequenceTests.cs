using Portal.Protocol;
using Portal.State;

namespace Portal.Map.Tests;

/// <summary>
/// Replays the REAL Keystone live-socket sequences exercised during live
/// localhost QA against the client's map model.
///
/// Each test drives the exact event order the server emits for one movement:
/// <c>map.snapshot</c> at login, then <c>map.room.discovered</c> and
/// <c>map.position</c> for each step. Payloads are transcribed from the live
/// wire shapes, including the rule that an undiscovered destination is
/// described ONLY by a direction, never by an id, a name or coordinates.
/// </summary>
public sealed class LiveDiscoverySequenceTests
{
    /// <summary>
    /// Two discovered rooms joined north-south, with one unexplored passage
    /// leading north out of the field gate. This is the shape Keystone sent at
    /// login during QA.
    /// </summary>
    private static MapSnapshotPayload LoginSnapshot() => MapPayloads.Snapshot(
        rooms: new[]
        {
            MapPayloads.Room("homestead", 10, 20, 0, "ashmarch", "The Homestead", "ash_plain",
                markers: new[] { "safe", "settlement" }),
            MapPayloads.Room("field_gate", 10, 23, 0, "ashmarch", "The Field Gate", "ash_plain",
                markers: "safe"),
        },
        // Keystone's canonical realm map stores both directions of a passage as
        // separate edges; map.snapshot forwards each one with a discovered
        // endpoint.
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
        shownCount: 2);

    /// <summary>Steps north onto the already-discovered field gate.</summary>
    private static void MoveToFieldGate(MapState state) => state.ApplyPosition(new MapPositionPayload
    {
        CurrentRoomId = "field_gate",
        CurrentAreaId = "ashmarch",
        CurrentPosition = new MapPosition { X = 10, Y = 23, Z = 0 },
        CurrentZ = 0,
        IsDiscovered = true,
    });

    [Fact]
    public void Login_snapshot_arrives_with_the_expected_world_state()
    {
        var state = new MapState();
        state.ApplySnapshot(LoginSnapshot());

        Assert.True(state.HasSnapshot);
        Assert.Equal(2, state.Rooms.Count);
        Assert.Equal("homestead", state.CurrentRoomId);
        Assert.Equal("ashmarch", state.CurrentAreaId);
        Assert.Equal(0, state.CurrentZ);
        Assert.Equal(new[] { "ashmarch" }, state.Areas);
        Assert.Equal(new[] { 0 }, state.Floors);

        // The unexplored passage north is represented by a direction-only stub.
        var stub = Assert.Single(state.GetFrontier("field_gate"));
        Assert.Equal(MapDirection.North, stub.DirectionKind);
        Assert.Equal("unexplored", stub.State);
    }

    [Fact]
    public void Moving_into_an_undiscovered_room_discovers_it_and_moves_the_marker()
    {
        var state = new MapState();
        state.ApplySnapshot(LoginSnapshot());

        Assert.False(state.TryGetRoom("orchard", out _));
        Assert.True(state.HasFrontier("field_gate"));

        // Step 1, already discovered: north to the field gate. Only a
        // map.position arrives. The frontier stub stays, because the passage
        // leaving the field gate is still unexplored.
        MoveToFieldGate(state);
        Assert.Equal("field_gate", state.CurrentRoomId);
        Assert.True(state.HasFrontier("field_gate"));
        Assert.False(state.TryGetRoom("orchard", out _));

        // Step 2, undiscovered: north out of the field gate. Keystone marks the
        // destination discovered, so it emits map.room.discovered and then
        // map.position.
        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard", "ash_plain",
                markers: "safe"),
            DiscoveredCount = 3,
        });

        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "orchard",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        // 1. the new room is on the map
        Assert.True(state.TryGetRoom("orchard", out var orchard));
        Assert.Equal("The Orchard", orchard!.Name);
        Assert.Equal(3, state.Rooms.Count);

        // 2. the frontier stub it replaced is gone
        Assert.False(state.HasFrontier("field_gate"));

        // 3. the marker moved
        Assert.Equal("orchard", state.CurrentRoomId);
        Assert.True(state.IsCurrentRoom("orchard"));
        Assert.Equal(0, state.CurrentZ);

        // 4. Discovery does NOT invent topology. Keystone's map.room.discovered
        //    payload carries only the room record, so the edge that was just
        //    one: the edge that was just travelled is not part of the edge set
        //    until the next authoritative map.snapshot arrives. This is a fog
        //    rule as much as a correctness one: Portal never invents a
        //    connection the server did not report.
        Assert.Equal(2, state.Edges.Count);

        // 5. Once Keystone's next snapshot does arrive with the real edges, the
        //    adjacent Go action resolves in the ordinary Keystone direction.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("homestead", 10, 20, 0, "ashmarch", "The Homestead"),
                MapPayloads.Room("field_gate", 10, 23, 0, "ashmarch", "The Field Gate"),
                MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            },
            edges: new[]
            {
                MapPayloads.Edge("homestead", "field_gate", "north"),
                // Keystone's canonical realm map stores BOTH directions of a
                // passage as separate edges, and map.snapshot forwards each one
                // that has a discovered endpoint. The reverse edge is therefore
                // authoritative server data, not something Portal invented.
                MapPayloads.Edge("field_gate", "homestead", "south"),
                MapPayloads.Edge("field_gate", "orchard", "north"),
                MapPayloads.Edge("orchard", "field_gate", "south"),
            },
            frontier: new[] { MapPayloads.Frontier("orchard", "north") },
            currentRoomId: "orchard",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 3,
            shownCount: 3));

        var back = state.GetOutgoingEdges("orchard").Select(e => e.Direction).ToArray();
        Assert.Contains("south", back);
        Assert.True(state.TryGetAdjacentDirection("orchard", "field_gate", out var backDirection));
        Assert.Equal(MapDirection.South, backDirection);

        // The reverse direction also works, so the compass is synchronised
        // with the map in both directions after the snapshot.
        Assert.True(state.TryGetAdjacentDirection("field_gate", "orchard", out var forthDirection));
        Assert.Equal(MapDirection.North, forthDirection);
    }

    [Fact]
    public void Discovery_persists_when_moving_back_and_forth()
    {
        var state = new MapState();
        state.ApplySnapshot(LoginSnapshot());

        // Discovery happens in the order Keystone emits it.
        MoveToFieldGate(state);
        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            DiscoveredCount = 3,
        });
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "orchard",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        // Walk back south to the field gate.
        MoveToFieldGate(state);

        // Walk north again. The room is already known, so Keystone sends only a
        // position update: no second discovery event, and no duplicates.
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "orchard",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        Assert.Equal(3, state.Rooms.Count);
        Assert.Single(state.Rooms.Values.Where(r => r.RoomId == "orchard"));
        Assert.Equal("orchard", state.CurrentRoomId);
        Assert.Equal(3, state.DiscoveredCount);
    }

    // Locked-door fog security.

    [Fact]
    public void Attempting_a_locked_door_leaks_nothing_and_mutates_nothing()
    {
        var state = new MapState();

        // The snapshot shows the corridor outside the locked door. The door's
        // far side is an unexplored stub only: a direction, no destination.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("vault_antechamber", 4, 4, 0, "ashmarch", "Vault Antechamber") },
            frontier: new[] { MapPayloads.Frontier("vault_antechamber", "north") },
            currentRoomId: "vault_antechamber",
            currentAreaId: "ashmarch",
            currentZ: 0,
            discoveredCount: 1,
            shownCount: 1));

        var roomsBefore = state.Rooms.Count;
        var frontierBefore = state.GetFrontier("vault_antechamber").Count;

        // Movement is attempted through the locked door. Keystone rejects it and
        // answers movement.failed with NO map.room.discovered, and no
        // map.position naming the hidden destination. The client therefore
        // receives nothing but a position event saying the player is still
        // exactly where they were.
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "vault_antechamber",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 4, Y = 4, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        // The destination never appears, in any form.
        Assert.Equal(roomsBefore, state.Rooms.Count);
        Assert.False(state.TryGetRoom("vault_inner", out _));

        // No hidden id, name or coordinates anywhere in the state.
        var serialised = string.Join('|',
            state.Rooms.Values.Select(r => $"{r.RoomId}:{r.Name}:{r.X},{r.Y},{r.Z}"));
        Assert.DoesNotContain("vault_inner", serialised, StringComparison.OrdinalIgnoreCase);

        // The frontier remains appropriately unexplored: still there, still
        // direction-only, still naming no destination.
        Assert.Equal(frontierBefore, state.GetFrontier("vault_antechamber").Count);
        var stub = Assert.Single(state.GetFrontier("vault_antechamber"));
        Assert.Equal(MapDirection.North, stub.DirectionKind);
        Assert.Equal("unexplored", stub.State);

        // Structurally a frontier record CANNOT name a destination: the record
        // type has no such field. The fog guarantee is in the wire contract,
        // not in client-side filtering.
        Assert.Null(typeof(MapFrontierRecord).GetProperty("To"));
        Assert.Null(typeof(MapFrontierRecord).GetProperty("Destination"));

        // And the player marker did not move.
        Assert.Equal("vault_antechamber", state.CurrentRoomId);
    }

    [Fact]
    public void Unlocking_and_traversing_discovers_the_room_only_after_arrival()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("vault_antechamber", 4, 4, 0, "ashmarch", "Vault Antechamber") },
            frontier: new[] { MapPayloads.Frontier("vault_antechamber", "north") },
            currentRoomId: "vault_antechamber",
            currentAreaId: "ashmarch",
            currentZ: 0,
            discoveredCount: 1,
            shownCount: 1));

        // Unlocking on its own reveals nothing. A door being openable is not the
        // same as the room beyond it being discovered.
        Assert.Single(state.Rooms);
        Assert.True(state.HasFrontier("vault_antechamber"));

        // Only the successful arrival emits the discovery.
        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("vault_inner", 4, 7, 0, "ashmarch", "Vault Inner Chamber"),
            DiscoveredCount = 2,
        });

        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "vault_inner",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 4, Y = 7, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        Assert.Equal(2, state.Rooms.Count);
        Assert.True(state.TryGetRoom("vault_inner", out var inner));
        Assert.Equal("Vault Inner Chamber", inner!.Name);
        Assert.Equal("vault_inner", state.CurrentRoomId);
        Assert.False(state.HasFrontier("vault_antechamber"));
    }

    // Vertical movement.

    [Fact]
    public void Going_down_discovers_the_room_below_and_moves_the_marker_to_that_floor()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[] { MapPayloads.Room("cellar_stair", 7, 7, 0, "ashmarch", "Cellar Stair") },
            frontier: new[] { MapPayloads.Frontier("cellar_stair", "down") },
            currentRoomId: "cellar_stair",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 1,
            shownCount: 1));

        // The unexplored lower level is a stub, never a room you can step into.
        Assert.True(state.HasFrontier("cellar_stair"));
        Assert.False(state.TryGetAdjacentDirection("cellar_stair", "cellar_vault", out _));

        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("cellar_vault", 7, 7, -1, "ashmarch", "Cellar Vault"),
            DiscoveredCount = 2,
        });

        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "cellar_vault",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 7, Y = 7, Z = -1 },
            CurrentZ = -1,
            IsDiscovered = true,
        });

        // The floor list is derived from Keystone data, not a dense range.
        Assert.Equal(new[] { -1, 0 }, state.Floors);
        Assert.Equal(-1, state.CurrentZ);

        // The previous floor stays selectable because it is discovered.
        Assert.Contains(0, state.Floors);
        state.SelectedZ = 0;
        Assert.Equal(0, state.EffectiveFloor);
        Assert.Contains("cellar_stair", state.VisibleRoomsOnFloor.Select(r => r.RoomId));

        // Releasing the pin returns the view to the player's own floor.
        state.SelectedZ = null;
        Assert.Equal(-1, state.EffectiveFloor);
        Assert.Contains("cellar_vault", state.VisibleRoomsOnFloor.Select(r => r.RoomId));
    }

    // Reconnect.

    [Fact]
    public void Reconnect_snapshot_restores_discovery_with_no_duplicates_or_stale_state()
    {
        var state = new MapState();

        // Session one: discover two more rooms and move to the second.
        state.ApplySnapshot(LoginSnapshot());
        MoveToFieldGate(state);
        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
            DiscoveredCount = 3,
        });
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "orchard",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 10, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });
        state.ApplyRoomDiscovered(new MapRoomDiscoveredPayload
        {
            Room = MapPayloads.Room("mill_road", 13, 26, 0, "ashmarch", "Mill Road"),
            DiscoveredCount = 4,
        });
        state.ApplyPosition(new MapPositionPayload
        {
            CurrentRoomId = "mill_road",
            CurrentAreaId = "ashmarch",
            CurrentPosition = new MapPosition { X = 13, Y = 26, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true,
        });

        Assert.Equal(4, state.Rooms.Count);

        // Disconnect. MainWindow clears the map on losing the session, exactly
        // as the real client does, so nothing stale can be observed.
        state.Reset();
        Assert.Empty(state.Rooms);
        Assert.False(state.HasSnapshot);

        // Reconnect: authenticate, and Keystone re-sends the complete snapshot
        // for the same character.
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("homestead", 10, 20, 0, "ashmarch", "The Homestead"),
                MapPayloads.Room("field_gate", 10, 23, 0, "ashmarch", "The Field Gate"),
                MapPayloads.Room("orchard", 10, 26, 0, "ashmarch", "The Orchard"),
                MapPayloads.Room("mill_road", 13, 26, 0, "ashmarch", "Mill Road"),
            },
            edges: new[]
            {
                MapPayloads.Edge("homestead", "field_gate", "north"),
                MapPayloads.Edge("field_gate", "homestead", "south"),
                MapPayloads.Edge("field_gate", "orchard", "north"),
                MapPayloads.Edge("orchard", "field_gate", "south"),
                MapPayloads.Edge("orchard", "mill_road", "east"),
                MapPayloads.Edge("mill_road", "orchard", "west"),
            },
            frontier: new[] { MapPayloads.Frontier("mill_road", "east") },
            currentRoomId: "mill_road",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 4,
            shownCount: 4));

        Assert.Equal(4, state.Rooms.Count);
        Assert.Equal(6, state.Edges.Count);
        Assert.Equal("mill_road", state.CurrentRoomId);
        Assert.Equal("ashmarch", state.CurrentAreaId);
        Assert.Equal(0, state.CurrentZ);
        Assert.Equal(new[] { "ashmarch" }, state.Areas);

        // No duplicate rooms and no duplicate edges.
        Assert.Equal(4, state.Rooms.Values.Select(r => r.RoomId).Distinct().Count());
        Assert.Equal(6, state.Edges.Select(e => (e.From, e.To, e.Direction)).Distinct().Count());

        // Frontier is exactly what the new snapshot said.
        var stub = Assert.Single(state.GetFrontier("mill_road"));
        Assert.Equal(MapDirection.East, stub.DirectionKind);
    }

    [Fact]
    public void Area_filter_shows_only_discovered_rooms_and_never_a_hidden_area()
    {
        var state = new MapState();
        state.ApplySnapshot(MapPayloads.Snapshot(
            rooms: new[]
            {
                MapPayloads.Room("m1", 1, 1, 0, "ashmarch", "Marsh One"),
                MapPayloads.Room("m2", 2, 1, 0, "ashmarch", "Marsh Two"),
                MapPayloads.Room("v1", 90, 90, 0, "valroian_reach", "Vale One"),
            },
            edges: new[]
            {
                MapPayloads.Edge("m1", "m2", "east"),
                MapPayloads.Edge("m2", "m1", "west"),
            },
            currentRoomId: "m1",
            currentAreaId: "ashmarch",
            currentZ: 0,
            floors: new[] { 0 },
            discoveredCount: 3,
            shownCount: 3));

        // Current-area default: the filter follows the player.
        Assert.True(state.AreaFilterFollowsPlayer);
        Assert.Equal("ashmarch", state.SelectedAreaId);
        Assert.Equal(new[] { "m1", "m2" }, state.VisibleRooms.Select(r => r.RoomId));

        // Switching area shows only that area's discovered rooms.
        state.SelectedAreaId = "valroian_reach";
        Assert.Equal(new[] { "v1" }, state.VisibleRooms.Select(r => r.RoomId));

        // The area list contains only areas Keystone actually disclosed.
        Assert.Equal(new[] { "ashmarch", "valroian_reach" }, state.Areas);
    }
}