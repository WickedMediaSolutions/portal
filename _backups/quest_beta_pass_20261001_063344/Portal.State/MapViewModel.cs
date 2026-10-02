using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>One selectable area in the map's area filter.</summary>
/// <param name="AreaId">The raw Keystone <c>area_id</c>.</param>
public sealed record MapAreaOption(string AreaId)
{
    /// <summary>
    /// A readable form of the id, e.g. "mordrath_ashmarch" -&gt; "Mordrath Ashmarch".
    /// </summary>
    /// <remarks>
    /// Purely cosmetic. Keystone sends only the opaque <c>area_id</c> in the
    /// snapshot and deliberately withholds the full area lore, so the display
    /// label is derived from the id itself and adds no hidden information.
    /// Undiscovered areas are never listed at all.
    /// </remarks>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(AreaId)) return "(none)";

            var parts = AreaId.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts.Select(p =>
                p.Length <= 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..]));
        }
    }
}

/// <summary>One selectable floor in the map's Z selector.</summary>
/// <param name="Z">The world Z level.</param>
public sealed record MapFloorOption(int Z)
{
    /// <summary>The floor as shown to the user, e.g. "Z -3".</summary>
    public string DisplayName => $"Z {Z:+#;-#;0}";
}

/// <summary>
/// The bindable view-model behind the Portal map panel: it owns the map state,
/// the viewport, the filter selections and the inspected room.
/// </summary>
/// <remarks>
/// <para>
/// All four Keystone map events funnel in here through
/// <see cref="ApplySnapshot"/>, <see cref="ApplyRoomDiscovered"/>,
/// <see cref="ApplyPosition"/> and <see cref="ApplyState"/>, which simply
/// forward to <see cref="MapState"/>. This class owns the local view concerns:
/// which area/floor is selected, which room is inspected, and whether the view
/// should follow the player.
/// </para>
/// <para>
/// Auto-follow is OFF by default and is never forced on. Movement only
/// re-centres the map when the user has explicitly enabled following, so
/// inspecting another region is never interrupted mid-read.
/// </para>
/// </remarks>
public sealed class MapViewModel : INotifyPropertyChanged, IMapEventSink
{
    private string? _selectedRoomId;
    private bool _autoFollow = true;
    private string? _statusText;
    private bool _hasAppliedInitialSnapshot;

    /// <summary>Creates the view-model with a fresh state and viewport.</summary>
    public MapViewModel()
    {
        State = new MapState();
        Viewport = new MapViewport();

        State.PropertyChanged += (_, _) => OnStateChanged();
        Viewport.PropertyChanged += (_, _) => Raise();
    }

    /// <summary>Raised whenever any bindable property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The underlying fog-of-war model.</summary>
    public MapState State { get; }

    /// <summary>The zoom/pan transform.</summary>
    public MapViewport Viewport { get; }

    // ─── Selection ───────────────────────────────────────────────────────

    /// <summary>
    /// The room currently inspected in the details pane, or null.
    /// </summary>
    /// <remarks>
    /// Always a <c>world_room_id</c>. Setting this to an id that is not in the
    /// map is ignored, so a stale click can never surface a room that Keystone
    /// has not disclosed.
    /// </remarks>
    public string? SelectedRoomId
    {
        get => _selectedRoomId;
        set
        {
            if (_selectedRoomId == value) return;
            if (value is not null && !State.TryGetRoom(value, out _))
            {
                // Refuse to inspect anything the map does not actually contain.
                return;
            }
            _selectedRoomId = value;
            Raise();
            RaiseSelectionDerived();
        }
    }

    /// <summary>The inspected room, or null when nothing is selected.</summary>
    public MapRoomRecord? SelectedRoom =>
        _selectedRoomId is not null && State.TryGetRoom(_selectedRoomId, out var room)
            ? room
            : null;

    /// <summary>Title line for the details pane.</summary>
    public string SelectedRoomTitle
    {
        get
        {
            var room = SelectedRoom;
            if (room is null) return "No room selected";

            var name = string.IsNullOrWhiteSpace(room.Name) ? room.RoomId : room.Name;
            return State.IsCurrentRoom(room.RoomId) ? $"▶ YOU — {name}" : name;
        }
    }

