using Portal.Protocol;
using Portal.State;

namespace Portal.Map.Tests;

/// <summary>
/// Covers zoom limits, pan state, centring and floor stepping.
/// </summary>
public sealed class MapViewportTests
{
    /// <summary>A viewport with a real, non-degenerate size.</summary>
    private static MapViewport Sized(double w = 800, double h = 600)
    {
        var viewport = new MapViewport();
        viewport.SetViewportSize(w, h);
        return viewport;
    }

    [Fact]
    public void Zoom_is_clamped_at_both_ends_however_many_times_it_is_applied()
    {
        var viewport = Sized();

        for (var i = 0; i < 500; i++) viewport.ZoomIn();
        Assert.Equal(MapViewport.MaxZoom, viewport.Zoom, 6);

        for (var i = 0; i < 500; i++) viewport.ZoomOut();
        Assert.Equal(MapViewport.MinZoom, viewport.Zoom, 6);
    }

    [Fact]
    public void Zoom_ignores_degenerate_factors()
    {
        var viewport = Sized();
        var before = viewport.Zoom;

        viewport.ZoomAt(new MapPoint(10, 10), 0);
        viewport.ZoomAt(new MapPoint(10, 10), -3);
        viewport.ZoomAt(new MapPoint(10, 10), double.NaN);
        viewport.ZoomAt(new MapPoint(10, 10), double.PositiveInfinity);

        Assert.Equal(before, viewport.Zoom, 6);
    }

    [Fact]
    public void Zoom_percent_tracks_the_zoom_factor()
    {
        var viewport = Sized();
        viewport.ZoomIn();
        Assert.Equal((int)Math.Round(viewport.Zoom * 100), viewport.ZoomPercent);
    }

    [Fact]
    public void Anchored_zoom_keeps_the_point_under_the_cursor_fixed()
    {
        var viewport = Sized();
        viewport.CenterOn(10, 10);

        var anchor = new MapPoint(300, 200);
        var before = viewport.Unproject(anchor);

        for (var i = 0; i < 5; i++)
            viewport.ZoomAt(anchor, 1.3);

        var after = viewport.Unproject(anchor);

        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
    }

    [Fact]
    public void Projection_round_trips_through_unproject()
    {
        var viewport = Sized();
        viewport.CenterOn(-7, 13);
        viewport.ZoomIn();
        viewport.ZoomIn();

        foreach (var (x, y) in new[] { (0, 0), (5, -9), (-120, 44), (999, -999) })
        {
            var screen = viewport.Project(x, y);
            var back = viewport.Unproject(screen);

            Assert.Equal(x, back.X, 6);
            Assert.Equal(y, back.Y, 6);
        }
    }

    [Fact]
    public void World_north_is_drawn_upward_so_the_map_is_not_mirrored()
    {
        var viewport = Sized();
        viewport.CenterOn(0, 0);

        var origin = viewport.Project(0, 0);
        var north = viewport.Project(0, 10);
        var south = viewport.Project(0, -10);

        // Keystone's north is +Y, and +Y must render ABOVE the origin on screen.
        Assert.True(north.Y < origin.Y);
        Assert.True(south.Y > origin.Y);

        var east = viewport.Project(10, 0);
        Assert.True(east.X > origin.X);
    }

    [Fact]
    public void Pan_moves_the_origin_and_survives_repeated_drags()
    {
        var viewport = Sized();
        var startX = viewport.OriginX;
        var startY = viewport.OriginY;

        viewport.PanBy(40, 25);
        Assert.Equal(startX + 40, viewport.OriginX, 6);
        Assert.Equal(startY + 25, viewport.OriginY, 6);

        viewport.PanBy(-10, -5);
        Assert.Equal(startX + 30, viewport.OriginX, 6);
        Assert.Equal(startY + 20, viewport.OriginY, 6);
    }

    [Fact]
    public void Pan_ignores_non_finite_deltas()
    {
        var viewport = Sized();
        var originX = viewport.OriginX;

        viewport.PanBy(double.NaN, 10);
        viewport.PanBy(double.PositiveInfinity, 10);

        Assert.Equal(originX, viewport.OriginX, 6);
    }

    [Fact]
    public void Center_on_places_the_world_point_at_the_middle_of_the_viewport()
    {
        var viewport = Sized(800, 600);

        viewport.CenterOn(42, -17);

        var centre = viewport.ViewportCentre;
        Assert.Equal(400, centre.X, 6);
        Assert.Equal(300, centre.Y, 6);

        var projected = viewport.Project(42, -17);
        Assert.Equal(centre.X, projected.X, 6);
        Assert.Equal(centre.Y, projected.Y, 6);
    }

