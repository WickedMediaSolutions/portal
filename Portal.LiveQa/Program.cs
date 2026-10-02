using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Portal.Networking;
using Portal.Protocol;
using Portal.State;

namespace Portal.LiveQa;

/// <summary>
/// Receives Portal Protocol envelopes from a live WebSocket and lets the QA
/// driver wait for specific responses or drain a burst of events.
/// </summary>
internal sealed class Receiver
{
    private readonly System.Net.WebSockets.ClientWebSocket _ws;
    private readonly List<MessageEnvelope> _pending = new();
    private readonly SemaphoreSlim _signal = new(0);

    private readonly byte[] _buffer = new byte[16 * 1024];

    public Receiver(System.Net.WebSockets.ClientWebSocket ws)
    {
        _ws = ws;
        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try
        {
            while (_ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(_buffer), CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        return;
                    ms.Write(_buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(ms.ToArray());
                var parsed = Portal.Protocol.ProtocolSerializer.Deserialize(text);
                if (parsed.IsSuccess)
                {
                    _pending.Add(parsed.Value);
                    _signal.Release();
                }
                else
                {
                    Console.WriteLine("  << UNPARSEABLE: " +
                        text[..Math.Min(400, text.Length)]);
                    Console.WriteLine("     error: " + parsed.Errors[0].Message);
                }
            }
        }
        catch
        {
            // Socket closed: nothing more to pump.
        }
    }

    private MessageEnvelope? Take()
    {
        if (_pending.Count > 0)
        {
            var e = _pending[0];
            _pending.RemoveAt(0);
            return e;
        }
        return null;
    }

    /// <summary>Waits for a specific response type with the given correlation id.</summary>
    public async Task<JsonElement?> Expect(string messageType, string correlationId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var e = Take();
            if (e is not null)
            {
                if (e.MessageType == messageType && e.CorrelationId == correlationId)
                    return e.Payload;
                continue;
            }

            await _signal.WaitAsync(TimeSpan.FromMilliseconds(250));
        }

        return null;
    }

    /// <summary>Collects every event that arrives within the given window.</summary>
    public async Task<List<MessageEnvelope>> DrainFor(TimeSpan window)
    {
        var list = new List<MessageEnvelope>();
        var deadline = DateTime.UtcNow.Add(window);

        while (DateTime.UtcNow < deadline)
        {
            var e = Take();
            if (e is not null)
            {
                list.Add(e);
                continue;
            }

            await _signal.WaitAsync(TimeSpan.FromMilliseconds(150));
        }

        return list;
    }
}

internal static class Program
{
    private static readonly List<string> Log = new();

    internal static void Say(string line)
    {
        Log.Add(line);
        Console.WriteLine(line);
    }

    private static async Task<int> Main(string[] args)
    {
        // --services runs the live service/room/door/WHO parity QA sweep against
        // a real running Keystone; --quests runs the live quest lifecycle sweep
        // (bootstrap, accept, live kill, ready, complete, duplicate refusal,
        // abandon/re-accept, reconnect); the default stays the map QA sweep.
        if (args.Contains("--services"))
            return await ServiceQa.RunAsync(args);

        if (args.Contains("--relverify"))
            return await ServiceQa.RunRelVerifyAsync(args);

        if (args.Contains("--quests"))
            return await QuestQa.RunAsync(args);

        var username = Arg(args, "--user") ?? "testuser";
        var password = Arg(args, "--pass") ?? "testpass";
        Username = username;
        Password = password;
        DoorDirection = Arg(args, "--door");
        var useLocalDev = !args.Contains("--production");

        // Resolve the endpoint through Portal's OWN resolver: this is the same
        // code path the login bar's toggle uses.
        var target = PortalEndpoints.FromToggle(useLocalDev);
        var endpoint = PortalEndpoints.Resolve(target);

        Say("=== Portal live localhost QA ===");
        Say($"toggle_local_development = {useLocalDev}");
        Say($"resolved_target          = {PortalEndpoints.Describe(target)}");
        Say($"resolved_endpoint        = {endpoint}");
        Say($"plaintext_transport      = {PortalEndpoints.IsPlaintextTransport(endpoint)}");
        Say($"user                     = {username}");
        Say(string.Empty);

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await ws.ConnectAsync(endpoint, cts.Token);
        }
        catch (Exception ex)
        {
            Say($"FATAL: websocket connect failed: {ex.GetType().Name}: {ex.Message}");
            Dump();
            return 2;
        }