    /// <summary>
    /// The detailed facts shown for the selected room. Every line is sourced
    /// from the payload Keystone sent; nothing is derived from world files.
    /// </summary>
    public string SelectedRoomDetails
    {
        get
        {
            var room = SelectedRoom;
            if (room is null) return "Click a room on the map to inspect it.";

            var lines = new List<string>
            {
                $"Area:   {room.AreaId}",
                $"Coords: {room.X}, {room.Y}, {room.Z}  (floor Z {room.Z})"
            };

            if (!string.IsNullOrWhiteSpace(room.District))
                lines.Add($"District: {room.District}");

            lines.Add($"Markers: {DescribeMarkers(room)}");
            lines.Add($"Opens:  {DescribeDirections(room)}");

            if (State.IsCurrentRoom(room.RoomId))
                lines.Add("This is where your character is standing.");

            if (State.TryGetAdjacentDirection(State.CurrentRoomId, room.RoomId, out var moveDir))
                lines.Add($"Click MOVE to step {MapDirections.ToDisplayName(moveDir)} here.");

            return string.Join('\n', lines);
        }
    }

    /// <summary>
    /// The discovered directions leaving the selected room, in a stable order.
    /// </summary>
    /// <remarks>
    /// Only edges between two DISCOVERED rooms appear here. Frontier stubs are
    /// listed separately and never named a destination.
    /// </remarks>
    public string SelectedRoomDirections => DescribeDirections(SelectedRoom);

