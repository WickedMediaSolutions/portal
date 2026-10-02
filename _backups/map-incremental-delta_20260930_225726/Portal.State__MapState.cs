using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// The client's complete, fog-of-war-respecting model of one character's map.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of truth the map panel renders. It is populated
/// ONLY from Keystone's filtered events (<c>map.snapshot</c>,
/// <c>map.room.discovered</c>, <c>map.position</c>, <c>map.state</c>).
/// </para>
/// <para>
/// <b>Hard rules enforced by this type:</b>
/// </para>
/// <list type="number">
/// <item><description>Rooms are keyed by <c>world_room_id</c> only — never by
/// name, coordinates, or database id. Six frozen Phase 1 coordinate cells
/// overlap across the faction projections, so coordinate keying would silently
/// merge distinct rooms.</description></item>
/// <item><description>Undiscovered rooms are never created, never guessed, and
/// never given coordinates. A frontier entry stays a direction off a known
/// source room.</description></item>
/// <item><description>Nothing is ever persisted: the map lives for the session
/// and is rebuilt from <c>map.snapshot</c>, so a reconnect is always
/// authoritative and never stale.</description></item>
/// </list>
/// <para>
/// Area and floor filtering is applied for DISPLAY only. It never removes a room
/// from the underlying set, so switching filters back and forth is lossless.
/// </para>
/// </remarks>
public sealed class MapState : INotifyPropertyChanged
{
    // Backing stores. _rooms is the authoritative dictionary keyed by
    // world_room_id; _outgoingEdges is an index rebuilt alongside it.
    private readonly Dictionary<string, MapRoomRecord> _rooms = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<MapEdgeRecord>> _outgoingEdges =
        new(StringComparer.Ordinal);

    private string? _currentRoomId;
    private string? _currentAreaId;
    private int? _currentZ;
    private int _discoveredCount;
    private int _shownCount;
    private int _totalGeneratedRooms;

    // Display filters (local view state, never sent to the server).
    private string? _selectedAreaId;
    private bool _areaFilterFollowsPlayer = true;
    private int? _selectedZ;
    private bool _floorFilterFollowsPlayer = true;

    /// <summary>Raised whenever any bindable property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Every discovered room, keyed by its stable <c>world_room_id</c>.
    /// Consumers must key on <see cref="MapRoomRecord.RoomId"/> — never on name
    /// or coordinates.
    /// </summary>
    public IReadOnlyDictionary<string, MapRoomRecord> Rooms => _rooms;

    /// <summary>
    /// Every discovered-to-discovered edge. Both endpoints are always present in
    /// <see cref="Rooms"/>; an edge whose destination is unknown is a frontier
    /// entry, never an edge.
    /// </summary>
    public IReadOnlyCollection<MapEdgeRecord> Edges { get; private set; } =
        Array.Empty<MapEdgeRecord>();

    /// <summary>Every unexplored exit, keyed by its discovered source room.</summary>
    public IReadOnlyDictionary<string, List<MapFrontierRecord>> Frontier { get; private set; } =
        new Dictionary<string, List<MapFrontierRecord>>(StringComparer.Ordinal);