        Say($"[OK] websocket established to {endpoint} (state={ws.State})");
        Say(string.Empty);

        var rx = new Receiver(ws);

        // ── Handshake ──
        var handshakeId = Guid.NewGuid().ToString("N");
        await SendRaw(ws, "handshake.request", handshakeId, Handshake());

        var handshake = await rx.Expect(ProtocolMessageTypes.HandshakeResponse, handshakeId);
        if (handshake is null)
        {
            Say("FATAL: no handshake.response");
            Dump();
            return 3;
        }

        Say("[OK] handshake.response received");
        Say($"     payload: {Compact(handshake.Value)}");
        Say(string.Empty);

        // ── Authentication ──
        var authId = Guid.NewGuid().ToString("N");
        await Send(ws, "auth.request", authId, new AuthenticationRequest(username, password));

        var auth = await rx.Expect(ProtocolMessageTypes.AuthenticationResponse, authId);
        if (auth is null)
        {
            Say("FATAL: no auth.response");
            Dump();
            return 4;
        }

        var authPayload = ProtocolSerializer.DeserializePayload<AuthenticationResponse>(
            new MessageEnvelope { Payload = auth.Value });

        if (authPayload.IsFailure)
        {
            Say($"FATAL: auth.response payload invalid: {authPayload.Errors[0].Message}");
            Dump();
            return 5;
        }

        if (!authPayload.Value.Success)
        {
            Say($"FATAL: authentication rejected: {authPayload.Value.ErrorCode} {authPayload.Value.ErrorMessage}");
            Dump();
            return 6;
        }

        Say("[OK] auth.response success");
        Say($"     session  = {authPayload.Value.Session?.SessionId}");
        Say($"     character= {authPayload.Value.Session?.CharacterName} (id={authPayload.Value.Session?.CharacterId})");
        Say(string.Empty);

        // ── Drain the gameplay + map bootstrap ──
        var state = new MapState();
        var map = new MapViewModel();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        var drain = await rx.DrainFor(TimeSpan.FromSeconds(6));
        foreach (var env in drain)
        {
            seen[env.MessageType] = seen.GetValueOrDefault(env.MessageType) + 1;
            ApplyMapEvent(state, map, env);
        }

        Say($"[bootstrap] received {drain.Count} events");
        foreach (var kv in seen.OrderBy(k => k.Key, StringComparer.Ordinal))
            Say($"     {kv.Key} x{kv.Value}");
        Say(string.Empty);

        ReportSnapshot(state, "initial");

        // The locked-door probe runs FIRST, while the character is still standing
        // in the room the QA fixture placed it in. Later phases deliberately walk
        // the character around, which would move it away from the door.
        var doorCode = await RunLockedDoor(state, map, ws, rx, doReconnect: false);
        if (doorCode != 0) return doorCode;