    /// <summary>The unexplored exits leaving the selected room.</summary>
    public string SelectedRoomFrontier
    {
        get
        {
            var room = SelectedRoom;
            if (room is null) return string.Empty;

            var stubs = State.GetFrontier(room.RoomId);
            if (stubs.Count == 0) return string.Empty;

            // Direction only. There is deliberately nothing to show about what
            // lies beyond these exits, because Keystone has not said.
            return string.Join(", ", stubs
                .Select(s => MapDirections.ToLabel(s.DirectionKind))
                .OrderBy(s => s, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The direction that would move the player into the selected room, when
    /// that room is directly adjacent to the player's current room.
    /// </summary>
    public MapDirection? PendingMoveDirection =>
        SelectedRoom is not null &&
        State.TryGetAdjacentDirection(State.CurrentRoomId, SelectedRoom.RoomId, out var dir)
            ? dir
            : null;

    /// <summary>True when the selected room can be stepped into with one move.</summary>
    public bool CanMoveToSelectedRoom => PendingMoveDirection.HasValue;

    // ─── Filters ─────────────────────────────────────────────────────────

    /// <summary>The areas present in the map filter, from Keystone's discovered data only.</summary>
    public ObservableCollection<MapAreaOption> AvailableAreas { get; } = new();

    /// <summary>The floors present in the Z selector for the selected area.</summary>
    public ObservableCollection<MapFloorOption> AvailableFloors { get; } = new();

    /// <summary>
    /// The selected area, or null for "all discovered areas".
    /// </summary>
    public MapAreaOption? SelectedArea
    {
        get => AvailableAreas.FirstOrDefault(a => a.AreaId == State.SelectedAreaId);
        set => State.SelectedAreaId = value?.AreaId;
    }

    /// <summary>
    /// The selected floor, or null for "all floors".
    /// </summary>
    public MapFloorOption? SelectedFloor
    {
        get => State.SelectedZ is int z
            ? AvailableFloors.FirstOrDefault(f => f.Z == z)
            : null;
        set => State.SelectedZ = value?.Z;
    }

    /// <summary>
    /// When true, the view re-centres on the player as they move.
    /// </summary>
    /// <remarks>
    /// Enabled by default because following the character is the map's main
    /// navigation purpose. Manual panning never disables it silently; a user who
    /// wants to stay put while exploring simply unticks it.
    /// </remarks>
    public bool AutoFollow
    {
        get => _autoFollow;
        set
        {
            if (_autoFollow == value) return;
            _autoFollow = value;
            Raise();
            if (value) CenterOnPlayer();
        }
    }

    // ─── Summary text ───────────────────────────────────────────────────

    /// <summary>
    /// The title of the room the character is currently standing in.
    /// </summary>
    /// <remarks>
    /// Sourced exclusively from the server's authoritative
    /// <c>map.position</c> / <c>map.snapshot</c> room record, so the header
    /// can never show a hardcoded or stale room name.  Before the first
    /// position arrives (or while disconnected) this reports
    /// <c>"Location: unknown"</c> rather than inventing a value.
    /// </remarks>
    public string CurrentRoomTitle
    {
        get
        {
            if (State.CurrentRoomId is null)
                return "Location: unknown";

            return State.TryGetRoom(State.CurrentRoomId, out var room)
                ? $"Room: {room.Name}"
                : "Location: unknown";
        }
    }

    /// <summary>A one-line progress summary: "Discovered 42 of 7943".</summary>
    public string DiscoverySummary
    {
        get
        {
            if (State.TotalGeneratedRooms > 0)
                return $"Discovered {State.DiscoveredCount} of {State.TotalGeneratedRooms}";
            return $"Discovered {State.DiscoveredCount}";
        }
    }

    /// <summary>The current area / floor / coordinates line.</summary>
    public string LocationSummary
    {
        get
        {
            if (State.CurrentRoomId is null) return "Location: unknown";

            var area = string.IsNullOrEmpty(State.CurrentAreaId) ? "--" : State.CurrentAreaId;
            var floor = State.CurrentZ?.ToString() ?? "--";

            return State.TryGetRoom(State.CurrentRoomId, out var room)
                ? $"Area: {area}   Floor Z: {floor}   At: {room.X}, {room.Y}"
                : $"Area: {area}   Floor Z: {floor}";
        }
    }

    /// <summary>How many rooms the current filters are showing.</summary>
    public int VisibleRoomCount => State.VisibleRoomsOnFloor.Count;

    /// <summary>How many unexplored exits the current floor shows.</summary>
    public int VisibleFrontierCount =>
        State.VisibleRoomsOnFloor.Sum(r => State.GetFrontier(r.RoomId).Count);

    /// <summary>True when the map has nothing to draw yet.</summary>
    public bool HasNoRooms => State.IsEmpty;

    /// <summary>
    /// A short hint explaining why the map is empty, or a live status line.
    /// </summary>
    public string StatusText
    {
        get => _statusText ?? DefaultStatus();
        set { if (_statusText != value) { _statusText = value; Raise(); } }
    }

    /// <summary>True when the current filters and floor yield nothing to draw.</summary>
    public bool IsMapEmpty => State.IsEmpty || State.VisibleRoomsOnFloor.Count == 0;

    private string DefaultStatus()
    {
        if (State.IsEmpty)
            return "No rooms discovered yet.";

        // A floor can legitimately contain nothing the character has explored
        // yet. Say so plainly instead of showing an unexplained blank map.
        if (VisibleRoomCount == 0)
        {
            var floor = State.EffectiveFloor;
            return floor is int z
                ? $"No discovered rooms on floor Z {z} yet."
                : "No discovered rooms in view yet.";
        }

        return $"{VisibleRoomCount} rooms shown";
    }

    // ─── Actions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Centres the map on the player's current room at the default zoom.
    /// </summary>
    /// <remarks>
    /// Uses only the current room's own authoritative coordinates. If the room is
    /// not (yet) in the map there is nothing legitimate to centre on, so the
    /// view is left untouched rather than jumping to a guess.
    /// </remarks>
    public void CenterOnPlayer()
    {
        if (State.CurrentRoomId is null) return;
        if (!State.TryGetRoom(State.CurrentRoomId, out var room)) return;
        Viewport.Reset(room.X, room.Y);
    }

    /// <summary>
    /// Centres on the player without changing the user's current zoom level.
    /// </summary>
    public void RecenterOnPlayerKeepingZoom()
    {
        if (State.CurrentRoomId is null) return;
        if (!State.TryGetRoom(State.CurrentRoomId, out var room)) return;
        Viewport.CenterOn(room);
    }

    /// <summary>
    /// Zooms and pans so every room on the CURRENT floor fits on screen.
    /// </summary>
    /// <remarks>
    /// Fits only the floor actually being drawn. Fitting every floor at once
    /// would zoom far out and shrink the current floor to a few pixels.
    /// </remarks>
    public void FitToVisibleRooms()
    {
        var visible = State.VisibleRoomsOnFloor;
        if (visible.Count == 0) return;
        Viewport.FitTo(visible);
    }

    /// <summary>Restores the default zoom, keeping the current centre.</summary>
    public void ResetZoom()
    {
        var (wx, wy) = Viewport.Unproject(Viewport.ViewportCentre);
        Viewport.CenterOn((int)Math.Round(wx), (int)Math.Round(wy), MapViewport.DefaultZoom);
    }

    /// <summary>Steps to the next available floor above the selected one.</summary>
    public void SelectFloorUp() => StepFloor(+1);

    /// <summary>Steps to the next available floor below the selected one.</summary>
    public void SelectFloorDown() => StepFloor(-1);

    /// <summary>
    /// Moves the floor selection by <paramref name="step"/> through the floors
    /// that ACTUALLY exist for the current area.
    /// </summary>
    /// <remarks>
    /// The realm's Z range is -26..+14 but those levels are deliberately
    /// non-contiguous per area, so stepping walks the real list Keystone
    /// reported rather than incrementing an integer. That means "floor up" lands
    /// on the next genuine level and never on a floor that does not exist.
    /// </remarks>
    private void StepFloor(int step)
    {
        var floors = State.FloorsForSelectedArea;
        if (floors.Count == 0) return;

        // When no floor is pinned, step relative to the floor the player is
        // actually standing on, so "up" means up rather than "select current".
        var current = State.SelectedZ ?? State.CurrentZ;

        var index = current is int z ? IndexOfFloor(floors, z) : -1;

        if (index < 0)
        {
            // The floor is unknown or not in this area's list: snap to the
            // closest real level in the direction of travel.
            var seed = current ?? (step > 0 ? floors[0] : floors[^1]);

            int nearest;
            if (step > 0)
            {
                var above = IndexOfFloorAtLeast(floors, seed);
                nearest = above >= 0 ? floors[above] : floors[^1];
            }
            else
            {
                var below = IndexOfFloorAtMost(floors, seed);
                nearest = below >= 0 ? floors[below] : floors[0];
            }

            State.SelectedZ = nearest;
            return;
        }

        var target = index + step;
        if (target < 0 || target >= floors.Count) return; // already at an end
        State.SelectedZ = floors[target];
    }

    /// <summary>Index of a floor within the available list, or -1 when absent.</summary>
    private static int IndexOfFloor(IReadOnlyList<int> floors, int z)
    {
        for (var i = 0; i < floors.Count; i++)
        {
            if (floors[i] == z) return i;
        }
        return -1;
    }

    /// <summary>Index of the lowest floor that is &gt;= <paramref name="z"/>, or -1.</summary>
    private static int IndexOfFloorAtLeast(IReadOnlyList<int> floors, int z)
    {
        for (var i = 0; i < floors.Count; i++)
        {
            if (floors[i] >= z) return i;
        }
        return -1;
    }

    /// <summary>Index of the highest floor that is &lt;= <paramref name="z"/>, or -1.</summary>
    private static int IndexOfFloorAtMost(IReadOnlyList<int> floors, int z)
    {
        for (var i = floors.Count - 1; i >= 0; i--)
        {
            if (floors[i] <= z) return i;
        }
        return -1;
    }

    /// <summary>
    /// Selects the room the player currently occupies, so the details pane
    /// follows the character.
    /// </summary>
    public void SelectCurrentRoom() => SelectedRoomId = State.CurrentRoomId;

    // ─── Keystone event handling (IMapEventSink) ─────────────────────────
    //
    // Called on the UI dispatcher by GameEventService. Each handler applies the
    // payload and then updates only the local view concerns.

    /// <inheritdoc />
    public void ApplyMapSnapshot(MapSnapshotPayload payload)
    {
        State.ApplySnapshot(payload);

        // The room selected in the previous session may not be in this snapshot
        // (a different character, or a fresh discovery set). Drop the selection
        // rather than keeping a reference to a room that is no longer known.
        if (_selectedRoomId is not null && !State.TryGetRoom(_selectedRoomId, out _))
            _selectedRoomId = null;

        if (!_hasAppliedInitialSnapshot)
        {
            _hasAppliedInitialSnapshot = true;

            // First snapshot of the session: centre once, then leave the camera
            // alone until the user or auto-follow moves it.
            CenterOnPlayer();
            _statusText = null;
        }

        RefreshFilterOptions();
    }

    /// <inheritdoc />
    public void ApplyMapRoomDiscovered(MapRoomDiscoveredPayload payload)
    {
        State.ApplyRoomDiscovered(payload);

        if (_selectedRoomId is not null && !State.TryGetRoom(_selectedRoomId, out _))
            _selectedRoomId = null;

        RefreshFilterOptions();
    }

    /// <inheritdoc />
    public void ApplyMapPosition(MapPositionPayload payload)
    {
        State.ApplyPosition(payload);

        // Follow the player only when auto-follow is on. With it off, a move
        // never yanks the view away from a region being inspected.
        if (AutoFollow && State.CurrentRoomId is not null &&
            State.TryGetRoom(State.CurrentRoomId, out var room))
        {
            Viewport.CenterOn(room);
        }

        RefreshFilterOptions();
    }

    /// <inheritdoc />
    public void ApplyMapState(MapStatePayload payload)
    {
        State.ApplyState(payload);
    }

    /// <summary>
    /// Clears the map for logout / disconnect / reconnect.
    /// </summary>
    /// <remarks>
    /// Keystone always re-sends a complete <c>map.snapshot</c> on reconnect, so
    /// starting from an empty map guarantees nothing stale from the previous
    /// session can ever be shown.
    /// </remarks>
    public void ClearMap()
    {
        State.Reset();
        _selectedRoomId = null;
        _hasAppliedInitialSnapshot = false;
        _statusText = null;
        RefreshFilterOptions();
        RaiseSelectionDerived();
    }

    // ─── Internals ───────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the area and floor selector options from the discovered data.
    /// </summary>
    /// <remarks>
    /// Only areas and floors that actually contain DISCOVERED rooms are offered,
    /// so the selectors can never name an area the character has not seen. The
    /// floor list follows the selected area, because Keystone's Z levels are not
    /// uniform across areas.
    /// </remarks>
    private void RefreshFilterOptions()
    {
        var areas = State.Areas;
        if (!AvailableAreas.Select(a => a.AreaId).SequenceEqual(areas, StringComparer.Ordinal))
        {
            var previous = State.SelectedAreaId;
            AvailableAreas.Clear();
            foreach (var area in areas)
                AvailableAreas.Add(new MapAreaOption(area));

            // The default is the character's current area, which is the most
            // useful starting view for navigation.
            State.SelectedAreaId =
                areas.Contains(previous, StringComparer.Ordinal) ? previous : State.CurrentAreaId;
            Raise(nameof(SelectedArea));
        }

        var floors = State.FloorsForSelectedArea;
        var floorOptions = floors.Select(z => new MapFloorOption(z)).ToArray();
        if (!AvailableFloors.SequenceEqual(floorOptions))
        {
            AvailableFloors.Clear();
            foreach (var floor in floorOptions)
                AvailableFloors.Add(floor);
            Raise(nameof(SelectedFloor));
        }

        Raise(nameof(AvailableAreas));
        Raise(nameof(AvailableFloors));
        RaiseAllSummary();
    }

    /// <summary>
    /// Reacts to any change in the underlying map state by refreshing the
    /// derived, bindable projections.
    /// </summary>
    private void OnStateChanged()
    {
        RaiseAllSummary();
        RaiseSelectionDerived();
    }

    private void RaiseAllSummary()
    {
        Raise(nameof(CurrentRoomTitle));
        Raise(nameof(DiscoverySummary));
        Raise(nameof(LocationSummary));
        Raise(nameof(VisibleRoomCount));
        Raise(nameof(VisibleFrontierCount));
        Raise(nameof(HasNoRooms));
        Raise(nameof(IsMapEmpty));
        Raise(nameof(StatusText));
    }

    private void RaiseSelectionDerived()
    {
        Raise(nameof(SelectedRoom));
        Raise(nameof(SelectedRoomTitle));
        Raise(nameof(SelectedRoomDetails));
        Raise(nameof(SelectedRoomDirections));
        Raise(nameof(SelectedRoomFrontier));
        Raise(nameof(PendingMoveDirection));
        Raise(nameof(CanMoveToSelectedRoom));
    }

    /// <summary>
    /// Formats a room's markers for display. Only markers Keystone actually sent
    /// are shown, and only for discovered rooms.
    /// </summary>
    private static string DescribeMarkers(MapRoomRecord? room)
    {
        if (room is null) return "--";
        if (room.Markers.Count == 0) return "none";

        // Distinct and sorted so the text is stable between refreshes.
        return string.Join(", ", room.Markers
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().Replace('_', ' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Formats the discovered exits of a room, marking doors and vertical links.
    /// </summary>
    private string DescribeDirections(MapRoomRecord? room)
    {
        if (room is null) return "--";

        var edges = State.GetOutgoingEdges(room.RoomId);
        if (edges.Count == 0) return "none";

        var parts = new List<string>(edges.Count);
        foreach (var edge in edges)
        {
            var direction = edge.DirectionKind;
            var label = MapDirections.ToDisplayName(direction);

            // A door marker means only that the real exit passes through a door.
            // Keystone does not report whether it is open or locked, so nothing
            // further is claimed here.
            if (edge.Door) label += " (door)";
            if (MapDirections.IsVertical(direction)) label += " (floor link)";

            parts.Add(label);
        }

        return string.Join(", ", parts);
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}