    /// <summary>Area ids present among the discovered rooms.</summary>
    public IReadOnlyList<string> Areas { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// The Z levels present among discovered rooms, as reported by Keystone.
    /// </summary>
    public IReadOnlyList<int> Floors { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// The Z levels the SELECTED area actually occupies, according to Keystone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the character's current area this is the server's own
    /// <c>floors</c> list, which describes every level the area genuinely uses —
    /// not just the ones this character has walked onto. The realm's Z levels
    /// are deliberately non-contiguous, so this must never be expanded into a
    /// dense -26..+14 range.
    /// </para>
    /// <para>
    /// For any other area, only the levels holding DISCOVERED rooms can be
    /// derived client-side, because Keystone discloses area floors for the
    /// current area only. Never inventing a floor keeps the selector honest.
    /// </para>
    /// </remarks>
    public IReadOnlyList<int> FloorsForSelectedArea
    {
        get
        {
            if (_selectedAreaId is null) return Floors;

            // The server's own answer for the current area.
            if (_currentAreaId is not null &&
                string.Equals(_selectedAreaId, _currentAreaId, StringComparison.Ordinal) &&
                Floors.Count > 0)
            {
                return Floors;
            }

            return _rooms.Values
                .Where(r => string.Equals(r.AreaId, _selectedAreaId, StringComparison.Ordinal))
                .Select(r => r.Z)
                .Distinct()
                .OrderBy(z => z)
                .ToArray();
        }
    }

    /// <summary>The <c>world_room_id</c> the character currently occupies.</summary>
    public string? CurrentRoomId
    {
        get => _currentRoomId;
        private set { if (_currentRoomId != value) { _currentRoomId = value; Raise(); } }
    }

    /// <summary>The <c>area_id</c> the character currently occupies.</summary>
    public string? CurrentAreaId
    {
        get => _currentAreaId;
        private set { if (_currentAreaId != value) { _currentAreaId = value; Raise(); } }
    }

    /// <summary>The character's current floor (world Z), when known.</summary>
    public int? CurrentZ
    {
        get => _currentZ;
        private set { if (_currentZ != value) { _currentZ = value; Raise(); } }
    }

    /// <summary>How many rooms the character has discovered in total.</summary>
    public int DiscoveredCount
    {
        get => _discoveredCount;
        private set { if (_discoveredCount != value) { _discoveredCount = value; Raise(); } }
    }

    /// <summary>How many rooms the last snapshot actually displayed.</summary>
    public int ShownCount
    {
        get => _shownCount;
        private set { if (_shownCount != value) { _shownCount = value; Raise(); } }
    }

    /// <summary>How many generated rooms the realm contains, per <c>map.state</c>.</summary>
    public int TotalGeneratedRooms
    {
        get => _totalGeneratedRooms;
        private set
        {
            if (_totalGeneratedRooms != value) { _totalGeneratedRooms = value; Raise(); }
        }
    }

    /// <summary>True once at least one snapshot has been applied.</summary>
    public bool HasSnapshot { get; private set; }

    /// <summary>True when no room has been discovered yet.</summary>
    public bool IsEmpty => _rooms.Count == 0;

    // ─── Display filters ─────────────────────────────────────────────────

    /// <summary>
    /// The area filter, or null for "all discovered areas". Display-only: it
    /// never hides a room from the underlying set.
    /// </summary>
    public string? SelectedAreaId
    {
        get => _selectedAreaId;
        set
        {
            if (_selectedAreaId == value) return;

            // An explicit area choice turns off follow-player, so a deliberate
            // user selection is never silently overridden by movement.
            _selectedAreaId = value;
            _areaFilterFollowsPlayer = value is null;
            Raise();
            Raise(nameof(AreaFilterFollowsPlayer));
            Raise(nameof(IsAreaFilterLocked));
            Raise(nameof(FloorsForSelectedArea));
            Raise(nameof(VisibleRooms));
        }
    }

    /// <summary>True when the area filter tracks the character's current area.</summary>
    public bool AreaFilterFollowsPlayer => _areaFilterFollowsPlayer;

    /// <summary>True when the user has pinned an area and it will not auto-switch.</summary>
    public bool IsAreaFilterLocked => !_areaFilterFollowsPlayer;

    /// <summary>
    /// The floor (Z) filter, or null for "every floor". Display-only: it never
    /// hides a room from the underlying set.
    /// </summary>
    public int? SelectedZ
    {
        get => _selectedZ;
        set
        {
            if (_selectedZ == value) return;

            _selectedZ = value;
            _floorFilterFollowsPlayer = value is null;
            Raise();
            Raise(nameof(FloorFilterFollowsPlayer));
            Raise(nameof(IsFloorFilterLocked));
            Raise(nameof(VisibleRooms));
        }
    }

    /// <summary>True when the floor filter tracks the character's current floor.</summary>
    public bool FloorFilterFollowsPlayer => _floorFilterFollowsPlayer;

    /// <summary>True when the user has pinned a floor and it will not auto-switch.</summary>
    public bool IsFloorFilterLocked => !_floorFilterFollowsPlayer;

    /// <summary>
    /// The rooms that pass the current area and floor filters, in a stable
    /// order. This is the exact set the renderer draws.
    /// </summary>
    public IReadOnlyList<MapRoomRecord> VisibleRooms => _rooms.Values
        .Where(MatchesFilter)
        .OrderBy(r => r.X).ThenBy(r => r.Y).ThenBy(r => r.RoomId, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// The discovered rooms passing the current filters, maintained as an
    /// observable collection for direct XAML binding.
    /// </summary>
    public ObservableCollection<MapRoomRecord> VisibleRoomList { get; } = new();

    /// <summary>True when a room passes the current area and floor filters.</summary>
    public bool MatchesFilter(MapRoomRecord room) =>
        (_selectedAreaId is null ||
         string.Equals(room.AreaId, _selectedAreaId, StringComparison.Ordinal)) &&
        (_selectedZ is null || room.Z == _selectedZ);

    /// <summary>
    /// The single floor the map is currently showing.
    /// </summary>
    /// <remarks>
    /// The map is ALWAYS a single-floor plan. When the user has not pinned a
    /// floor the view follows the player's own floor, so rooms on other Z
    /// levels are never silently overlaid onto this plane — that would
    /// misrepresent a multi-storey area as a flat corridor. When even the
    /// player's floor is unknown, the lowest discovered level is used.
    /// </remarks>
    public int? EffectiveFloor => _selectedZ ?? _currentZ;

    /// <summary>
    /// The rooms that actually pass BOTH the area filter and the single-floor
    /// rule — i.e. exactly the set the renderer draws.
    /// </summary>
    public IReadOnlyList<MapRoomRecord> VisibleRoomsOnFloor
    {
        get
        {
            var visible = VisibleRooms;
            if (visible.Count == 0) return visible;

            var floor = EffectiveFloor ?? visible.Min(r => r.Z);
            return visible.Where(r => r.Z == floor).ToArray();
        }
    }

    // ─── Lookups ──────────────────────────────────────────────────────────

    /// <summary>Looks up a discovered room by its stable id.</summary>
    public bool TryGetRoom(string? roomId, out MapRoomRecord room)
    {
        room = null!;
        if (string.IsNullOrEmpty(roomId)) return false;
        return _rooms.TryGetValue(roomId, out room!);
    }

    /// <summary>True when the room exists in the map and passes the active filters.</summary>
    public bool IsVisible(string? roomId) =>
        TryGetRoom(roomId, out var room) && MatchesFilter(room);

    /// <summary>True when the given id is the character's current room.</summary>
    public bool IsCurrentRoom(string? roomId) =>
        !string.IsNullOrEmpty(roomId) &&
        string.Equals(roomId, _currentRoomId, StringComparison.Ordinal);

    /// <summary>The edges leaving a discovered room, in a stable order.</summary>
    public IReadOnlyList<MapEdgeRecord> GetOutgoingEdges(string? roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return Array.Empty<MapEdgeRecord>();
        return _outgoingEdges.TryGetValue(roomId, out var list)
            ? list
            : Array.Empty<MapEdgeRecord>();
    }

    /// <summary>The unexplored exits leaving a discovered room.</summary>
    public IReadOnlyList<MapFrontierRecord> GetFrontier(string? roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return Array.Empty<MapFrontierRecord>();
        return Frontier.TryGetValue(roomId, out var list)
            ? list
            : Array.Empty<MapFrontierRecord>();
    }

    /// <summary>True when the room has at least one Up or Down connection.</summary>
    public bool HasVerticalConnection(string? roomId) =>
        GetOutgoingEdges(roomId).Any(e => MapDirections.IsVertical(e.DirectionKind));

    /// <summary>True when the room has at least one door connection.</summary>
    public bool HasDoorConnection(string? roomId) =>
        GetOutgoingEdges(roomId).Any(e => e.Door);

    /// <summary>True when at least one unexplored exit leaves this room.</summary>
    public bool HasFrontier(string? roomId) => GetFrontier(roomId).Count > 0;

    /// <summary>
    /// Resolves the single Keystone movement direction that leads from
    /// <paramref name="fromRoomId"/> to the already-discovered room
    /// <paramref name="toRoomId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists ONLY to support the beta's safe adjacent-room click: it
    /// reuses the exact direction Keystone already reported for that real edge
    /// and sends it through the ordinary <c>movement.direction.request</c> path.
    /// </para>
    /// <para>
    /// It deliberately returns false unless <paramref name="toRoomId"/> is a
    /// DIRECT neighbour of the player's current room, and false whenever the
    /// choice would be ambiguous (more than one edge in the same direction).
    /// There is no multi-room pathfinding and no route computation of any kind:
    /// Portal never invents a path the server did not report.
    /// </para>
    /// </remarks>
    /// <param name="fromRoomId">The room to move from (the player's current room).</param>
    /// <param name="toRoomId">The already-discovered destination room.</param>
    /// <param name="direction">The authoritative direction Keystone reported.</param>
    /// <returns>True only for an unambiguous, directly adjacent discovered room.</returns>
    public bool TryGetAdjacentDirection(
        string? fromRoomId, string? toRoomId, out MapDirection direction)
    {
        direction = MapDirection.Unknown;

        if (string.IsNullOrEmpty(fromRoomId) ||
            string.IsNullOrEmpty(toRoomId) ||
            string.Equals(fromRoomId, toRoomId, StringComparison.Ordinal))
        {
            return false;
        }

        // Both rooms must already be known. A frontier stub is never a
        // navigable destination: the exit exists, but its far end is unknown.
        if (!TryGetRoom(toRoomId, out _))
            return false;

        var candidates = GetOutgoingEdges(fromRoomId)
            .Where(e => string.Equals(e.To, toRoomId, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length != 1)
            return false;

        direction = candidates[0].DirectionKind;
        return direction != MapDirection.Unknown;
    }

    // ─── Event application ───────────────────────────────────────────────

    /// <summary>
    /// Replaces the entire visible map state with an authoritative
    /// <c>map.snapshot</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is FULL REPLACEMENT semantics, never a merge. A snapshot is the
    /// server's complete truth for this character at this moment, so any room,
    /// edge or frontier stub left over from a previous session is dropped. That
    /// is what makes a reconnect correct without relying on incremental events:
    /// a stale frontier stub from an old session simply cannot survive.
    /// </para>
    /// <para>
    /// Only rooms Keystone actually sent are inserted. A payload is never
    /// "completed" with guessed neighbours, and frontier entries are never
    /// resolved into destination rooms here.
    /// </para>
    /// </remarks>
    public void ApplySnapshot(MapSnapshotPayload snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _rooms.Clear();

        foreach (var room in snapshot.Rooms)
        {
            // A room without a stable id cannot be keyed safely. Skipping it is
            // always better than inventing a key that could collide with a real
            // room, so a malformed entry can never corrupt the map.
            if (room is null || string.IsNullOrEmpty(room.RoomId))
                continue;

            _rooms[room.RoomId] = room;
        }

        // Edges are only honoured when BOTH endpoints are discovered rooms that
        // are present in this snapshot. An edge pointing at an unknown room is
        // silently discarded rather than drawn as a connection to nowhere.
        var edgeList = new List<MapEdgeRecord>(snapshot.Edges.Count);
        foreach (var edge in snapshot.Edges)
        {
            if (edge is null) continue;
            if (string.IsNullOrEmpty(edge.From) || string.IsNullOrEmpty(edge.To)) continue;
            if (!_rooms.ContainsKey(edge.From) || !_rooms.ContainsKey(edge.To)) continue;
            edgeList.Add(edge);
        }
        Edges = edgeList;

        // Frontier entries stay attached to their DISCOVERED source room and
        // carry nothing else. The key is the source room, so an entry whose
        // source is missing from this snapshot is dropped.
        var frontier = new Dictionary<string, List<MapFrontierRecord>>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Frontier)
        {
            if (entry is null || string.IsNullOrEmpty(entry.From)) continue;
            if (!_rooms.ContainsKey(entry.From)) continue;

            if (!frontier.TryGetValue(entry.From, out var list))
            {
                list = new List<MapFrontierRecord>();
                frontier[entry.From] = list;
            }
            list.Add(entry);
        }
        Frontier = frontier;

        Areas = _rooms.Values
            .Select(r => r.AreaId)
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToArray();

        // Keystone's own floors list is preferred because it describes the
        // current area's real occupied levels (Z ranges are deliberately
        // non-contiguous). When absent, fall back to the Z levels actually
        // present among discovered rooms — never to a dense -26..+14 range.
        var reported = snapshot.Floors.Count > 0
            ? snapshot.Floors.Distinct().OrderBy(z => z).ToArray()
            : _rooms.Values.Select(r => r.Z).Distinct().OrderBy(z => z).ToArray();
        Floors = reported;

        CurrentRoomId = snapshot.CurrentRoomId;
        CurrentAreaId = snapshot.CurrentAreaId;
        CurrentZ = snapshot.CurrentZ ?? snapshot.CurrentPosition?.Z;

        DiscoveredCount = snapshot.DiscoveredCount > 0
            ? snapshot.DiscoveredCount
            : _rooms.Count;
        ShownCount = snapshot.ShownCount > 0 ? snapshot.ShownCount : _rooms.Count;

        HasSnapshot = true;

        ApplyFollowFilters();
        RebuildIndexes();
        RefreshVisible();
    }

    /// <summary>
    /// Adds a single newly discovered room from a <c>map.room.discovered</c>
    /// event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is purely ADDITIVE and deliberately minimal. The payload contains a
    /// new room and a counter, so that is all that is taken from it.
    /// </para>
    /// <para>
    /// In particular the new room's EDGES and FRONTIER are NOT reconstructed
    /// here. Doing so would mean guessing topology the server did not send, so
    /// instead the room appears immediately and its connections light up on the
    /// next authoritative <c>map.snapshot</c>. The frontier stub the player just
    /// walked through is cleared because that is a direct consequence of the
    /// room now being discovered — see <see cref="ResolveFrontierToward"/>.
    /// </para>
    /// </remarks>
    public void ApplyRoomDiscovered(MapRoomDiscoveredPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var room = payload.Room;
        if (room is null || string.IsNullOrEmpty(room.RoomId))
            return;

        var previousRoomId = _currentRoomId;

        // Keyed by world_room_id, never by name or coordinates.
        _rooms[room.RoomId] = room;

        if (payload.DiscoveredCount > 0)
            DiscoveredCount = payload.DiscoveredCount;
        else
            DiscoveredCount = _rooms.Count;

        if (!string.IsNullOrEmpty(room.AreaId) &&
            !Areas.Contains(room.AreaId, StringComparer.Ordinal))
        {
            Areas = Areas.Append(room.AreaId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(a => a, StringComparer.Ordinal)
                .ToArray();
        }

        if (!Floors.Contains(room.Z))
        {
            Floors = Floors.Append(room.Z).Distinct().OrderBy(z => z).ToArray();
        }

        // The exit the player just used is no longer unexplored. This only
        // touches frontier stubs pointing at a room that is NOW discovered, so
        // it can never resolve a stub into a hidden destination.
        if (!string.IsNullOrEmpty(previousRoomId))
            ResolveFrontierToward(previousRoomId, room);

        ApplyFollowFilters();
        RebuildIndexes();
        RefreshVisible();
    }

    /// <summary>
    /// Clears the frontier stub(s) leaving <paramref name="sourceRoomId"/> whose
    /// direction is consistent with the room that has just been discovered.
    /// </summary>
    /// <remarks>
    /// Both rooms are already DISCOVERED at this point and both have
    /// authoritative coordinates from Keystone, so comparing them reveals
    /// nothing hidden. The stub is simply removed because the passage it marked
    /// is no longer unexplored. No destination room is ever created here.
    /// </remarks>
    private void ResolveFrontierToward(string sourceRoomId, MapRoomRecord discovered)
    {
        if (!Frontier.TryGetValue(sourceRoomId, out var stubs) || stubs.Count == 0)
            return;
        if (!TryGetRoom(sourceRoomId, out var source))
            return;

        var remaining = new List<MapFrontierRecord>(stubs.Count);
        foreach (var stub in stubs)
        {
            var direction = stub.DirectionKind;
            var consistent = MapDirections.IsDisplacementConsistentWith(
                direction,
                discovered.X - source.X,
                discovered.Y - source.Y,
                discovered.Z - source.Z);

            if (!consistent)
                remaining.Add(stub);
        }

        if (remaining.Count == stubs.Count)
            return; // nothing changed

        var updated = new Dictionary<string, List<MapFrontierRecord>>(
            Frontier, StringComparer.Ordinal);
        updated[sourceRoomId] = remaining;
        Frontier = updated;

        Raise(nameof(Frontier));
    }

    /// <summary>
    /// Moves the player's marker from a <c>map.position</c> event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the CURRENT ROOM changes here. This never adds a room to the map:
    /// a position event for a room that is not yet in the discovered set is
    /// recorded as the current location but contributes no geometry, because
    /// Keystone reports coordinates only for discovered rooms anyway.
    /// </para>
    /// <para>
    /// When <see cref="IsDiscovered"/> is false the position and floor fields
    /// are null in the payload and are applied as "unknown" — never defaulted
    /// to 0, which would put the marker at a fabricated coordinate.
    /// </para>
    /// </remarks>
    public void ApplyPosition(MapPositionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CurrentRoomId = payload.CurrentRoomId;
        CurrentAreaId = payload.CurrentAreaId ?? CurrentAreaId;

        if (payload.IsDiscovered && payload.CurrentPosition is { } position)
        {
            CurrentZ = payload.CurrentZ ?? position.Z;

            // Defensive: if the room really is discovered but is somehow absent
            // from the set, adopt the coordinates Keystone just authorised rather
            // than leaving a known room off the map.
            if (!string.IsNullOrEmpty(payload.CurrentRoomId) &&
                !_rooms.ContainsKey(payload.CurrentRoomId))
            {
                _rooms[payload.CurrentRoomId] = new MapRoomRecord
                {
                    RoomId = payload.CurrentRoomId,
                    AreaId = payload.CurrentAreaId ?? string.Empty,
                    // The name was not part of the position payload, so it stays
                    // empty rather than being invented from the room id.
                    Name = string.Empty,
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z
                };

                Areas = _rooms.Values
                    .Select(r => r.AreaId)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(a => a, StringComparer.Ordinal)
                    .ToArray();

                if (!Floors.Contains(position.Z))
                    Floors = Floors.Append(position.Z).Distinct().OrderBy(z => z).ToArray();

                RebuildIndexes();
            }
        }
        else
        {
            // Not discovered (or no coordinates): the floor is genuinely unknown.
            CurrentZ = null;
        }

        ApplyFollowFilters();
        RefreshVisible();
    }

    /// <summary>
    /// Reconciles discovery counters from a <c>map.state</c> event.
    /// </summary>
    /// <remarks>
    /// Keystone's <c>map.state</c> carries COUNTS and no geography, so this
    /// updates the counters only. It never adds, removes or moves a room.
    /// </remarks>
    public void ApplyState(MapStatePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.DiscoveredCount >= 0)
            DiscoveredCount = payload.DiscoveredCount;

        if (payload.TotalGeneratedRooms > 0)
            TotalGeneratedRooms = payload.TotalGeneratedRooms;

        if (!string.IsNullOrEmpty(payload.CurrentAreaId))
            CurrentAreaId = payload.CurrentAreaId;

        ApplyFollowFilters();
        RefreshVisible();
    }

    /// <summary>
    /// Clears every trace of the map, used on logout, disconnect and reconnect.
    /// </summary>
    /// <remarks>
    /// Without this a reconnect could briefly show rooms from the previous
    /// session. Keystone re-sends a full <c>map.snapshot</c> on reconnect, so
    /// starting from empty guarantees the visible map is always exactly what
    /// the newest snapshot says and nothing stale survives.
    /// </remarks>
    public void Reset()
    {
        _rooms.Clear();
        Edges = Array.Empty<MapEdgeRecord>();
        Frontier = new Dictionary<string, List<MapFrontierRecord>>(StringComparer.Ordinal);
        Areas = Array.Empty<string>();
        Floors = Array.Empty<int>();
        _currentRoomId = null;
        _currentAreaId = null;
        _currentZ = null;
        _discoveredCount = 0;
        _shownCount = 0;
        _totalGeneratedRooms = 0;
        HasSnapshot = false;

        _selectedAreaId = null;
        _areaFilterFollowsPlayer = true;
        _selectedZ = null;
        _floorFilterFollowsPlayer = true;

        RebuildIndexes();
        RefreshVisible();

        Raise(nameof(CurrentRoomId));
        Raise(nameof(CurrentAreaId));
        Raise(nameof(CurrentZ));
        Raise(nameof(DiscoveredCount));
        Raise(nameof(ShownCount));
        Raise(nameof(TotalGeneratedRooms));
        Raise(nameof(HasSnapshot));
        Raise(nameof(IsEmpty));
        Raise(nameof(SelectedAreaId));
        Raise(nameof(SelectedZ));
        Raise(nameof(Edges));
        Raise(nameof(Areas));
        Raise(nameof(Floors));
    }

    // ─── Internals ───────────────────────────────────────────────────────

    /// <summary>
    /// Re-applies the "follow the player" filters after the current room moved.
    /// A filter the user pinned by hand is left completely alone.
    /// </summary>
    private void ApplyFollowFilters()
    {
        if (_areaFilterFollowsPlayer && _currentAreaId is not null &&
            !string.Equals(_selectedAreaId, _currentAreaId, StringComparison.Ordinal))
        {
            _selectedAreaId = _currentAreaId;
            Raise(nameof(SelectedAreaId));
            Raise(nameof(IsAreaFilterLocked));
            Raise(nameof(FloorsForSelectedArea));
        }

        if (_floorFilterFollowsPlayer && _currentZ.HasValue &&
            _selectedZ != _currentZ)
        {
            _selectedZ = _currentZ;
            Raise(nameof(SelectedZ));
            Raise(nameof(IsFloorFilterLocked));
        }
    }

    /// <summary>
    /// Rebuilds the per-room outgoing-edge index. Only edges whose source is a
    /// known discovered room are indexed, so a lookup can never surface an edge
    /// into an undiscovered room.
    /// </summary>
    private void RebuildIndexes()
    {
        _outgoingEdges.Clear();

        foreach (var edge in Edges)
        {
            if (!_rooms.ContainsKey(edge.From) || !_rooms.ContainsKey(edge.To)) continue;

            if (!_outgoingEdges.TryGetValue(edge.From, out var list))
            {
                list = new List<MapEdgeRecord>(4);
                _outgoingEdges[edge.From] = list;
            }
            list.Add(edge);
        }

        foreach (var list in _outgoingEdges.Values)
        {
            list.Sort(static (a, b) =>
            {
                var byDirection = string.CompareOrdinal(a.Direction, b.Direction);
                return byDirection != 0
                    ? byDirection
                    : string.CompareOrdinal(a.To, b.To);
            });
        }
    }

    /// <summary>
    /// Re-publishes the filtered room set to bound UI collections and raises
    /// change notifications for every derived projection.
    /// </summary>
    private void RefreshVisible()
    {
        var visible = VisibleRooms;

        // Full replace rather than incremental diffing: the visible set is small
        // (a filtered slice, not the whole realm) and a replace keeps the bound
        // list exactly in step with the filters with no ordering surprises.
        VisibleRoomList.Clear();
        foreach (var room in visible)
            VisibleRoomList.Add(room);

        Raise(nameof(VisibleRoomList));
        Raise(nameof(VisibleRooms));
        Raise(nameof(FloorsForSelectedArea));
        Raise(nameof(IsEmpty));
        Raise(nameof(Edges));
        Raise(nameof(Rooms));
        Raise(nameof(Frontier));
        Raise(nameof(Areas));
        Raise(nameof(Floors));
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}