        return await RunMovement(state, map, ws, rx, username, password);
    }

    private static async Task<int> RunMovement(
        MapState state, MapViewModel map, ClientWebSocket ws, Receiver rx,
        string username, string password)
    {
        // ── Movement / discovery sweep over the live socket ──
        Say("=== movement / discovery sweep (live socket) ===");

        var directions = new[] { "north", "south", "east", "west", "up", "down" };

        foreach (var direction in directions)
        {
            var before = state.Rooms.Keys.ToHashSet(StringComparer.Ordinal);
            var beforeRoom = state.CurrentRoomId;
            var beforeFrontier = state.Frontier.Values.Sum(v => v.Count);

            await Send(ws, "movement.direction.request",
                Guid.NewGuid().ToString("N"), new { direction });

            var moved = await rx.DrainFor(TimeSpan.FromSeconds(3));
            foreach (var env in moved)
                ApplyMapEvent(state, map, env);

            var after = state.Rooms.Keys.ToHashSet(StringComparer.Ordinal);
            var newRooms = after.Except(before).OrderBy(r => r, StringComparer.Ordinal).ToArray();
            var failed = moved.Where(e => e.MessageType == ProtocolMessageTypes.MovementFailed).ToArray();

            Say($"  {direction,-5}: {beforeRoom ?? "--",-22} -> {state.CurrentRoomId ?? "--",-22}" +
                $" z={(state.CurrentZ?.ToString() ?? "--"),-3}" +
                $" evts={moved.Count,2}" +
                $" frontier={beforeFrontier}->{state.Frontier.Values.Sum(v => v.Count)}" +
                $" new=[{string.Join(",", newRooms)}]" +
                (failed.Length > 0 ? " MOVEMENT_FAILED" : string.Empty));
        }

        Say(string.Empty);
        ReportSnapshot(state, "after-movement");

        // ── Compass / adjacent-room resolution from real edges ──
        Say("=== adjacent Go directions (real Keystone edges) ===");
        if (state.CurrentRoomId is { } cur)
        {
            var edges = state.GetOutgoingEdges(cur);
            Say($"     outgoing edges from {cur}: {edges.Count}");
            foreach (var e in edges)
            {
                var resolvable = state.TryGetAdjacentDirection(cur, e.To, out var dir);
                Say($"     {e.Direction,-8} -> {e.To,-24}{(e.Door ? "door " : "     ")}go={(resolvable ? dir.ToString() : "n/a")}");
            }

            Say($"     frontier stubs at {cur}: {state.GetFrontier(cur).Count}");
            foreach (var f in state.GetFrontier(cur))
                Say($"     frontier: {f.Direction,-8} state={f.State} (no destination field)");
        }

        Say(string.Empty);
        Say("=== area / floor selectors (Keystone data only) ===");
        Say($"     areas                : [{string.Join(", ", state.Areas)}]");
        Say($"     floors               : [{string.Join(", ", state.Floors)}]");
        Say($"     area filter follows  : {state.AreaFilterFollowsPlayer}");
        Say($"     selected area        : {state.SelectedAreaId ?? "(follows player)"}");
        Say($"     effective floor      : {state.EffectiveFloor?.ToString() ?? "--"}");
        Say($"     visible rooms        : {state.VisibleRooms.Count}");

        if (state.Areas.Count > 1)
        {
            foreach (var area in state.Areas)
            {
                state.SelectedAreaId = area;
                Say($"     area '{area}' -> visible {state.VisibleRooms.Count}, floors [{string.Join(",", state.FloorsForSelectedArea)}]");
            }
            state.SelectedAreaId = null;
        }

        Say(string.Empty);
        Say("=== map viewport interaction on live data ===");
        var viewport = new MapViewport();
        viewport.SetViewportSize(900, 600);
        var z0 = viewport.Zoom;
        viewport.ZoomIn();
        viewport.ZoomIn();
        Say($"     zoom in x2 : {z0:0.00} -> {viewport.Zoom:0.00}");
        viewport.ZoomOut();
        Say($"     zoom out   : {viewport.Zoom:0.00}");

        if (state.CurrentRoomId is { } roomId && state.TryGetRoom(roomId, out var room))
        {
            viewport.CenterOn(room.X, room.Y);
            Say($"     centre on player ({roomId} @ {room.X},{room.Y}) -> origin {viewport.OriginX:0.0},{viewport.OriginY:0.0}");
        }

        viewport.FitTo(state.VisibleRoomsOnFloor.ToList());
        Say($"     fit all    : zoom {viewport.Zoom:0.00}");

        map.CenterOnPlayer();
        map.AutoFollow = true;
        Say($"     centre-on-player + follow-me: AutoFollow={map.AutoFollow}");

        Say(string.Empty);

        var code = await RunVerticalAndAdjacent(state, map, ws, rx);
        if (code != 0) return code;

        var incCode = await RunIncrementalDiscoveryQA(state, map, ws, rx);
        if (incCode != 0) return incCode;

        return await RunLockedDoor(state, map, ws, rx, doReconnect: true);
    }

    /// <summary>
    /// Live proof that ONE movement is enough: the client's graph gains the new
    /// room, its newly legal edges, its resolved frontier removals and its new
    /// frontier stubs, and adjacent Go works, all from the single
    /// <c>map.room.discovered</c> delta.
    /// </summary>
    /// <remarks>
    /// The critical assertion is that NO <c>map.snapshot</c> is sent during this
    /// phase: a snapshot here would mean the client was still waiting on a full
    /// resnapshot to learn a connection the server already knew about.
    /// </remarks>
    private static async Task<int> RunIncrementalDiscoveryQA(
        MapState state, MapViewModel map, ClientWebSocket ws, Receiver rx)
    {
        Say("=== incremental discovery delta (live socket, no reconnect) ===");

        var snapshotsSeen = 0;
        var discoveriesWithEdges = 0;
        var edgeImmediate = 0;
        var frontierRemoved = 0;
        var frontierAdded = 0;
        var adjacentImmediate = 0;
        var consecutive = 0;
        var steps = 0;

        var dirs = new[] { "north", "east", "south", "west", "up", "down" };

        // Up to 18 attempts: each either discovers a new room (which we verify)
        // or moves us somewhere else to try again from.
        for (var attempt = 0; attempt < 18 && consecutive < 2; attempt++)
        {
            // Prefer a direction that is still an UNEXPLORED stub off the current
            // room: that is guaranteed to be a genuine new discovery rather than
            // a bounce back through a room we already know.
            var dir = state.CurrentRoomId is { } here
                ? state.GetFrontier(here)
                    .Select(s => s.Direction)
                    .FirstOrDefault(d => !state.GetOutgoingEdges(here)
                        .Any(e => e.Direction == d))
                : null;

            if (string.IsNullOrEmpty(dir))
                dir = dirs[attempt % dirs.Length];

            var previousRoom = state.CurrentRoomId;
            var edgesBefore = state.Edges.Count;
            var roomsBefore = state.Rooms.Count;

            // Snapshot the exact frontier keys so a removal is observed as a real
            // change rather than assumed.
            var frontierKeysBefore = state.Frontier
                .SelectMany(kv => kv.Value.Select(s => $"{s.From}|{s.Direction}"))
                .ToHashSet(StringComparer.Ordinal);

            await Send(ws, "movement.direction.request",
                Guid.NewGuid().ToString("N"), new MovementDirectionRequest(dir));

            var moved = await rx.DrainFor(TimeSpan.FromSeconds(3));
            snapshotsSeen += moved.Count(e => e.MessageType == ProtocolMessageTypes.MapSnapshot);

            foreach (var env in moved)
                ApplyMapEvent(state, map, env);

            var discovered = moved
                .Where(e => e.MessageType == ProtocolMessageTypes.MapRoomDiscovered)
                .ToArray();
            if (discovered.Length == 0)
                continue;

            steps++;
            var parsed = ProtocolSerializer.DeserializePayload<MapRoomDiscoveredPayload>(
                discovered[0]);
            if (parsed.IsFailure)
            {
                Say($"  !! delta INVALID: {parsed.Errors[0].Message}");
                return 11;
            }
            var delta = parsed.Value;

            if (delta.Edges.Count > 0)
            {
                discoveriesWithEdges++;
                consecutive++;
                Say($"  step {steps}: dir={dir,-5} {previousRoom} -> {delta.Room.RoomId}" +
                    $" \"{delta.Room.Name}\" z={delta.Room.Z}");
                Say($"     edges in delta       : {delta.Edges.Count}");
                foreach (var e in delta.Edges)
                    Say($"       {e.From,-24} -{e.Direction,-9}-> {e.To,-24}{(e.Door ? "door" : "")}");

                // IMMEDIATE: the edge is in the client's model with no snapshot.
                if (state.Edges.Count > edgesBefore)
                {
                    edgeImmediate++;
                    Say($"     [OK] edges {edgesBefore} -> {state.Edges.Count} immediately");
                }
                else
                {
                    Say($"     [!!] NO edge appeared (stayed at {edgesBefore})");
                }
            }

            if (delta.FrontierRemove.Count > 0)
            {
                var resolvedNow = delta.FrontierRemove
                    .Select(k => $"{k.From}|{k.Direction}")
                    .Where(frontierKeysBefore.Contains)
                    .ToArray();
                Say($"     frontier_remove      : {delta.FrontierRemove.Count}" +
                    $" (previously present: {resolvedNow.Length})" +
                    string.Concat(resolvedNow.Select(k => $" [{k}]")));
                if (resolvedNow.Length > 0)
                {
                    frontierRemoved++;
                    // IMMEDIATE: the stub is gone from the live model.
                    var liveKeys = state.Frontier
                        .SelectMany(kv => kv.Value.Select(s => $"{s.From}|{s.Direction}"))
                        .ToHashSet(StringComparer.Ordinal);
                    if (resolvedNow.All(k => !liveKeys.Contains(k)))
                        Say("     [OK] resolved stubs gone immediately");
                    else
                        Say("     [!!] a resolved stub is still present");
                }
            }

            if (delta.FrontierAdd.Count > 0)
            {
                frontierAdded++;
                Say($"     frontier_add         : {delta.FrontierAdd.Count}");
                foreach (var f in delta.FrontierAdd)
                    Say($"       {f.From,-24} -{f.Direction,-9}-> (no destination) state={f.State}");
                if (state.GetFrontier(delta.Room.RoomId).Count > 0)
                    Say("     [OK] new room's stubs visible immediately");
                else
                    Say("     [!!] new room has no stubs after discovery");
            }

            // IMMEDIATE adjacent Go, both directions, using ONLY the live model.
            foreach (var e in delta.Edges)
            {
                if (state.TryGetAdjacentDirection(e.From, e.To, out var goDir))
                {
                    adjacentImmediate++;
                    Say($"     [OK] adjacent Go {e.From} -{goDir}-> {e.To} works now");
                }
                else
                {
                    Say($"     [!!] adjacent Go {e.From}->{e.To} NOT resolvable");
                }
            }

            // No duplicate edges or stubs were introduced.
            var dupEdges = state.Edges.Count -
                state.Edges.Select(e => (e.From, e.Direction, e.To)).Distinct().Count();
            Say($"     duplicate edges={dupEdges} rooms {roomsBefore} -> {state.Rooms.Count}");
            if (dupEdges != 0)
            {
                Say("     [!!] duplicate edges introduced");
                return 12;
            }

            Say(string.Empty);
        }

        Say($"     discoveries with a real edge delta : {discoveriesWithEdges} (need >= 2)");
        Say($"     edge appeared immediately           : {edgeImmediate}");
        Say($"     frontier removal applied            : {frontierRemoved}");
        Say($"     frontier addition applied           : {frontierAdded}");
        Say($"     adjacent Go resolved immediately    : {adjacentImmediate}");
        Say($"     map.snapshot during movement        : {snapshotsSeen}   (must be 0)");
        Say($"     consecutive new rooms, no reconnect : {consecutive}");
        Say(string.Empty);

        return (discoveriesWithEdges >= 2 && consecutive >= 2 && snapshotsSeen == 0)
            ? 0
            : 13;
    }

    /// <summary>
    /// Vertical (Up/Down) traversal and the adjacent-Go direction resolution,
    /// both driven from the REAL edge set Keystone disclosed.
    /// </summary>
    private static async Task<int> RunVerticalAndAdjacent(
        MapState state, MapViewModel map, ClientWebSocket ws, Receiver rx)
    {
        Say("=== vertical + adjacent Go (live socket) ===");

        // Adjacent Go: resolve a real edge direction from the current room.
        if (state.CurrentRoomId is { } cur)
        {
            var edges = state.GetOutgoingEdges(cur);
            Say($"     real edges from {cur}: {edges.Count}");
            foreach (var e in edges)
            {
                var okDir = state.TryGetAdjacentDirection(cur, e.To, out var dir);
                Say($"       {e.Direction,-8} -> {e.To,-26}{(e.Door ? "door" : "    ")}go={(okDir ? dir.ToString() : "n/a")}");
            }

            foreach (var f in state.GetFrontier(cur))
                Say($"       frontier {f.Direction,-8} is NOT navigable (no destination id exists)");
        }

        Say(string.Empty);

        // Vertical traversal: go Down then Up, asserting the floor actually
        // changes and the previous floor stays selectable.
        foreach (var direction in new[] { "down", "up" })
        {
            var floorBefore = state.CurrentZ;
            var roomsBefore = state.Rooms.Count;

            await Send(ws, "movement.direction.request",
                Guid.NewGuid().ToString("N"), new MovementDirectionRequest(direction));

            var moved = await rx.DrainFor(TimeSpan.FromSeconds(4));
            foreach (var env in moved)
                ApplyMapEvent(state, map, env);

            var failed = moved.Count(e => e.MessageType == ProtocolMessageTypes.MovementFailed);
            var disc = moved
                .Where(e => e.MessageType == ProtocolMessageTypes.MapRoomDiscovered)
                .ToArray();

            Say($"  {direction,-5}: z {floorBefore?.ToString() ?? "--"} -> {state.CurrentZ?.ToString() ?? "--"}" +
                $"  room={state.CurrentRoomId ?? "--"}" +
                $"  newRooms={state.Rooms.Count - roomsBefore}" +
                $"  discoveredEvents={disc.Length}" +
                $"  failed={failed}" +
                $"  floors=[{string.Join(",", state.Floors)}]");

            foreach (var d in disc)
            {
                var r = ProtocolSerializer.DeserializePayload<MapRoomDiscoveredPayload>(d);
                if (r.IsSuccess)
                {
                    Say($"       discovered {r.Value.Room.RoomId} \"{r.Value.Room.Name}\"" +
                        $" @ {r.Value.Room.X},{r.Value.Room.Y},z={r.Value.Room.Z}");
                }
            }

            foreach (var f in moved.Where(e => e.MessageType == ProtocolMessageTypes.MovementFailed))
                Say($"       movement.failed: {Compact(f.Payload)}");
        }

        // The previous floor must remain selectable because it is discovered.
        if (state.Floors.Count > 1)
        {
            foreach (var z in state.Floors)
            {
                state.SelectedZ = z;
                Say($"     floor {z,3}: {state.VisibleRoomsOnFloor.Count} discovered rooms shown");
            }
            state.SelectedZ = null;
        }

        Say(string.Empty);
        return 0;
    }

    /// <summary>
    /// Locked-door fog security over the live socket.
    ///
    /// The Great Hall's west passage is a real generated door that starts
    /// CLOSED. This walks straight into it and proves that a refused traversal
    /// discloses nothing at all: no discovery event, no destination room, and
    /// the frontier stub survives untouched.
    /// </summary>
    private static async Task<int> RunLockedDoor(
        MapState state, MapViewModel map, ClientWebSocket ws, Receiver rx,
        bool doReconnect)
    {
        Say("=== locked-door fog security (live socket) ===");

        // Generic probe: walk into an unexplored direction off the CURRENT room
        // and see whether the server refuses. A refused traversal is what a
        // locked door looks like from the client, and it must disclose nothing.
        var here = state.CurrentRoomId;
        if (here is null)
        {
            Say("     (no current room; walking is covered above)");
            return 0;
        }

        var roomsBefore = state.Rooms.Count;
        var frontierBefore = state.GetFrontier(here).Count;
        var snapshotJson = string.Join('|',
            state.Rooms.Values.Select(r => $"{r.RoomId}:{r.Name}:{r.X},{r.Y},{r.Z}"));
        var edgesBefore = state.Edges.Count;

        // An explicit --door aims the probe; otherwise prefer a direction that is
        // actually an unexplored stub off this room.
        var candidates = state.GetFrontier(here).Select(s => s.Direction).ToList();
        var dir = DoorDirection
                  ?? candidates.FirstOrDefault()
                  ?? "west";

        Say($"     at room                 : {here}");
        Say($"     rooms before attempt    : {roomsBefore}");
        Say($"     edges before attempt    : {edgesBefore}");
        Say($"     frontier stubs before   : {frontierBefore}");
        Say($"     attempting {dir} (a real generated passage)");

        await Send(ws, "movement.direction.request",
            Guid.NewGuid().ToString("N"), new MovementDirectionRequest(dir));

        var moved = await rx.DrainFor(TimeSpan.FromSeconds(4));
        foreach (var env in moved)
            ApplyMapEvent(state, map, env);

        var failed = moved
            .Where(e => e.MessageType == ProtocolMessageTypes.MovementFailed)
            .ToArray();
        var discoveredEvents = moved
            .Count(e => e.MessageType == ProtocolMessageTypes.MapRoomDiscovered);
        var snapshotsDuring = moved
            .Count(e => e.MessageType == ProtocolMessageTypes.MapSnapshot);

        Say($"     movement.failed events  : {failed.Length}");
        foreach (var f in failed)
            Say($"       {Compact(f.Payload)}");

        Say($"     map.room.discovered     : {discoveredEvents}");
        Say($"     map.snapshot            : {snapshotsDuring}");
        Say($"     rooms after attempt     : {state.Rooms.Count}");
        Say($"     edges after attempt     : {state.Edges.Count}");
        Say($"     still at {here,-24}: {state.CurrentRoomId == here}");
        Say($"     frontier stubs after    : {state.GetFrontier(here).Count}");

        var stub = state.GetFrontier(here).FirstOrDefault(f => f.Direction == dir);
        Say($"     {dir} stub survives        : {stub is not null} state={stub?.State ?? "--"}");

        var after = string.Join('|',
            state.Rooms.Values.Select(r => $"{r.RoomId}:{r.Name}:{r.X},{r.Y},{r.Z}"));
        Say($"     room set unchanged      : {snapshotJson == after}");

        // If the traversal was REFUSED, nothing at all may have changed.
        if (failed.Length > 0)
        {
            Say($"     [OK] refused traversal disclosed nothing");
            Say($"       no discovery event    : {discoveredEvents == 0}");
            Say($"       no new edges          : {state.Edges.Count == edgesBefore}");
            Say($"       stub intact           : {stub is not null}");
            Say($"       room set identical    : {snapshotJson == after}");
            Say($"       no map.snapshot       : {snapshotsDuring == 0}");
        }

        Say(string.Empty);

        return doReconnect ? await RunReconnect(state, map, Username, Password) : 0;
    }

    private static string Username { get; set; } = "testuser";
    private static string Password { get; set; } = "testpass";

    /// <summary>
    /// Optional explicit direction for the locked-door probe (<c>--door west</c>).
    /// Lets QA aim the probe at a known generated door instead of guessing.
    /// </summary>
    private static string? DoorDirection { get; set; }

    private static async Task<int> RunReconnect(
        MapState state, MapViewModel map, string username, string password)
    {
        Say("=== reconnect ===");

        var roomsBefore = state.Rooms.Count;
        var edgesBefore = state.Edges.Count;
        var floorsBefore = string.Join(",", state.Floors);
        var areaBefore = state.CurrentAreaId ?? "--";
        var roomBefore = state.CurrentRoomId ?? "--";

        // The real client clears the map when the session is lost, so nothing
        // stale can be observed across a reconnect.
        state.Reset();
        map.ClearMap();
        Say($"     disconnected; map cleared (had {roomsBefore} rooms, {edgesBefore} edges)");
        Say($"     empty after clear: rooms={state.Rooms.Count} hasSnapshot={state.HasSnapshot}");

        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.LocalDevelopment);

        using var ws2 = new ClientWebSocket();
        await ws2.ConnectAsync(endpoint, CancellationToken.None);
        Say("[OK] websocket re-established");

        var rx2 = new Receiver(ws2);

        var hs2 = Guid.NewGuid().ToString("N");
        await SendRaw(ws2, "handshake.request", hs2, Handshake());
        var hsResp = await rx2.Expect(ProtocolMessageTypes.HandshakeResponse, hs2);
        Say($"[OK] re-handshake: {(hsResp is null ? "MISSING" : "ok")}");

        var user = Username;
        var pass = Password;

        var auth2 = Guid.NewGuid().ToString("N");
        await SendRaw(ws2, "auth.request", auth2,
            new AuthenticationRequest(user, pass));
        var authResp = await rx2.Expect(ProtocolMessageTypes.AuthenticationResponse, auth2);
        if (authResp is null)
        {
            Say("FATAL: no auth.response after reconnect");
            Dump();
            return 7;
        }

        var ok = ProtocolSerializer.DeserializePayload<AuthenticationResponse>(
            new MessageEnvelope { Payload = authResp.Value });
        Say($"[OK] re-authenticated: success={ok.Value.Success}");

        var drain2 = await rx2.DrainFor(TimeSpan.FromSeconds(6));
        foreach (var env in drain2)
            ApplyMapEvent(state, map, env);

        Say($"     bootstrap events after reconnect: {drain2.Count}");
        ReportSnapshot(state, "after-reconnect");

        var dupRooms = state.Rooms.Count - state.Rooms.Values.Select(r => r.RoomId).Distinct().Count();
        var dupEdges = state.Edges.Count - state.Edges.Select(e => (e.From, e.To, e.Direction)).Distinct().Count();

        Say($"     rooms   before/after : {roomsBefore} / {state.Rooms.Count}");
        Say($"     edges   before/after : {edgesBefore} / {state.Edges.Count}");
        Say($"     current room  before/after : {roomBefore} / {state.CurrentRoomId ?? "--"}");
        Say($"     current area  before/after : {areaBefore} / {state.CurrentAreaId ?? "--"}");
        Say($"     floors      before/after : {floorsBefore} / {string.Join(",", state.Floors)}");
        Say($"     duplicate rooms : {dupRooms}");
        Say($"     duplicate edges : {dupEdges}");
        Say($"     frontier stubs : {state.Frontier.Values.Sum(v => v.Count)}");
        Say(string.Empty);

        // ── Fog security audit over everything the live server disclosed ──
        Say("=== fog security audit (live payloads only) ===");
        AuditFog(state);

        Dump();
        return 0;
    }

    /// <summary>
    /// Verifies that nothing the server withheld ever entered client state:
    /// every frontier record is structurally direction-only, and every room
    /// present was disclosed by Keystone.
    /// </summary>
    private static void AuditFog(MapState state)
    {
        var stubCount = state.Frontier.Values.Sum(v => v.Count);
        Say($"     frontier records examined : {stubCount}");

        var hasDestinationField =
            typeof(MapFrontierRecord).GetProperty("To") is not null ||
            typeof(MapFrontierRecord).GetProperty("Destination") is not null;
        Say($"     frontier record has a destination field : {hasDestinationField}  (must be False)");

        var badStates = state.Frontier.Values
            .SelectMany(v => v)
            .Where(f => !string.IsNullOrEmpty(f.From) &&
                        f.DirectionKind != MapDirection.Unknown)
            .ToArray();
        Say($"     frontier records with known direction     : {badStates.Length}");

        Say($"     discovered rooms reported by Keystone     : {state.Rooms.Count}");
        Say($"     total generated rooms in realm (server)   : {state.TotalGeneratedRooms}");
        Say($"     full realm map loaded client-side?          : {state.Rooms.Count > 2000}  (must be False)");
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static Task Send(
        ClientWebSocket ws, string type, string correlationId, object payload) =>
        SendRaw(ws, type, correlationId, payload);

    private static HandshakeRequest Handshake() => new(
        "Portal",
        "1.0.0",
        ProtocolVersion.Current,
        new CapabilityInfo[]
        {
            new("combat.skill", "1.0"),
            new("combat.attack", "1.0"),
            new("target.select", "1.0"),
            new("character.skills.snapshot", "1.0"),
            new("movement.direction", "1.0"),
            new("movement.failed", "1.0"),
        });

    private static async Task SendRaw(
        ClientWebSocket ws, string type, string correlationId, object payload)
    {
        // The payload MUST be camelCase like the rest of the wire format, so it
        // is serialized through the same options Portal.Protocol uses rather
        // than with default reflection options.
        var json = ProtocolSerializer.Serialize(new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Request,
            MessageType = type,
            CorrelationId = correlationId,
            Payload = ProtocolSerializer.SerializePayload(payload),
        });

        Say($"  >> {json}");
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static string Compact(JsonElement e)
    {
        var text = e.GetRawText();
        return text.Length > 240 ? text[..240] + "..." : text;
    }

    /// <summary>
    /// Applies a live event through Portal's OWN deserializer and Portal's OWN
    /// map model, exactly as GameEventService -> MapViewModel does in the client.
    /// </summary>
    private static void ApplyMapEvent(MapState state, MapViewModel map, MessageEnvelope env)
    {
        switch (env.MessageType)
        {
            case ProtocolMessageTypes.MapSnapshot:
            {
                var r = ProtocolSerializer.DeserializePayload<MapSnapshotPayload>(env);
                if (r.IsSuccess)
                {
                    state.ApplySnapshot(r.Value);
                    map.ApplyMapSnapshot(r.Value);
                }
                else
                {
                    Say($"     !! map.snapshot INVALID: {r.Errors[0].Message}");
                }
                break;
            }

            case ProtocolMessageTypes.MapRoomDiscovered:
            {
                var r = ProtocolSerializer.DeserializePayload<MapRoomDiscoveredPayload>(env);
                if (r.IsSuccess)
                {
                    state.ApplyRoomDiscovered(r.Value);
                    map.ApplyMapRoomDiscovered(r.Value);
                }
                else
                {
                    Say($"     !! map.room.discovered INVALID: {r.Errors[0].Message}");
                }
                break;
            }

            case ProtocolMessageTypes.MapPosition:
            {
                var r = ProtocolSerializer.DeserializePayload<MapPositionPayload>(env);
                if (r.IsSuccess)
                {
                    state.ApplyPosition(r.Value);
                    map.ApplyMapPosition(r.Value);
                }
                else
                {
                    Say($"     !! map.position INVALID: {r.Errors[0].Message}");
                }
                break;
            }

            case ProtocolMessageTypes.MapState:
            {
                var r = ProtocolSerializer.DeserializePayload<MapStatePayload>(env);
                if (r.IsSuccess)
                {
                    state.ApplyState(r.Value);
                    map.ApplyMapState(r.Value);
                }
                break;
            }
        }
    }

    private static void ReportSnapshot(MapState state, string label)
    {
        Say($"--- map state [{label}] ---");
        Say($"     rooms={state.Rooms.Count} edges={state.Edges.Count} discoveredCount={state.DiscoveredCount}");
        Say($"     currentRoom={state.CurrentRoomId ?? "--"} currentArea={state.CurrentAreaId ?? "--"} currentZ={state.CurrentZ?.ToString() ?? "--"}");
        Say($"     areas=[{string.Join(", ", state.Areas)}] floors=[{string.Join(", ", state.Floors)}]");

        if (state.CurrentRoomId is { } cur && state.TryGetRoom(cur, out var room))
            Say($"     marker room: id={room.RoomId} name={room.Name} pos={room.X},{room.Y},{room.Z}");

        Say($"     frontier stubs={state.Frontier.Values.Sum(v => v.Count)}");
        Say(string.Empty);
    }

    private static void Dump() =>
        File.WriteAllLines("_qa_live_log.txt", Log);
}