    [Fact]
    public void Reset_restores_the_default_zoom_and_centres()
    {
        var viewport = Sized();
        viewport.ZoomIn();
        viewport.ZoomIn();
        viewport.PanBy(500, 500);

        viewport.Reset(3, 4);

        Assert.Equal(MapViewport.DefaultZoom, viewport.Zoom, 6);

        var projected = viewport.Project(3, 4);
        Assert.Equal(viewport.ViewportCentre.X, projected.X, 6);
        Assert.Equal(viewport.ViewportCentre.Y, projected.Y, 6);
    }

    [Fact]
    public void Fit_brings_every_room_into_view_and_stays_within_zoom_limits()
    {
        var viewport = Sized(800, 600);

        var rooms = new List<MapRoomRecord>();
        for (var i = 0; i < 300; i++)
            rooms.Add(MapPayloads.Room($"r{i}", i % 20, i / 20, 0));

        viewport.FitTo(rooms);

        Assert.InRange(viewport.Zoom, MapViewport.MinZoom, MapViewport.MaxZoom);

        foreach (var room in rooms)
        {
            var p = viewport.Project(room);
            Assert.InRange(p.X, 0, 800);
            Assert.InRange(p.Y, 0, 600);
        }
    }

    [Fact]
    public void Fit_of_a_single_room_does_not_divide_by_zero()
    {
        var viewport = Sized();
        viewport.FitTo(new[] { MapPayloads.Room("only", 5, 5, 0) });

        Assert.InRange(viewport.Zoom, MapViewport.MinZoom, MapViewport.MaxZoom);
        Assert.False(double.IsNaN(viewport.OriginX));
        Assert.False(double.IsInfinity(viewport.OriginY));
    }

    [Fact]
    public void Fit_of_an_empty_set_is_a_no_op()
    {
        var viewport = Sized();
        var originX = viewport.OriginX;

        viewport.FitTo(Array.Empty<MapRoomRecord>());

        Assert.Equal(originX, viewport.OriginX, 6);
    }

    // MARKER_VP_TESTS
}

/// <summary>
/// Covers the map panel's view-model: filter options, floor stepping,
/// centre-on-player, auto-follow and room inspection.
/// </summary>
public sealed class MapViewModelTests
{
    private static MapSnapshotPayload World() => MapPayloads.Snapshot(
        rooms: new[]
        {
            MapPayloads.Room("a", 0, 0, 0, "mordrath_capital", "Alpha", "d1", "safe"),
            MapPayloads.Room("b", 1, 0, 0, "mordrath_capital", "Beta", "d1", "safe", "bank"),
            MapPayloads.Room("v1", 9, 9, 0, "valroian_capital", "Val One"),
            MapPayloads.Room("deep", 0, 0, -26, "mordrath_capital", "Deepest"),
            MapPayloads.Room("top", 0, 0, 14, "mordrath_capital", "Highest")
        },
        edges: new[]
        {
            MapPayloads.Edge("a", "b", "east"),
            MapPayloads.Edge("b", "a", "up")
        },
        frontier: new[] { MapPayloads.Frontier("b", "north") },
        currentRoomId: "a",
        currentAreaId: "mordrath_capital",
        currentZ: 0,
        floors: new[] { -26, 0, 14 });

    [Fact]
    public void Snapshot_populates_the_area_and_floor_selectors_from_discovered_data()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        Assert.Equal(
            new[] { "mordrath_capital", "valroian_capital" },
            vm.AvailableAreas.Select(a => a.AreaId).ToArray());

        // The floor list follows the selected (current) area and contains only
        // levels that actually exist there.
        Assert.Equal(new[] { -26, 0, 14 }, vm.AvailableFloors.Select(f => f.Z).ToArray());
    }

    [Fact]
    public void Area_display_name_is_derived_from_the_id_and_adds_no_hidden_data()
    {
        var option = new MapAreaOption("mordrath_ashmarch");

        Assert.Equal("Mordrath Ashmarch", option.DisplayName);
        Assert.Equal("mordrath_ashmarch", option.AreaId);
    }

    [Fact]
    public void Floor_stepping_walks_the_real_levels_and_stops_at_the_ends()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.SelectedArea = new MapAreaOption("mordrath_capital");
        vm.SelectedFloor = null;

        // Upwards from the player's floor: 0 -> 14, then it stops.
        vm.SelectFloorUp();
        Assert.Equal(14, vm.State.SelectedZ);
        vm.SelectFloorUp();
        Assert.Equal(14, vm.State.SelectedZ);

        // Downwards: 14 -> 0 -> -26, then it stops.
        vm.SelectFloorDown();
        Assert.Equal(0, vm.State.SelectedZ);
        vm.SelectFloorDown();
        Assert.Equal(-26, vm.State.SelectedZ);
        vm.SelectFloorDown();
        Assert.Equal(-26, vm.State.SelectedZ);
    }

