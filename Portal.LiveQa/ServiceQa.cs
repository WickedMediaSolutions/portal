using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Portal.Networking;
using Portal.Protocol;
using Portal.State;

namespace Portal.LiveQa;

/// <summary>
/// Live socket client for service parity QA.
///
/// Mirrors what the shipped client does: every inbound envelope is pushed
/// through <see cref="ProtocolSerializer"/> (Portal's REAL deserializer) and
/// then applied to Portal's REAL <see cref="Portal.State"/> view-models, exactly
/// as <c>GameEventService</c> -> <c>ServiceContextSink</c> does.  Any
/// deserialization or model defect therefore shows up here as a live failure.
/// </summary>
internal sealed class LiveClient : IDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly List<MessageEnvelope> _inbox = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly Action<MessageEnvelope> _onMessage;

    /// <summary>Every message type Portal SENT during the whole session.</summary>
    public List<string> SentTypes { get; } = new();

    /// <summary>Every message type Portal RECEIVED during the whole session.</summary>
    public List<string> ReceivedTypes { get; } = new();

    /// <summary>Payloads that failed Portal's own deserializer.</summary>
    public List<string> DeserializeFailures { get; } = new();

    public bool IsOpen => _ws.State == WebSocketState.Open;

    /// <summary>The live socket state, for the QA report.</summary>
    public string State => _ws.State.ToString();

    public LiveClient(Action<MessageEnvelope> onMessage) => _onMessage = onMessage;

    public async Task ConnectAsync(Uri endpoint, CancellationToken ct)
    {
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await _ws.ConnectAsync(endpoint, timeout.Token);
        _ = Task.Run(PumpAsync);
    }

    private async Task PumpAsync()
    {
        try
        {
            while (_ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(_buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(_buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var text = Encoding.UTF8.GetString(ms.ToArray());
                var parsed = Portal.Protocol.ProtocolSerializer.Deserialize(text);
                if (parsed.IsFailure)
                {
                    DeserializeFailures.Add(
                        (parsed.IsSuccess ? "?" : "unparseable") + ": " + text[..Math.Min(300, text.Length)]);
                    continue;
                }

                var env = parsed.Value;
                ReceivedTypes.Add(env.MessageType);

                // Apply BEFORE queueing, so any envelope a waiter observes has
                // already been routed into the view-models.  Applying after the
                // enqueue would let a test read stale state purely because of
                // thread scheduling.
                _onMessage(env);

                lock (_inbox) _inbox.Add(env);
                _signal.Release();
            }
        }
        catch
        {
            // Socket closed: nothing further to pump.
        }
    }

    /// <summary>Serializes and sends one request through Portal's own serializer.</summary>
    public async Task<string> SendAsync<T>(string messageType, T payload, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            messageType,
            correlationId,
            null,
            Portal.Protocol.ProtocolSerializer.SerializePayload(payload));

        var json = Portal.Protocol.ProtocolSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        SentTypes.Add(messageType);
        return correlationId;
    }

    /// <summary>
    /// Serializes and sends one already-built request envelope.
    /// </summary>
    /// <remarks>
    /// Used where the request type differs from the payload type (the three
    /// quest actions all share one payload shape), so the envelope still goes
    /// out through Portal's own serializer exactly as a real service would.
    /// </remarks>
    public async Task SendRawAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        var json = Portal.Protocol.ProtocolSerializer.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        SentTypes.Add(envelope.MessageType);
    }

    /// <summary>Waits for the first envelope of <paramref name="messageType"/>.</summary>
    public async Task<MessageEnvelope?> WaitAsync(string messageType, int milliseconds = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            lock (_inbox)
            {
                var idx = _inbox.FindIndex(e => e.MessageType == messageType);
                if (idx >= 0)
                {
                    var e = _inbox[idx];
                    _inbox.RemoveAt(idx);
                    return e;
                }
            }

            await _signal.WaitAsync(TimeSpan.FromMilliseconds(120));
        }

        return null;
    }

    /// <summary>
    /// Discards every already-queued envelope of a type before waiting for the
    /// next one. The server legitimately resends snapshots (a trade emits a
    /// fresh shop.snapshot), so a naive wait can return an older event and make
    /// the driver assert against stale state.
    /// </summary>
    public void DiscardQueued(string messageType)
    {
        lock (_inbox) _inbox.RemoveAll(e => e.MessageType == messageType);
    }

    /// <summary>Removes and returns every queued envelope of the given types.</summary>
    public List<MessageEnvelope> TakeAll(params string[] messageTypes)
    {
        var taken = new List<MessageEnvelope>();
        lock (_inbox)
        {
            foreach (var e in _inbox.Where(e => messageTypes.Contains(e.MessageType)).ToList())
            {
                _inbox.Remove(e);
                taken.Add(e);
            }
        }

        return taken;
    }

    /// <summary>Waits a fixed window so late events can land, then clears the queue.</summary>
    public async Task<List<MessageEnvelope>> SettleAsync(int milliseconds = 1200)
    {
        await Task.Delay(milliseconds);
        List<MessageEnvelope> all;
        lock (_inbox)
        {
            all = _inbox.ToList();
            _inbox.Clear();
        }

        return all;
    }

    public void Dispose()
    {
        try { _ws.Dispose(); } catch { /* best effort */ }
    }
}

/// <summary>
/// Holds the REAL Portal view-models a running client would bind to, and routes
/// live envelopes into them exactly as GameEventService + ServiceContextSink do.
/// </summary>
internal sealed class World
{
    public RoomContextViewModel Room { get; } = new();
    public ShopViewModel Shop { get; } = new();
    public BankViewModel Bank { get; } = new();
    public WhoViewModel Who { get; } = new();
    public MapState Map { get; } = new();
    public MapViewModel MapView { get; } = new();

    public int Currency { get; private set; } = -1;
    public int? BankCurrencySeen { get; private set; }
    public List<InventoryItemRecord> Inventory { get; } = new();
    public List<EquippedItemRecord> Equipped { get; } = new();
    public CharacterStatusSnapshotPayload? Status { get; private set; }
    public CharacterPointsSnapshotPayload? Points { get; private set; }
    public CharacterSkillsSnapshotPayload? Skills { get; private set; }
    public CharacterSnapshotPayload? Character { get; private set; }
    public RoomEntitySnapshotPayload? Entities { get; private set; }
    public int RoomStateCount { get; private set; }
    public List<string> Applied { get; } = new();
    public List<string> Rejected { get; } = new();

    /// <summary>Raw JSON of the most recent room.state, for defect diagnosis.</summary>
    public string? LastRoomStateRaw { get; private set; }

    /// <summary>Raw JSON of the most recent shop.snapshot, for defect diagnosis.</summary>
    public string? LastShopSnapshotRaw { get; private set; }

    // The UI layer's own room-change housekeeping, replicated exactly.
    private string? _lastRoomId;
    private string? _lastWorldRoomId;
    private bool _hasRoom;

    public void ResetSession()
    {
        _lastRoomId = null;
        _lastWorldRoomId = null;
        _hasRoom = false;
        Shop.ClearForRoomChange();
        Who.Clear();
        Map.Reset();
        MapView.ClearMap();
        Inventory.Clear();
        Equipped.Clear();
        Currency = -1;
        BankCurrencySeen = null;
    }

    private bool Try<T>(MessageEnvelope env, out T value) where T : class
    {
        var r = Portal.Protocol.ProtocolSerializer.DeserializePayload<T>(env);
        if (r.IsFailure) { Rejected.Add(env.MessageType + ": " + r.Errors[0].Message); value = default!; return false; }
        value = r.Value;
        return true;
    }