[Fact]
    public void Center_on_player_places_the_current_room_in_the_middle()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);

        vm.ApplyMapSnapshot(World());
        vm.Viewport.PanBy(999, 999);
        vm.CenterOnPlayer();

        Assert.True(vm.State.TryGetRoom("a", out var room));
        var projected = vm.Viewport.Project(room);

        Assert.Equal(350, projected.X, 3);
        Assert.Equal(250, projected.Y, 3);
        Assert.Equal(MapViewport.DefaultZoom, vm.Viewport.Zoom, 6);
    }

    [Fact]
    public void Center_on_player_keeps_the_users_zoom_when_asked()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.Viewport.ZoomIn();
        vm.Viewport.ZoomIn();
        var zoom = vm.Viewport.Zoom;

        vm.RecenterOnPlayerKeepingZoom();

        Assert.Equal(zoom, vm.Viewport.Zoom, 6);
    }

    [Fact]
    public void Auto_follow_centres_on_movement_only_when_enabled()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        // Follow on (default): a move re-centres the map.
        Assert.True(vm.AutoFollow);
        vm.Viewport.PanBy(400, 400);

        vm.ApplyMapPosition(new MapPositionPayload
        {
            CurrentRoomId = "b",
            CurrentAreaId = "mordrath_capital",
            CurrentPosition = new MapPosition { X = 1, Y = 0, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true
        });

        Assert.True(vm.State.TryGetRoom("b", out var b));
        Assert.Equal(vm.Viewport.ViewportCentre.X, vm.Viewport.Project(b).X, 3);

        // Follow off: a move must NOT yank the view away.
        vm.AutoFollow = false;
        vm.Viewport.PanBy(500, 500);
        var originBefore = vm.Viewport.OriginX;

        vm.ApplyMapPosition(new MapPositionPayload
        {
            CurrentRoomId = "a",
            CurrentAreaId = "mordrath_capital",
            CurrentPosition = new MapPosition { X = 0, Y = 0, Z = 0 },
            CurrentZ = 0,
            IsDiscovered = true
        });

        Assert.Equal(originBefore, vm.Viewport.OriginX, 3);
    }

    [Fact]
    public void Selection_refuses_a_room_that_is_not_in_the_map()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.SelectedRoomId = "b";
        Assert.Equal("b", vm.SelectedRoomId);

        // A stale or invented id must be ignored outright.
        vm.SelectedRoomId = "never_discovered_room";
        Assert.Equal("b", vm.SelectedRoomId);
    }

    [Fact]
    public void Inspection_reports_only_discovered_facts()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.SelectedRoomId = "b";
        var details = vm.SelectedRoomDetails;

        Assert.Equal("Beta", vm.SelectedRoomTitle);
        Assert.Contains("mordrath_capital", details);
        Assert.Contains("1, 0, 0", details);
        Assert.Contains("d1", details);
        Assert.Contains("bank", details);

        // The up-edge to "a" is reported as a floor link, not a plain corridor.
        Assert.Contains("Up (floor link)", details);
    }

    [Fact]
    public void Adjacent_room_selection_offers_exactly_one_move_direction()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.SelectedRoomId = "b";
        Assert.True(vm.CanMoveToSelectedRoom);
        Assert.Equal(MapDirection.East, vm.PendingMoveDirection);

        // A non-adjacent room offers no movement at all.
        vm.SelectedRoomId = "v1";
        Assert.False(vm.CanMoveToSelectedRoom);
        Assert.Null(vm.PendingMoveDirection);
    }

    [Fact]
    public void Frontier_text_never_names_a_destination()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.SelectedRoomId = "b";

        // Just the direction label â€” never a room id or name.
        Assert.Equal("N", vm.SelectedRoomFrontier);
        Assert.DoesNotContain("room_", vm.SelectedRoomFrontier);
    }

    [Fact]
    public void Clear_map_empties_the_view_model_for_logout()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());
        vm.SelectedRoomId = "b";

        vm.ClearMap();

        Assert.True(vm.HasNoRooms);
        Assert.Null(vm.SelectedRoomId);
        Assert.Null(vm.SelectedRoom);
        Assert.Empty(vm.AvailableAreas);
        Assert.Empty(vm.AvailableFloors);
        Assert.Equal("No rooms discovered yet.", vm.StatusText);
    }

    [Fact]
    public void Discovery_summary_reports_progress_against_the_total()
    {
        var vm = new MapViewModel();
        vm.Viewport.SetViewportSize(700, 500);
        vm.ApplyMapSnapshot(World());

        vm.ApplyMapState(new MapStatePayload
        {
            DiscoveredCount = 512,
            TotalGeneratedRooms = 7943,
            CurrentAreaId = "mordrath_capital"
        });

        Assert.Equal("Discovered 512 of 7943", vm.DiscoverySummary);
    }
}