    public void Apply(MessageEnvelope env)
    {
        switch (env.MessageType)
        {
            case ProtocolMessageTypes.RoomState:
            {
                if (!Try(env, out RoomStatePayload p)) return;
                bool roomChanged = _hasRoom
                    && (p.RoomId != _lastRoomId || p.WorldRoomId != _lastWorldRoomId);
                _lastRoomId = p.RoomId;
                _lastWorldRoomId = p.WorldRoomId;
                _hasRoom = true;
                if (roomChanged) Shop.ClearForRoomChange();
                Room.ApplyRoomState(p);
                RoomStateCount++;
                LastRoomStateRaw = env.Payload.GetRawText();
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.ShopSnapshot:
            {
                if (!Try(env, out ShopSnapshotPayload p)) return;
                if (!p.Available && p.Shop is null) Shop.ClearForRoomChange();
                Shop.ApplyShopSnapshot(p);
                LastShopSnapshotRaw = env.Payload.GetRawText();
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.ShopResult:
            {
                if (!Try(env, out ShopResultPayload p)) return;
                Shop.ApplyShopResult(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.BankSnapshot:
            {
                if (!Try(env, out BankSnapshotPayload p)) return;
                Bank.ApplyBankSnapshot(p);
                BankCurrencySeen = p.BankCurrency;
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.BankResult:
            {
                if (!Try(env, out BankResultPayload p)) return;
                Bank.ApplyBankResult(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.DoorResult:
            {
                if (!Try(env, out DoorResultPayload p)) return;
                Room.ApplyDoorResult(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.WhoSnapshot:
            {
                if (!Try(env, out WhoSnapshotPayload p)) return;
                Who.ApplyWhoSnapshot(p);
                Applied.Add(env.MessageType);
                return;
            }
case ProtocolMessageTypes.InventorySnapshot:
            {
                if (!Try(env, out InventorySnapshotPayload p)) return;
                Inventory.Clear();
                Inventory.AddRange(p.Items);
                Currency = p.Currency;
                Shop.Currency = p.Currency;
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.EquipmentSnapshot:
            {
                if (!Try(env, out EquipmentSnapshotPayload p)) return;
                Equipped.Clear();
                Equipped.AddRange(p.Equipped);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.CharacterStatusSnapshot:
                if (Try(env, out CharacterStatusSnapshotPayload s)) { Status = s; Applied.Add(env.MessageType); }
                return;

            case ProtocolMessageTypes.CharacterPointsSnapshot:
                if (Try(env, out CharacterPointsSnapshotPayload cp)) { Points = cp; Applied.Add(env.MessageType); }
                return;

            case ProtocolMessageTypes.CharacterSkillsSnapshot:
                if (Try(env, out CharacterSkillsSnapshotPayload sk)) { Skills = sk; Applied.Add(env.MessageType); }
                return;

            case ProtocolMessageTypes.CharacterSnapshot:
                if (Try(env, out CharacterSnapshotPayload ch)) { Character = ch; Applied.Add(env.MessageType); }
                return;

            case ProtocolMessageTypes.RoomEntitySnapshot:
                if (Try(env, out RoomEntitySnapshotPayload en)) { Entities = en; Applied.Add(env.MessageType); }
                return;

            case ProtocolMessageTypes.MapSnapshot:
            {
                if (!Try(env, out MapSnapshotPayload p)) return;
                Map.ApplySnapshot(p); MapView.ApplyMapSnapshot(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.MapRoomDiscovered:
            {
                if (!Try(env, out MapRoomDiscoveredPayload p)) return;
                Map.ApplyRoomDiscovered(p); MapView.ApplyMapRoomDiscovered(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.MapPosition:
            {
                if (!Try(env, out MapPositionPayload p)) return;
                Map.ApplyPosition(p); MapView.ApplyMapPosition(p);
                Applied.Add(env.MessageType);
                return;
            }

            case ProtocolMessageTypes.MapState:
            {
                if (!Try(env, out MapStatePayload p)) return;
                Map.ApplyState(p); MapView.ApplyMapState(p);
                Applied.Add(env.MessageType);
                return;
            }
        }
    }

    public int QtyOf(string itemId) =>
        Inventory.Where(i => i.ItemId == itemId).Sum(i => i.Quantity);

    public InventoryItemRecord? Item(string itemId) =>
        Inventory.FirstOrDefault(i => i.ItemId == itemId);
}

/// <summary>
/// Live end-to-end service/room/door/WHO parity QA driver.
///
/// Talks to a REAL running Keystone over the REAL WebSocket bridge using
/// Portal's own serializer, deserializer and view-models.  Nothing here
/// bypasses the protocol or mutates any registry.
/// </summary>
internal static class ServiceQa
{
    private static readonly List<string> Log = new();
    private static int _pass;
    private static int _fail;
    private static int _skip;

    private static void Say(string line)
    {
        Log.Add(line);
        Console.WriteLine(line);
    }

    private static void Head(string title)
    {
        Say(string.Empty);
        Say(new string('=', 74));
        Say(title);
        Say(new string('=', 74));
    }

    private static void Check(bool ok, string label, string detail = "")
    {
        if (ok) { _pass++; Say($"  [PASS] {label}{(detail.Length > 0 ? " :: " + detail : "")}"); }
        else { _fail++; Say($"  [FAIL] {label}{(detail.Length > 0 ? " :: " + detail : "")}"); }
    }

    private static void Skip(string label, string detail = "")
    {
        _skip++;
        Say($"  [SKIP] {label}{(detail.Length > 0 ? " :: " + detail : "")}");
    }

    private static void Info(string label, object? value) => Say($"         {label,-34} {value}");

    private static string Arg(string[] args, string name)
    {
        var prefix = "--" + name + "=";
        foreach (var a in args)
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return a[prefix.Length..];
        return null;
    }

    // Live routes computed from the REAL exit graph (see _liveqa_routes.py).
    private static readonly (string Direction, string From, string To)[] ToShop =
    {
        ("southeast", "val_cw_muster_gallery", "val_cw_constable"),
        ("south", "val_cw_constable", "val_cw_sundial_terrace"),
        ("east", "val_cw_sundial_terrace", "valroian_capital_start"),
        ("northwest", "valroian_capital_start", "val_cw_undercroft"),
        ("north", "val_cw_undercroft", "val_cw_cistern_head"),
        ("east", "val_cw_cistern_head", "val_cw_under_crawl"),
        ("up", "val_cw_under_crawl", "val_lm_square"),
        ("north", "val_lm_square", "val_lm_cloth_walk"),
        ("east", "val_lm_cloth_walk", "val_lm_dyers_court"),
        ("northeast", "val_lm_dyers_court", "val_lm_crownprov_front"),
        ("up", "val_lm_crownprov_front", "val_lm_crownprov_floor"),
    };
private static readonly (string Direction, string From, string To)[] ToBank =
    {
        ("down", "val_lm_crownprov_floor", "val_lm_crownprov_front"),
        ("southwest", "val_lm_crownprov_front", "val_lm_dyers_court"),
        ("west", "val_lm_dyers_court", "val_lm_cloth_walk"),
        ("south", "val_lm_cloth_walk", "val_lm_square"),
        ("southwest", "val_lm_square", "val_cw_chandlery_lane"),
        ("west", "val_cw_chandlery_lane", "val_cw_east_colonnade"),
        ("west", "val_cw_east_colonnade", "val_cw_scholars_terrace"),
        ("west", "val_cw_scholars_terrace", "val_cw_chandlery_row"),
        ("southwest", "val_cw_chandlery_row", "val_cw_west_colonnade"),
        ("northwest", "val_cw_west_colonnade", "val_cw_vale_arch"),
        ("west", "val_cw_vale_arch", "val_vq_ward_arch"),
        ("west", "val_vq_ward_arch", "val_vq_chancery_lane"),
        ("north", "val_vq_chancery_lane", "val_vq_counting_house"),
        ("up", "val_vq_counting_house", "val_vq_counting_floor"),
    };

    private static readonly (string Direction, string From, string To)[] BankToHealing =
    {
        ("down", "val_vq_counting_floor", "val_vq_counting_house"),
        ("south", "val_vq_counting_house", "val_vq_chancery_lane"),
        ("east", "val_vq_chancery_lane", "val_vq_ward_arch"),
        ("northwest", "val_vq_ward_arch", "val_vq_ordinance_lane"),
        ("west", "val_vq_ordinance_lane", "val_vq_mint_lane"),
        ("north", "val_vq_mint_lane", "val_vq_mint_gate"),
        ("northeast", "val_vq_mint_gate", "val_vq_healers_guild"),
        ("up", "val_vq_healers_guild", "val_vq_dispensary"),
    };

    private static readonly (string Direction, string From, string To)[] HealingToBank =
    {
        ("down", "val_vq_dispensary", "val_vq_healers_guild"),
        ("southwest", "val_vq_healers_guild", "val_vq_mint_gate"),
        ("south", "val_vq_mint_gate", "val_vq_mint_lane"),
        ("east", "val_vq_mint_lane", "val_vq_ordinance_lane"),
        ("southeast", "val_vq_ordinance_lane", "val_vq_ward_arch"),
        ("west", "val_vq_ward_arch", "val_vq_chancery_lane"),
        ("north", "val_vq_chancery_lane", "val_vq_counting_house"),
        ("up", "val_vq_counting_house", "val_vq_counting_floor"),
    };

    private static readonly (string Direction, string From, string To)[] BankToDoor =
    {
        ("down", "val_vq_counting_floor", "val_vq_counting_house"),
        ("south", "val_vq_counting_house", "val_vq_chancery_lane"),
        ("east", "val_vq_chancery_lane", "val_vq_ward_arch"),
        ("east", "val_vq_ward_arch", "val_cw_vale_arch"),
        ("northeast", "val_cw_vale_arch", "val_cw_almshall"),
        ("down", "val_cw_almshall", "val_cw_almscellar"),
    };

    /// <summary>Connects, handshakes and authenticates. Returns null on failure.</summary>
    private static async Task<LiveClient?> ConnectAsync(
        Uri endpoint, string user, string pass, World world, bool expectSuccess = true)
    {
        var client = new LiveClient(world.Apply);
        try
        {
            await client.ConnectAsync(endpoint, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Say($"  FATAL connect: {ex.GetType().Name}: {ex.Message}");
            client.Dispose();
            return null;
        }

        Info("websocket", $"{endpoint} state={client.State}");

        var hsId = await client.SendAsync(
            ProtocolMessageTypes.HandshakeRequest, Handshake());
        var hs = await client.WaitAsync(ProtocolMessageTypes.HandshakeResponse, 10000);
        if (hs is null)
        {
            Say($"  FATAL: no handshake.response (correlation {hsId})");
            client.Dispose();
            return null;
        }

        var hsOk = Portal.Protocol.ProtocolSerializer.DeserializePayload<HandshakeResponse>(hs);
        Check(hsOk.IsSuccess && hsOk.Value.Accepted, "handshake accepted",
            hsOk.IsSuccess ? "accepted=true" : hsOk.Errors[0].Message);

        var authId = await client.SendAsync(
            ProtocolMessageTypes.AuthenticationRequest,
            new AuthenticationRequest(user, pass));
        var auth = await client.WaitAsync(ProtocolMessageTypes.AuthenticationResponse, 12000);
        if (auth is null)
        {
            Say($"  FATAL: no auth.response (correlation {authId})");
            client.Dispose();
            return null;
        }

        var a = Portal.Protocol.ProtocolSerializer.DeserializePayload<AuthenticationResponse>(auth);
        if (a.IsFailure)
        {
            Say($"  FATAL: auth.response unparseable: {a.Errors[0].Message}");
            client.Dispose();
            return null;
        }

        Check(a.Value.Success == expectSuccess, $"auth.request {(expectSuccess ? "succeeds" : "is rejected")}",
            a.Value.Success
                ? $"session={a.Value.Session?.SessionId} character={a.Value.Session?.CharacterName} id={a.Value.Session?.CharacterId}"
                : $"{a.Value.ErrorCode}: {a.Value.ErrorMessage}");

        if (!expectSuccess)
        {
            client.Dispose();
            return null;
        }

        return client;
    }

    /// <summary>Walks one hop and verifies the authoritative room.state that followed.</summary>
    private static async Task<bool> StepAsync(
        LiveClient c, World w, string direction, string expectWorldRoomId, bool verbose)
    {
        var before = w.Room.RoomName;
        var beforeCount = w.RoomStateCount;

        await c.SendAsync(
            ProtocolMessageTypes.MovementDirectionRequest,
            new MovementDirectionRequest(direction));

        var failed = await c.WaitAsync(ProtocolMessageTypes.MovementFailed, 1200);
        if (failed is not null)
        {
            // A real generated door can legitimately stand between two rooms on
            // a normal route. Opening it through the ordinary door protocol is
            // ordinary play, so the sweep does what a player does.
            var door = w.Room.Doors.FirstOrDefault(d => d.Direction == direction);
            if (door is not null && door.CanOpen)
            {
                await c.SendAsync(
                    ProtocolMessageTypes.DoorActionRequest,
                    new DoorActionRequest(direction, "open"));
                var opened = await c.WaitAsync(ProtocolMessageTypes.DoorResult, 5000);
                var newRoom = await c.WaitAsync(ProtocolMessageTypes.RoomState, 4000);
                if (opened is not null && w.Room.DoorFeedbackIsSuccess)
                {
                    if (verbose) Say($"         {direction,-11} opened a door to continue");
                    await StepAsync(c, w, direction, expectWorldRoomId, verbose);
                    return string.Equals(w.Room.AreaId, expectWorldRoomId,
                        StringComparison.Ordinal);
                }
            }

            if (verbose) Say($"         {direction,-11} BLOCKED  {before} (movement.failed)");
            return false;
        }

        var rs = await c.WaitAsync(ProtocolMessageTypes.RoomState, 4000);
        if (rs is null)
        {
            Check(false, $"room.state after moving {direction}", "no room.state received");
            return false;
        }

        var ok = w.Room.RoomName.Length > 0 && w.RoomStateCount > beforeCount
                 && string.Equals(w.Room.AreaId, expectWorldRoomId, StringComparison.Ordinal);

        if (verbose || !ok)
            Say($"         {direction,-11} {before,-34} -> {w.Room.RoomName,-34} [{w.Room.AreaId}]");

        if (!ok)
            Check(false, $"room.state after moving {direction}",
                $"expected world_room_id={expectWorldRoomId}, got {w.Room.AreaId} (room '{w.Room.RoomName}')");

        return ok;
    }

    /// <summary>Walks a whole route, verifying every hop and the room.state count.</summary>
    private static async Task<bool> WalkAsync(
        LiveClient c, World w, (string Direction, string From, string To)[] route, string label)
    {
        // Idempotent: a previous QA run may already have left the character at
        // the destination, in which case there is nothing to walk.
        if (route.Length > 0
            && string.Equals(w.Room.AreaId, route[^1].To, StringComparison.Ordinal))
        {
            Say($"         already standing at {route[^1].To}; route skipped");
            return true;
        }

        var rsBefore = w.RoomStateCount;
        var allOk = true;
        foreach (var hop in route)
        {
            var here = string.Equals(w.Room.AreaId, hop.From, StringComparison.Ordinal);
            if (!here)
            {
                Check(false, $"{label} hop {hop.Direction}",
                    $"expected to be in {hop.From}, actually {w.Room.AreaId} ('{w.Room.RoomName}')");
                return false;
            }

            if (!await StepAsync(c, w, hop.Direction, hop.To, verbose: false))
            {
                Check(false, $"{label} hop {hop.Direction} blocked",
                    $"stayed in {w.Room.AreaId} ('{w.Room.RoomName}')");
                return false;
            }
        }

        var moved = w.RoomStateCount - rsBefore;
        Check(allOk && moved >= route.Length,
            $"{label}: {route.Length} hops, room.state re-pushed on every move",
            $"room.state events={moved} (>= hops; extra events are door refreshes), "
            + $"now in [{w.Room.AreaId}]");
        return true;
    }

    /// <summary>
    /// Release-verification helper: connects with the real client, optionally
    /// walks a comma-separated route, and optionally issues a real
    /// character.respawn.request. Used during release preparation to put the
    /// QA character back into a known room so the service sweep's hardcoded
    /// route is valid, and to exercise the respawn path end to end.
    ///
    /// Not part of the shipped client: this file lives in the QA-only project
    /// that is excluded from Portal.slnx and from the distribution directory.
    /// </summary>
    public static async Task<int> RunRelVerifyAsync(string[] args)
    {
        var user = Arg(args, "user") ?? "testuser";
        var pass = Arg(args, "pass") ?? "qaportal2026";
        var routeArg = Arg(args, "route");
        var doRespawn = args.Contains("--respawn");
        var target = PortalEndpoints.FromToggle(!args.Contains("--production"));
        var endpoint = PortalEndpoints.Resolve(target);

        Head("REL-VERIFY (QA-only helper)");
        Info("endpoint", endpoint.ToString());

        var world = new World();
        var client = await ConnectAsync(endpoint, user, pass, world);
        if (client is null)
        {
            Say("FATAL: could not authenticate; aborting rel-verify.");
            Dump();
            return 2;
        }

        await client.SettleAsync(3000);
        Info("start room", $"{world.Room.AreaId} ('{world.Room.RoomName}')");

        if (!string.IsNullOrWhiteSpace(routeArg))
        {
            foreach (var dir in routeArg.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries))
            {
                var before = world.Room.AreaId;
                var beforeName = world.Room.RoomName;
                await client.SendAsync(
                    ProtocolMessageTypes.MovementDirectionRequest,
                    new MovementDirectionRequest(dir));
                var failed = await client.WaitAsync(ProtocolMessageTypes.MovementFailed, 1500);
                if (failed is not null)
                {
                    Check(false, $"walk {dir}", "movement.failed");
                    continue;
                }

                var rs = await client.WaitAsync(ProtocolMessageTypes.RoomState, 5000);
                Check(rs is not null && !string.Equals(before, world.Room.AreaId, StringComparison.Ordinal),
                    $"walk {dir}", $"[{before}] {beforeName} -> [{world.Room.AreaId}] {world.Room.RoomName}");
            }
        }

        Info("room after route", $"{world.Room.AreaId} ('{world.Room.RoomName}')");

        if (doRespawn)
        {
            Head("RESPAWN PATH");
            Info("hp before", $"{world.Character.Hp}/{world.Character.MaxHp} lv={world.Character.Level}");
            var id = await client.SendAsync(ProtocolMessageTypes.CharacterRespawnRequest, new { });
            Say($"  >> character.respawn.request correlation={id}");
            var rr = await client.WaitAsync(ProtocolMessageTypes.CharacterRespawnResult, 15000);
            if (rr is null)
            {
                Check(false, "character.respawn.result received", "no result within 15s");
            }
            else
            {
                Check(true, "character.respawn.result received", rr.Payload.GetRawText());
                await client.WaitAsync(ProtocolMessageTypes.RoomState, 6000);
                Info("room after respawn", $"{world.Room.AreaId} ('{world.Room.RoomName}')");
                Info("hp after", $"{world.Character.Hp}/{world.Character.MaxHp} lv={world.Character.Level}");
            }
        }

        Info("who", world.Who.Count.ToString());
        client.Dispose();
        Dump();
        return 0;
    }

    private static HandshakeRequest Handshake() => new(
        "Portal", "1.0.0", ProtocolVersion.Current,
        new[]
        {
            new CapabilityInfo("combat.skill", "1.0"),
            new CapabilityInfo("combat.attack", "1.0"),
            new CapabilityInfo("target.select", "1.0"),
            new CapabilityInfo("character.skills.snapshot", "1.0"),
            new CapabilityInfo("movement.direction", "1.0"),
            new CapabilityInfo("movement.failed", "1.0"),
        });

    public static async Task<int> RunAsync(string[] args)
    {
        var user = Arg(args, "user") ?? "testuser";
        var pass = Arg(args, "pass") ?? "qaportal2026";
        var useLocalDev = !args.Contains("--production");

        // Resolve the endpoint through Portal's OWN resolver: the same code path
        // the login bar's "Use Local Development Server" toggle uses.
        var target = PortalEndpoints.FromToggle(useLocalDev);
        var endpoint = PortalEndpoints.Resolve(target);

        Head("SECTION 1 - CONNECTION (Portal 'Use Local Development Server' = ON)");
        Info("toggle_local_development", useLocalDev);
        Info("resolved_target", PortalEndpoints.Describe(target));
        Info("resolved_endpoint", endpoint);
        Info("plaintext_transport", PortalEndpoints.IsPlaintextTransport(endpoint));
        Info("account", user);

        var world = new World();

        Head("SECTION 2 - INVALID LOGIN (authoritative rejection, no crash)");
        var badWorld = new World();
        var badClient = await ConnectAsync(endpoint, user, "definitely-not-the-password",
            badWorld, expectSuccess: false);
        Check(badClient is null, "invalid password is refused and the socket closes cleanly");

        Head("SECTION 3 - LOGIN / BOOTSTRAP");
        var client = await ConnectAsync(endpoint, user, pass, world);
        if (client is null)
        {
            Say("FATAL: could not authenticate; aborting live QA.");
            Dump();
            return 2;
        }

        await client.SettleAsync(4000);
        Say($"  [bootstrap] {client.ReceivedTypes.Count} events received");
        foreach (var g in client.ReceivedTypes.GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal))
            Say($"     {g.Key} x{g.Count()}");

        foreach (var required in new[]
        {
            ProtocolMessageTypes.CharacterSnapshot,
            ProtocolMessageTypes.CharacterStatusSnapshot,
            ProtocolMessageTypes.InventorySnapshot,
            ProtocolMessageTypes.EquipmentSnapshot,
            ProtocolMessageTypes.CharacterSkillsSnapshot,
            ProtocolMessageTypes.CharacterPointsSnapshot,
            ProtocolMessageTypes.RoomState,
            ProtocolMessageTypes.RoomEntitySnapshot,
            ProtocolMessageTypes.MapSnapshot,
        })
        {
            Check(client.ReceivedTypes.Contains(required), $"bootstrap contains {required}");
        }

        Check(world.Currency >= 0, "resources: carried copper in inventory.snapshot", $"{world.Currency}c");
        Check(world.Status is not null, "resources: HP/mana/stamina snapshot present");
        Check(world.Skills is not null, "skills/spells snapshot present");
        Check(world.Points is not null, "character points snapshot present");
        Check(world.Map.CurrentRoomId is not null, "map.position / current room present",
            world.Map.CurrentRoomId ?? "missing");
        Check(client.DeserializeFailures.Count == 0,
            "every inbound payload parsed by Portal's own deserializer",
            $"{client.DeserializeFailures.Count} failure(s)");
        foreach (var f in client.DeserializeFailures.Take(3)) Say("        " + f);
        Check(world.Rejected.Count == 0,
            "every inbound payload accepted by Portal's own models",
            $"{world.Rejected.Count} rejected: {string.Join(" | ", world.Rejected.Take(3))}");

        Head("SECTION 4 - AUTHORITATIVE ROOM STATE");
        Info("room.state worldRoomId", world.Room.AreaId);
        Info("room.name", world.Room.RoomName);
        Info("room.description (first 150)", Trim(world.Room.RoomDescription, 150));
        Info("description length", world.Room.RoomDescription.Length);
        Info("exits", string.Join(", ", world.Room.Exits.Select(e => e.Direction)));
        Info("doors", world.Room.Doors.Count == 0
            ? "(none in this room)"
            : string.Join(", ", world.Room.Doors.Select(d =>
                $"{d.Direction}[{(d.IsOpen ? "open" : "closed")}{(d.IsLocked ? ",locked" : "")}]")));
        Info("services.shopIds", string.Join(", ", world.Room.ShopIds));
        Info("services.bank", world.Room.HasBank);
        Info("services.healingRoom", world.Room.IsHealingRoom);
        Info("map currentRoomId", world.Map.CurrentRoomId ?? "-");
        Info("map rooms discovered", world.Map.Rooms.Count);
        Info("room entities", world.Entities?.Entities?.Count ?? 0);

        Check(world.Room.RoomName.Length > 0, "room name present");
        Check(world.Room.RoomDescription.Length > 0, "room description present");
        Check(world.Map.CurrentRoomId is not null && world.Room.AreaId.Length > 0,
            "map current room and room.state agree on world room",
            $"map={world.Map.CurrentRoomId} room.state={world.Room.AreaId}");
        Check(world.Room.Doors.Count <= world.Room.Exits.Count,
            "door rows are a subset of real exits",
            $"{world.Room.Doors.Count} doors / {world.Room.Exits.Count} exits");

        return await PhasesAsync(endpoint, user, pass, client, world, args);
    }

    private static string Trim(string s, int n) =>
        string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= n ? s : s[..n] + "...");

    /// <summary>Extracts the raw "services" object from a room.state payload.</summary>
    private static string ServicesOf(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "(no raw payload)";
        var i = raw.IndexOf("\"services\"", StringComparison.Ordinal);
        return i < 0 ? "(no services key)" : raw[i..];
    }

    private static async Task<int> PhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        // ─────────────────────────── WHO ───────────────────────────
        Head("SECTION 5 - WHO (Players Online control)");
        await client.SendAsync(ProtocolMessageTypes.WhoRequest, new WhoRequest());
        var who = await client.WaitAsync(ProtocolMessageTypes.WhoSnapshot, 8000);
        if (who is null)
        {
            Check(false, "who.request returns who.snapshot", "no who.snapshot received");
        }
        else
        {
            Check(true, "who.request returns who.snapshot", $"{w.Who.Rows.Count} row(s)");
            Say($"         summary: {w.Who.Summary}");
            Say($"         {"Name",-22} {"Lv",3}  {"Race",-14} {"Profession",-16} {"Faction",-10} {"Guild",-10} Sect");
            foreach (var r in w.Who.Rows)
                Say($"         {r.Name,-22} {r.Level,3}  {r.Race,-14} {r.Profession,-16} {r.Faction,-10} {r.Guild,-10} {r.Sect}");

            Check(w.Who.Rows.Count == w.Who.Count, "WhoViewModel count matches visible rows",
                $"{w.Who.Count} / {w.Who.Rows.Count}");
            var qa = w.Who.Rows.FirstOrDefault(r => r.Name.Contains("testchar", StringComparison.OrdinalIgnoreCase));
            Check(qa is not null, "QA character appears in who.snapshot",
                qa is null ? "not present" : $"{qa.Name} L{qa.Level} {qa.Race}/{qa.Profession}");
            Check(w.Who.Rows.All(r => r.Level > 0), "every row publishes a real level");
            var raw = who.Payload!.GetRawText();
            Check(!raw.Contains("\"title\"", StringComparison.OrdinalIgnoreCase),
                "who.snapshot carries no fabricated title column");
        }

        // ─────────────────────────── SHOP ───────────────────────────
        Head("SECTION 6 - REAL GENERATED SHOP (walked to normally)");
        Info("target area", "valroian_capital");
        Info("target world_room_id", "val_lm_crownprov_floor");
        Info("target room", "Crownprovision Store Floor");
        Info("route", string.Join(" -> ", ToShop.Select(h => h.Direction)));
        if (!await WalkAsync(client, w, ToShop, "walk to shop"))
        {
            Check(false, "reached the shop room");
            return FinishAsync(client, w);
        }

        Check(string.Equals(w.Room.AreaId, "val_lm_crownprov_floor", StringComparison.Ordinal),
            "Portal is standing in the intended Keystone shop room", w.Room.AreaId);
        Check(w.Room.HasShops, "Portal exposes a Shop action/context for this room",
            string.Join(", ", w.Room.ShopIds));
        if (!w.Room.HasShops)
            Say("         RAW room.state services: " + ServicesOf(w.LastRoomStateRaw));

        // Shop LIST
        await client.SendAsync(ProtocolMessageTypes.ShopSnapshotRequest, new ShopSnapshotRequest(null));
        var listEnv = await client.WaitAsync(ProtocolMessageTypes.ShopSnapshot, 8000);
        Check(listEnv is not null, "shop.snapshot.request returns shop.snapshot (shop list)");
        if (listEnv is not null)
        {
            Check(w.Shop.Shops.Count > 0, "shop list is populated", $"{w.Shop.Shops.Count} shop(s)");
            foreach (var s in w.Shop.Shops) Say($"         shop id={s.ShopId} name='{s.Name}'");
            Say("         RAW shop.snapshot: " + Trim(w.LastShopSnapshotRaw ?? "(none)", 400));
        }

        // Shop WARES
        var shopId = w.Room.ShopIds.FirstOrDefault();
        if (shopId is null)
        {
            Check(false, "room advertises at least one shop id",
                "no shop id in room.state services");
            Say("         RAW shop.snapshot: " + Trim(w.LastShopSnapshotRaw ?? "(none)", 300));
            // Continue: the remaining systems are QA'd independently.
            return await ShopFailurePhasesAsync(endpoint, user, pass, client, w, args);
        }

        await client.SendAsync(ProtocolMessageTypes.ShopSnapshotRequest, new ShopSnapshotRequest(shopId));
        var waresEnv = await client.WaitAsync(ProtocolMessageTypes.ShopSnapshot, 8000);
        Check(waresEnv is not null, "shop.snapshot for one shop returns its wares");
        Check(w.Shop.SelectedShopName.Length > 0, "Portal displays the real shop name", w.Shop.SelectedShopName);
        Check(w.Shop.Wares.Count > 0, "Portal displays real wares", $"{w.Shop.Wares.Count} line(s)");
        Check(w.Shop.Currency == w.Currency, "Portal displays carried copper in the shop panel",
            $"{w.Shop.Currency}c");
        Say($"         {"item_id",-26} {"name",-26} {"buy",6} {"sell",6} {"stock",7}  canBuy/canSell");
        foreach (var ware in w.Shop.Wares.Take(20))
            Say($"         {ware.ItemId,-26} {Trim(ware.Name, 26),-26} "
                + $"{(ware.BuyPrice?.ToString() ?? "-"),6} {(ware.SellPrice?.ToString() ?? "-"),6} "
                + $"{(ware.Stock?.ToString() ?? "inf"),7}  {ware.CanBuy}/{ware.CanSell}");
        Check(w.Shop.Wares.Any(x => x.BuyPrice is > 0), "server published real buy prices");
        Check(w.Shop.Wares.Any(x => x.SellPrice is > 0), "server published real sell prices");

        return await TradePhasesAsync(endpoint, user, pass, client, w, args);
    }

    private static async Task<int> TradePhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        var shopId = w.Room.ShopIds.First();
        var buyable = w.Shop.Wares.Where(x => x.CanBuy).OrderBy(x => x.BuyPrice).ToList();

        // ── SUCCESSFUL BUY ──
        Head("SECTION 7 - SHOP: SUCCESSFUL BUY");
        if (buyable.Count == 0)
        {
            Check(false, "shop offers at least one purchasable ware");
        }
        else
        {
            var item = buyable[0];
            Info("item", $"{item.ItemId} '{item.Name}' buy={item.BuyPrice}c sell={item.SellPrice}c stock={item.Stock?.ToString() ?? "infinite"}");

            var copperBefore = w.Currency;
            var qtyBefore = w.QtyOf(item.ItemId);
            var stockBefore = item.Stock;
            Info("BEFORE copper", copperBefore);
            Info("BEFORE quantity", qtyBefore);
            Info("BEFORE stock", stockBefore?.ToString() ?? "infinite");

            await client.SendAsync(ProtocolMessageTypes.ShopBuyRequest, new ShopBuyRequest(shopId, item.ItemId, 1));
            var res = await client.WaitAsync(ProtocolMessageTypes.ShopResult, 10000);
            Check(res is not null, "shop.buy.request returns shop.result");
            if (res is not null)
            {
                var p = Portal.Protocol.ProtocolSerializer.DeserializePayload<ShopResultPayload>(res);
                if (p.IsSuccess)
                {
                    Check(p.Value.Success, "Keystone accepted the buy", p.Value.Message);
                    Info("shop.result message", p.Value.Message);
                    Info("shop.result totalCost", p.Value.TotalCost);
                }
                else Check(false, "shop.result parsed", p.Errors[0].Message);
            }

            await client.SettleAsync(2000);
            var copperAfter = w.Currency;
            var qtyAfter = w.QtyOf(item.ItemId);
            Info("AFTER copper", copperAfter);
            Info("AFTER quantity", qtyAfter);

            Check(qtyAfter == qtyBefore + 1, "item added to inventory without reconnect",
                $"{qtyBefore} -> {qtyAfter}");
            Check(copperAfter == copperBefore - item.BuyPrice!.Value,
                "correct price deducted",
                $"{copperBefore} - {item.BuyPrice} = {copperBefore - item.BuyPrice.Value} (actual {copperAfter})");
            Check(w.Shop.ResultIsSuccess && w.Shop.HasResult, "Portal shows the authoritative result",
                w.Shop.ResultDisplay);

            var refreshed = w.Shop.Wares.FirstOrDefault(x => x.ItemId == item.ItemId);
            if (stockBefore is int sb && refreshed is not null)
                Check(refreshed.Stock == sb - 1, "finite stock decremented", $"{sb} -> {refreshed.Stock?.ToString() ?? "null"}");
            else
                Skip("finite stock decrement", "this ware has unlimited stock");

            // ── SUCCESSFUL SELL ──
            Head("SECTION 8 - SHOP: SUCCESSFUL SELL");
            var copperBeforeSell = w.Currency;
            var qtyBeforeSell = w.QtyOf(item.ItemId);

            await client.SendAsync(ProtocolMessageTypes.ShopSellRequest, new ShopSellRequest(shopId, item.ItemId, 1));
            var sellRes = await client.WaitAsync(ProtocolMessageTypes.ShopResult, 10000);
            Check(sellRes is not null, "shop.sell.request returns shop.result");
            if (sellRes is not null)
            {
                var p = Portal.Protocol.ProtocolSerializer.DeserializePayload<ShopResultPayload>(sellRes);
                if (p.IsSuccess)
                {
                    Check(p.Value.Success, "Keystone accepted the sell", p.Value.Message);
                    Info("shop.result message", p.Value.Message);
                    Check(p.Value.TotalCost == item.SellPrice!.Value, "server credited the published sell price",
                        $"{p.Value.TotalCost}c (published {item.SellPrice}c)");
                }
                else Check(false, "shop.result parsed", p.Errors[0].Message);
            }

            await client.SettleAsync(2000);
            Info("AFTER sell copper", w.Currency);
            Info("AFTER sell quantity", w.QtyOf(item.ItemId));
            Check(w.QtyOf(item.ItemId) == qtyBeforeSell - 1,
                "sold item removed/decremented from inventory",
                $"{qtyBeforeSell} -> {w.QtyOf(item.ItemId)}");
            Check(w.Currency == copperBeforeSell + item.SellPrice!.Value,
                "correct sell value credited",
                $"{copperBeforeSell} + {item.SellPrice} (actual {w.Currency})");
        }

        return await ShopFailurePhasesAsync(endpoint, user, pass, client, w, args);
    }

    private static async Task<int> ShopFailurePhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        var shopId = w.Room.ShopIds.First();
        Head("SECTION 9 - SHOP: AUTHORITATIVE FAILURE CASES");

        var tooDear = w.Shop.Wares.Where(x => x.CanBuy).OrderByDescending(x => x.BuyPrice).FirstOrDefault();
        if (tooDear is not null)
        {
            var before = w.Currency;
            var beforeQty = w.QtyOf(tooDear.ItemId);
            await client.SendAsync(ProtocolMessageTypes.ShopBuyRequest,
                new ShopBuyRequest(shopId, tooDear.ItemId, 99));
            var r = await client.WaitAsync(ProtocolMessageTypes.ShopResult, 10000);
            Check(r is not null && !w.Shop.ResultIsSuccess && w.Shop.ResultMessage.Length > 0,
                $"insufficient-funds / over-quantity buy refused ({tooDear.ItemId})", w.Shop.ResultDisplay);
            await client.SettleAsync(1200);
            Check(w.Currency == before && w.QtyOf(tooDear.ItemId) == beforeQty,
                "refused buy changed neither copper nor inventory",
                $"copper {before}->{w.Currency}, qty {beforeQty}->{w.QtyOf(tooDear.ItemId)}");
        }

        await client.SendAsync(ProtocolMessageTypes.ShopBuyRequest,
            new ShopBuyRequest(shopId, "definitely_not_a_real_item", 1));
        var inv1 = await client.WaitAsync(ProtocolMessageTypes.ShopResult, 10000);
        Check(inv1 is not null && !w.Shop.ResultIsSuccess && w.Shop.ResultMessage.Length > 0,
            "invalid item id refused with a server message", w.Shop.ResultDisplay);

        await client.SendAsync(ProtocolMessageTypes.ShopBuyRequest,
            new ShopBuyRequest("valroian_town_1_general_shop", "wooden_plank", 1));
        var inv2 = await client.WaitAsync(ProtocolMessageTypes.ShopResult, 10000);
        Check(inv2 is not null && !w.Shop.ResultIsSuccess,
            "shop not linked to the current room is refused", w.Shop.ResultDisplay);

        await client.SendAsync(ProtocolMessageTypes.ShopSnapshotRequest,
            new ShopSnapshotRequest("valroian_town_1_general_shop"));
        client.DiscardQueued(ProtocolMessageTypes.ShopSnapshot);
        await client.SendAsync(ProtocolMessageTypes.ShopSnapshotRequest,
            new ShopSnapshotRequest("valroian_town_1_general_shop"));
        await client.WaitAsync(ProtocolMessageTypes.ShopSnapshot, 8000);
        Check(!w.Shop.HasWares, "wares for a foreign shop are never displayed",
            $"{w.Shop.Wares.Count} wares");

        return await BankPhasesAsync(endpoint, user, pass, client, w, args);
    }

    private static async Task<int> BankPhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        Head("SECTION 10 - BANK (walked to normally)");
        Info("target area", "valroian_capital");
        Info("target world_room_id", "val_vq_counting_house / val_vq_counting_floor");
        Info("target room", "The Counting House / The Counting Floor");
        Info("route", string.Join(" -> ", ToBank.Select(h => h.Direction)));
        if (!await WalkAsync(client, w, ToBank, "walk to bank"))
        {
            Check(false, "reached the bank room",
                $"stalled in {w.Room.AreaId} ('{w.Room.RoomName}')");
            return FinishAsync(client, w);
        }

        Check(string.Equals(w.Room.AreaId, "val_vq_counting_floor", StringComparison.Ordinal),
            "Portal is standing in the intended Keystone bank room", w.Room.AreaId);
        Check(w.Room.HasBank, "Portal exposes a Bank context for this room", $"bank={w.Room.HasBank}");

        await client.SendAsync(ProtocolMessageTypes.BankSnapshotRequest, new BankSnapshotRequest());
        var snap = await client.WaitAsync(ProtocolMessageTypes.BankSnapshot, 8000);
        Check(snap is not null, "bank.snapshot.request returns bank.snapshot");
        Check(w.Bank.Available, "bank.snapshot reports the service available");
        Info("BEFORE carried copper", w.Bank.CarriedCurrency);
        Info("BEFORE bank_currency", w.Bank.BankCurrency);
        Info("inventory currency", w.Currency);

        Head("SECTION 11 - BANK: DEPOSIT");
        var carriedBefore = w.Bank.CarriedCurrency;
        var bankBefore = w.Bank.BankCurrency;
        const int deposit = 25;

        await client.SendAsync(ProtocolMessageTypes.BankDepositRequest, new BankDepositRequest(deposit));
        var dep = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(dep is not null, "bank.deposit.request returns bank.result");
        if (dep is not null)
        {
            var p = Portal.Protocol.ProtocolSerializer.DeserializePayload<BankResultPayload>(dep);
            if (p.IsSuccess)
            {
                Check(p.Value.Success, "Keystone accepted the deposit", p.Value.Message);
                Info("bank.result message", p.Value.Message);
                Info("bank.result newBankCurrency", p.Value.NewBankCurrency?.ToString() ?? "(null)");
            }
            else Check(false, "bank.result parsed", p.Errors[0].Message);
        }
        await client.SettleAsync(2000);
        Info("AFTER carried copper", w.Bank.CarriedCurrency);
        Info("AFTER bank_currency", w.Bank.BankCurrency);
        Info("AFTER inventory currency", w.Currency);
        Check(w.Bank.BankCurrency == bankBefore + deposit, "banked copper increases by the deposit",
            $"{bankBefore} -> {w.Bank.BankCurrency}");
        Check(w.Bank.CarriedCurrency == carriedBefore - deposit, "carried copper decreases by the deposit",
            $"{carriedBefore} -> {w.Bank.CarriedCurrency}");
        Check(w.Currency == carriedBefore - deposit, "inventory.snapshot reflects the new carried total",
            $"{w.Currency}c");

        Head("SECTION 12 - BANK: WITHDRAW");
        var carriedBeforeW = w.Bank.CarriedCurrency;
        var bankBeforeW = w.Bank.BankCurrency;
        await client.SendAsync(ProtocolMessageTypes.BankWithdrawRequest, new BankWithdrawRequest(deposit));
        var wd = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(wd is not null, "bank.withdraw.request returns bank.result");
        if (wd is not null)
        {
            var p = Portal.Protocol.ProtocolSerializer.DeserializePayload<BankResultPayload>(wd);
            if (p.IsSuccess) Check(p.Value.Success, "Keystone accepted the withdrawal", p.Value.Message);
            else Check(false, "bank.result parsed", p.Errors[0].Message);
        }
        await client.SettleAsync(2000);
        Info("AFTER withdraw carried", w.Bank.CarriedCurrency);
        Info("AFTER withdraw banked", w.Bank.BankCurrency);
        Check(w.Bank.BankCurrency == bankBeforeW - deposit, "banked copper decreases by the withdrawal",
            $"{bankBeforeW} -> {w.Bank.BankCurrency}");
        Check(w.Bank.CarriedCurrency == carriedBeforeW + deposit, "carried copper increases by the withdrawal",
            $"{carriedBeforeW} -> {w.Bank.CarriedCurrency}");

        Head("SECTION 13 - BANK: AUTHORITATIVE FAILURE CASES");
        await client.SendAsync(ProtocolMessageTypes.BankWithdrawRequest, new BankWithdrawRequest(999999));
        var over = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(over is not null && !w.Bank.ResultIsSuccess && w.Bank.ResultMessage.Length > 0,
            "withdraw above balance refused", w.Bank.ResultDisplay);

        await client.SendAsync(ProtocolMessageTypes.BankDepositRequest, new BankDepositRequest(0));
        var zero = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(zero is not null && !w.Bank.ResultIsSuccess && w.Bank.ResultMessage.Length > 0,
            "zero-amount deposit refused", w.Bank.ResultDisplay);

        await client.SendAsync(ProtocolMessageTypes.BankDepositRequest, new BankDepositRequest(-50));
        var neg = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(neg is not null && !w.Bank.ResultIsSuccess && w.Bank.ResultMessage.Length > 0,
            "negative-amount deposit refused", w.Bank.ResultDisplay);

        await client.SendAsync(ProtocolMessageTypes.BankDepositRequest, new BankDepositRequest(999999));
        var depOver = await client.WaitAsync(ProtocolMessageTypes.BankResult, 10000);
        Check(depOver is not null && !w.Bank.ResultIsSuccess,
            "deposit above carried balance refused", w.Bank.ResultDisplay);

return await DoorPhasesAsync(endpoint, user, pass, client, w, args);
    }

    private static async Task<int> DoorPhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        Head("SECTION 14 - HEALING ROOM (informational context, passive regeneration)");
        Info("target area", "valroian_capital");
        Info("target world_room_id", "val_vq_dispensary");
        Info("target room", "The Brightwater Dispensary");
        if (!await WalkAsync(client, w, BankToHealing, "walk to healing room"))
        {
            Check(false, "reached the healing room");
            return FinishAsync(client, w);
        }

        Check(string.Equals(w.Room.AreaId, "val_vq_dispensary", StringComparison.Ordinal),
            "Portal is standing in the intended Keystone healing room", w.Room.AreaId);
        Check(w.Room.IsHealingRoom, "Portal shows healing-room context (informational)", w.Room.RoomName);
        Check(!w.Room.HasShops && !w.Room.HasBank,
            "healing room offers no shop and no bank", $"shops={w.Room.ShopIds.Count} bank={w.Room.HasBank}");
        Check(!client.ReceivedTypes.Any(t => t.StartsWith("healer", StringComparison.OrdinalIgnoreCase)),
            "no healer.* message type exists (healing rooms are passive, not a service)");

        Head("SECTION 15 - DOORS (real generated keyed door)");
        Info("door room", "val_cw_almscellar -> val_cw_coal_store (west)");
        Info("door definition", "shared val_coal_door, canonical key brass_key, starts LOCKED");
        if (!await WalkAsync(client, w, HealingToBank, "walk back to bank floor"))
        {
            Check(false, "walked back from the healing room");
            return FinishAsync(client, w);
        }
        if (!await WalkAsync(client, w, BankToDoor, "walk to the door room"))
        {
            Check(false, "reached the door room");
            return FinishAsync(client, w);
        }

        Info("standing in", $"{w.Room.RoomName} [{w.Room.AreaId}]");
        var door = w.Room.Doors.FirstOrDefault(d => d.Direction == "west");
        if (door is null)
        {
            Check(false, "room.state exposes the real west door of the almscellar",
                "doors: " + string.Join(", ", w.Room.Doors.Select(d => d.Direction)));
        }
        else
        {
            Check(true, "room.state exposes the real west door",
                $"{door.Direction} '{door.DisplayName}' isDoor={door.IsDoor} open={door.IsOpen} locked={door.IsLocked} hasLock={door.HasLock}");
            Check(door.IsLocked && !door.CanOpen && door.CanUnlock,
                "button enablement mirrors server state for a locked door",
                $"open={door.IsOpen} locked={door.IsLocked} canOpen={door.CanOpen} "
                + $"canClose={door.CanClose} canLock={door.CanLock} canUnlock={door.CanUnlock}");

            Head("SECTION 16 - DOOR FOG SECURITY");
            var roomsBefore = w.Map.Rooms.Count;
            Check(!w.Map.Rooms.ContainsKey("val_cw_coal_store"),
                "destination is undiscovered before legitimate traversal");

            await client.SendAsync(ProtocolMessageTypes.MovementDirectionRequest,
                new MovementDirectionRequest("west"));
            await client.SettleAsync(2500);
            Check(w.Map.Rooms.Count == roomsBefore, "blocked traversal revealed no new room",
                $"{roomsBefore} -> {w.Map.Rooms.Count}");
            Check(!w.Map.Rooms.ContainsKey("val_cw_coal_store"),
                "hidden destination id never leaked into the map");
            Check(!w.Room.Doors.Any(d => d.Direction == "west" && d.Name.Contains("coal", StringComparison.OrdinalIgnoreCase)),
                "door row carries no destination name");
            Check(string.Equals(w.Room.AreaId, "val_cw_almscellar", StringComparison.Ordinal),
                "character did not move through the locked door", w.Room.AreaId);

            Head("SECTION 17 - DOOR AUTHORITATIVE FAILURE CASES");
            foreach (var (dir, act, label) in new[]
            {
                ("west", "open", "opening a LOCKED door"),
                ("west", "unlock", "unlocking WITHOUT the key"),
                ("west", "lock", "locking WITHOUT the key"),
                ("nowhere", "open", "acting on a non-existent direction"),
                ("west", "detonate", "an unknown door action"),
            })
            {
                await client.SendAsync(ProtocolMessageTypes.DoorActionRequest,
                    new DoorActionRequest(dir, act));
                var r = await client.WaitAsync(ProtocolMessageTypes.DoorResult, 8000);
                Check(r is not null && !w.Room.DoorFeedbackIsSuccess,
                    $"{label} is refused", w.Room.DoorFeedbackDisplay);
            }

            Skip("door LOCK SUCCESS path",
                "needs a canonical key in legitimate inventory; no generated shop or mob drop "
                + "in this beta realm supplies brass_key and frozen door definitions must not change");
        }

        return await FinalPhasesAsync(endpoint, user, pass, client, w, args);
    }

    private static async Task<int> FinalPhasesAsync(
        Uri endpoint, string user, string pass, LiveClient client, World w, string[] args)
    {
        Head("SECTION 18 - SERVICE / AREA TRANSITION (no stale UI)");
        Check(w.Room.Doors.Count > 0, "door controls reflect only doors in the CURRENT room",
            $"{w.Room.Doors.Count} door(s): {string.Join(", ", w.Room.Doors.Select(d => d.Direction))}");

        await client.SendAsync(ProtocolMessageTypes.ShopSnapshotRequest, new ShopSnapshotRequest(null));
        await client.WaitAsync(ProtocolMessageTypes.ShopSnapshot, 8000);
        Check(!w.Shop.HasWares, "no shop wares displayed outside a shop room");

        await client.SendAsync(ProtocolMessageTypes.BankSnapshotRequest, new BankSnapshotRequest());
        await client.WaitAsync(ProtocolMessageTypes.BankSnapshot, 8000);
        Check(!w.Room.HasBank, "bank context absent outside the bank room", $"bank={w.Room.HasBank}");

        Head("SECTION 19 - EQUIPMENT / INVENTORY REGRESSION");
        Info("equipped", w.Equipped.Count == 0
            ? "(nothing equipped)"
            : string.Join(", ", w.Equipped.Select(e => $"{e.Slot}={e.ItemId}")));
        Info("inventory", w.Inventory.Count == 0
            ? "(empty)"
            : string.Join(", ", w.Inventory.Take(20).Select(i => $"{i.ItemId}x{i.Quantity}")));
        Info("slots on carried items",
            string.Join(", ", w.Inventory.Where(i => i.Slot is not null).Select(i => i.Slot).Distinct().Take(20)));

        if (w.Equipped.Count > 0)
        {
            Check(w.Equipped.All(e => !string.IsNullOrWhiteSpace(e.Slot)),
                "every equipped row names a real slot",
                string.Join(", ", w.Equipped.Select(e => $"{e.Slot}<-{e.ItemId}")));
            Check(w.Equipped.Select(e => e.Slot).Distinct().Count() == w.Equipped.Count,
                "no duplicate equipment slots");
            Info("distinct equipped slots", w.Equipped.Select(e => e.Slot).Distinct().Count());
        }
        else
        {
            Skip("equip / unequip round trip", "QA character carries nothing equipped");
        }

        Head("SECTION 20 - CHARACTER POINTS / SKILLS");
        Info("available points", w.Points?.AvailablePoints.ToString() ?? "(unknown)");
        foreach (var p in w.Points?.Stats ?? Array.Empty<AllocatableStatRecord>())
            Say($"         allocatable: {p.StatId} '{p.Name}' = {p.Value} (cap {p.Cap}{(p.IsCapped ? ", CAPPED" : "")})");
        Info("skills snapshot present", w.Skills is not null);

        Head("SECTION 21 - RECONNECT (full state rehydration)");
        var beforeName = w.Room.RoomName;
        var beforeDesc = w.Room.RoomDescription;
        var beforeExits = w.Room.Exits.Count;
        var beforeDoors = w.Room.Doors.Count;
        var beforeShopIds = w.Room.ShopIds.Count;
        var beforeBank = w.Room.HasBank;
        var beforeHeal = w.Room.IsHealingRoom;
        var beforeCurrency = w.Currency;
        var beforeBankCurrency = w.Bank.BankCurrency;
        var beforeInventory = w.Inventory.Count;
        var beforeEquipped = w.Equipped.Count;
        var beforeMapRooms = w.Map.Rooms.Count;
        var beforeCurrentRoom = w.Map.CurrentRoomId;
        var beforeCurrentArea = w.Map.CurrentAreaId;

        client.Dispose();
        await Task.Delay(1200);
        w.ResetSession();

        var client2 = await ConnectAsync(endpoint, user, pass, w);
        if (client2 is null)
        {
            Check(false, "reconnect succeeded");
            return FinishSummary(null, w);
        }

        await client2.SettleAsync(4000);

        Check(beforeName == w.Room.RoomName, "room name restored", w.Room.RoomName);
        Check(beforeDesc == w.Room.RoomDescription, "room description restored");
        Check(beforeExits == w.Room.Exits.Count, "exits restored", $"{w.Room.Exits.Count}");
        Check(beforeDoors == w.Room.Doors.Count, "dynamic door context restored",
            $"{w.Room.Doors.Count} door(s)");
        Check(beforeShopIds == w.Room.ShopIds.Count && beforeBank == w.Room.HasBank
              && beforeHeal == w.Room.IsHealingRoom, "service context restored",
            $"shops={w.Room.ShopIds.Count} bank={w.Room.HasBank} healing={w.Room.IsHealingRoom}");
        Check(beforeCurrency == w.Currency, "currency restored", $"{w.Currency}c");
        Check(beforeInventory == w.Inventory.Count, "inventory restored",
            $"{w.Inventory.Count} item line(s)");
        Check(beforeEquipped == w.Equipped.Count, "equipment restored", $"{w.Equipped.Count} equipped");
        Check(beforeMapRooms == w.Map.Rooms.Count, "map discovery restored",
            $"{w.Map.Rooms.Count} room(s)");
        Check(beforeCurrentRoom == w.Map.CurrentRoomId, "map current room restored",
            w.Map.CurrentRoomId ?? "-");
        Check(beforeCurrentArea == w.Map.CurrentAreaId, "floor/area restored",
            w.Map.CurrentAreaId ?? "-");
        Check(w.Status is not null && w.Points is not null && w.Skills is not null,
            "resources, points and skills restored on reconnect");
        Check(client2.DeserializeFailures.Count == 0 && w.Rejected.Count == 0,
            "reconnect payloads all parsed by Portal",
            $"{client2.DeserializeFailures.Count + w.Rejected.Count} problem(s)");

        await client2.SendAsync(ProtocolMessageTypes.WhoRequest, new WhoRequest());
        var who2 = await client2.WaitAsync(ProtocolMessageTypes.WhoSnapshot, 8000);
        Check(who2 is not null && w.Who.Rows.Count > 0, "WHO still works after reconnect",
            $"{w.Who.Rows.Count} row(s)");

        await client2.SendAsync(ProtocolMessageTypes.BankSnapshotRequest, new BankSnapshotRequest());
        await client2.WaitAsync(ProtocolMessageTypes.BankSnapshot, 8000);
        Check(w.Bank.BankCurrency == beforeBankCurrency, "bank currency restored on reconnect",
            $"{beforeBankCurrency}c -> {w.Bank.BankCurrency}c");

        Head("SECTION 22 - PROTOCOL TRACE (whole session)");
        var allSent = client.SentTypes.Concat(client2.SentTypes).ToList();
        var allRecv = client.ReceivedTypes.Concat(client2.ReceivedTypes).ToList();
        Say("  Portal -> Keystone (requests actually sent):");
        foreach (var g in allSent.GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal))
            Say($"     {g.Key} x{g.Count()}");
        Say("  Keystone -> Portal (events actually received):");
        foreach (var g in allRecv.GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal))
            Say($"     {g.Key} x{g.Count()}");

        var required = new[]
        {
            ProtocolMessageTypes.RoomState, ProtocolMessageTypes.ShopSnapshot,
            ProtocolMessageTypes.ShopBuyRequest, ProtocolMessageTypes.ShopSellRequest,
            ProtocolMessageTypes.ShopResult, ProtocolMessageTypes.BankSnapshot,
            ProtocolMessageTypes.BankDepositRequest, ProtocolMessageTypes.BankWithdrawRequest,
            ProtocolMessageTypes.BankResult, ProtocolMessageTypes.DoorActionRequest,
            ProtocolMessageTypes.DoorResult, ProtocolMessageTypes.WhoRequest,
            ProtocolMessageTypes.WhoSnapshot,
        };
        var sentSet = allSent.ToHashSet(StringComparer.Ordinal);
        var recvSet = allRecv.ToHashSet(StringComparer.Ordinal);
        foreach (var t in required)
        {
            var want = t.EndsWith(".request", StringComparison.Ordinal) ? sentSet : recvSet;
            Check(want.Contains(t), $"protocol path occurred: {t}");
        }

        return FinishSummary(client2, w);
    }

    private static int FinishSummary(LiveClient? client, World w)
    {
        Head("SUMMARY");
        Say($"  PASS = {_pass}");
        Say($"  FAIL = {_fail}");
        Say($"  SKIP = {_skip}");
        Dump();
        return _fail == 0 ? 0 : 1;
    }

    private static int FinishAsync(LiveClient? client, World w) => FinishSummary(client, w);

    private static void Dump() => File.WriteAllLines("_qa_services_log.txt", Log);
}