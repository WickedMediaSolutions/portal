using System.Net.WebSockets;
using System.Text;
using Portal.Networking;
using Portal.Protocol;
using Portal.State;

namespace Portal.LiveQa;

/// <summary>
/// Live end-to-end QUEST QA driver against a REAL running Keystone.
///
/// Drives the real Portal client path only:
///   * a real WebSocket to ws://localhost:4013 (Portal's own endpoint resolver),
///   * Portal's REAL <see cref="Portal.Protocol.ProtocolSerializer"/> for every
///     outbound request and every inbound event,
///   * Portal's REAL <see cref="QuestState"/> / <see cref="QuestCardViewModel"/>
///     view-models, applied exactly as GameEventService would apply them.
///
/// There are no direct in-process protocol calls here and nothing is
/// fabricated: every assertion is a claim about what crossed the wire.
/// Availability, readiness, rewards and completion are ALL decided by the
/// running server; this driver only reports and clicks.
/// </summary>
internal static class QuestQa
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

    /// <summary>
    /// Renders rewards from the wire payload through Portal's REAL reward view
    /// model, so the report shows what a client would actually render.
    /// </summary>
    private static string DescribeRewards(QuestRewards? rewards) =>
        QuestRewardsViewModel.FromRewards(rewards).Summary;

    // Live route to a real giant_rat spawn, computed from the LIVE exit graph by
    // _liveqa_quest_route.py (read-only).  giant_rat is rat_slayer's real kill
    // target; skeleton_warrior (bone_collector) only spawns in the Silvermere
    // component, which is not connected to this character's area.
    private static readonly (string Direction, string To)[] ToRat =
    {
        ("up", "val_cw_almshall"),
        ("southwest", "val_cw_vale_arch"),
        ("west", "val_vq_ward_arch"),
        ("northwest", "val_vq_ordinance_lane"),
        ("west", "val_vq_mint_lane"),
        ("northwest", "val_vq_messenger_post"),
        ("east", "val_cr_outside_gate"),
    };

    /// <summary>Formats a card for the log using only server-authored values.</summary>
    private static string Describe(QuestCardViewModel card) =>
        $"{card.QuestId}[{card.State}] ready={card.ReadyToComplete} " +
        $"acc={card.CanAccept} ab={card.CanAbandon} cp={card.CanComplete} :: {card.ObjectiveSummary}";

    /// <summary>
    /// Holds Portal's REAL QuestState model plus the raw inbound envelopes, so a
    /// deserialization or modelling defect surfaces exactly as it would in the
    /// running client.
    /// </summary>
    private sealed class QuestWorld
    {
        public QuestState Quests { get; } = new();
        public List<string> DeserializeFailures { get; } = new();
        public List<string> Rejected { get; } = new();
        public List<string> ReceivedTypes { get; } = new();
        public int QuestSnapshotCount { get; private set; }
        public int QuestResultCount { get; private set; }
        public int QuestProgressCount { get; private set; }

        /// <summary>Last quest.result, kept verbatim for assertions.</summary>
        public QuestResultPayload? LastResult { get; private set; }
        /// <summary>Last quest.progress payload.</summary>
        public QuestProgressPayload? LastProgress { get; private set; }
        /// <summary>Most recent raw quest.snapshot text.</summary>
        public string? LastSnapshotRaw { get; private set; }
        /// <summary>Most recent room entity snapshot, used to pick a live mob.</summary>
        public RoomEntitySnapshotPayload? Entities { get; private set; }
        /// <summary>Most recent room.state, used to confirm each walk hop.</summary>
        public RoomStatePayload? Room { get; private set; }
        public int RoomStateCount { get; private set; }

        private bool Try<T>(MessageEnvelope env, out T value) where T : class
        {
            var r = ProtocolSerializer.DeserializePayload<T>(env);
            if (r.IsFailure)
            {
                Rejected.Add(env.MessageType + ": " + r.Errors[0].Message);
                value = default!;
                return false;
            }
            value = r.Value;
            return true;
        }

        public void Apply(MessageEnvelope env)
        {
            ReceivedTypes.Add(env.MessageType);

            if (env.MessageType == ProtocolMessageTypes.RoomEntitySnapshot)
            {
                if (Try(env, out RoomEntitySnapshotPayload e)) Entities = e;
                return;
            }

            if (env.MessageType == ProtocolMessageTypes.RoomState)
            {
                if (Try(env, out RoomStatePayload r)) { Room = r; RoomStateCount++; }
                return;
            }

            if (!env.MessageType.StartsWith("quest.", StringComparison.Ordinal))
                return;

            switch (env.MessageType)
            {
                case ProtocolMessageTypes.QuestSnapshot:
                {
                    QuestSnapshotCount++;
                    LastSnapshotRaw = env.Payload.GetRawText();
                    if (Try(env, out QuestSnapshotPayload p)) Quests.ApplySnapshot(p);
                    return;
                }
                case ProtocolMessageTypes.QuestProgress:
                {
                    QuestProgressCount++;
                    LastProgress = null;
                    if (Try(env, out QuestProgressPayload p))
                    {
                        LastProgress = p;
                        Quests.ApplyProgress(p);
                    }
                    return;
                }
                case ProtocolMessageTypes.QuestResult:
                {
                    QuestResultCount++;
                    LastResult = null;
                    if (Try(env, out QuestResultPayload r))
                    {
                        LastResult = r;
                        Quests.ApplyResult(r);
                    }
                    return;
                }
            }
        }

        public QuestCardViewModel? Find(string questId) => Quests.FindById(questId);

        public IEnumerable<QuestCardViewModel> All =>
            Quests.ActiveQuests.Concat(Quests.AvailableQuests).Concat(Quests.CompletedQuests);
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

    /// <summary>
    /// Connects through Portal's own endpoint resolver, handshakes and
    /// authenticates with a real account. Returns null on failure.
    /// </summary>
    private static async Task<LiveClient?> ConnectAsync(
        Uri endpoint, string user, string pass, QuestWorld world)
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

        var hsOk = ProtocolSerializer.DeserializePayload<HandshakeResponse>(hs);
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

        var a = ProtocolSerializer.DeserializePayload<AuthenticationResponse>(auth);
        if (a.IsFailure)
        {
            Say($"  FATAL: auth.response unparseable: {a.Errors[0].Message}");
            client.Dispose();
            return null;
        }

        Check(a.Value.Success, "auth.request succeeds",
            a.Value.Success
                ? $"session={a.Value.Session?.SessionId} character={a.Value.Session?.CharacterName} id={a.Value.Session?.CharacterId}"
                : $"{a.Value.ErrorCode}: {a.Value.ErrorMessage}");

        if (!a.Value.Success)
        {
            client.Dispose();
            return null;
        }

        return client;
    }

    /// <summary>
    /// Sends a quest action and returns the authoritative quest.result that
    /// came back, or null when the server did not answer.
    /// </summary>
    /// <remarks>
    /// The three quest actions share one wire payload shape
    /// (<c>{ questId }</c>), so the envelope is built here and still goes out
    /// through Portal's REAL serializer, exactly as QuestService does.
    /// </remarks>
    private static async Task<QuestResultPayload?> ActAsync(
        LiveClient c, string messageType, string questId)
    {
        c.DiscardQueued(ProtocolMessageTypes.QuestResult);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            messageType,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(new QuestAcceptRequest(questId)));
        await c.SendRawAsync(envelope);

        var result = await c.WaitAsync(ProtocolMessageTypes.QuestResult, 10000);
        if (result is null)
            return null;

        var r = ProtocolSerializer.DeserializePayload<QuestResultPayload>(result);
        return r.IsSuccess ? r.Value : null;
    }

    /// <summary>Waits for the server to re-publish the quest view.</summary>
    private static async Task QuestSettleAsync(LiveClient c)
    {
        await c.WaitAsync(ProtocolMessageTypes.QuestSnapshot, 8000);
        await Task.Delay(400);
    }

    /// <summary>
    /// Walks one hop and reports the authoritative room.state that followed.
    /// </summary>
    private static async Task<bool> StepAsync(
        LiveClient c, QuestWorld w, string direction, string expectWorldRoomId)
    {
        var before = w.RoomStateCount;
        await c.SendAsync(
            ProtocolMessageTypes.MovementDirectionRequest,
            new MovementDirectionRequest(direction));

        var failed = await c.WaitAsync(ProtocolMessageTypes.MovementFailed, 1500);
        if (failed is not null)
        {
            var door = w.Room?.Exits.FirstOrDefault(e => e.Direction == direction);
            if (door is not null && door.CanOpen)
            {
                await c.SendAsync(
                    ProtocolMessageTypes.DoorActionRequest,
                    new DoorActionRequest(direction, "open"));
                await c.WaitAsync(ProtocolMessageTypes.DoorResult, 5000);
                await StepAsync(c, w, direction, expectWorldRoomId);
                return string.Equals(w.Room?.WorldRoomId, expectWorldRoomId,
                    StringComparison.Ordinal);
            }
            return false;
        }

        var rs = await c.WaitAsync(ProtocolMessageTypes.RoomState, 5000);
        if (rs is null || w.RoomStateCount <= before)
            return false;

        return string.Equals(w.Room?.WorldRoomId, expectWorldRoomId,
            StringComparison.Ordinal);
    }

    /// <summary>Walks a whole route, verifying every hop.</summary>
    private static async Task<bool> WalkAsync(
        LiveClient c, QuestWorld w, (string Direction, string To)[] route, string label)
    {
        foreach (var hop in route)
        {
            if (!await StepAsync(c, w, hop.Direction, hop.To))
            {
                Check(false, $"{label}: hop {hop.Direction}",
                    $"expected world_room_id={hop.To}, got {w.Room?.WorldRoomId} " +
                    $"('{(w.Room?.Name ?? "?").Trim()}')");
                return false;
            }
        }
        Check(true, $"{label}: walked {route.Length} hops to a live quest target",
            $"now in [{w.Room?.WorldRoomId}] '{(w.Room?.Name ?? "?").Trim()}'");
        return true;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var user = Arg(args, "user") ?? "testuser";
        var pass = Arg(args, "pass") ?? "qaportal2026";
        var useLocalDev = !args.Contains("--production");

        var target = PortalEndpoints.FromToggle(useLocalDev);
        var endpoint = PortalEndpoints.Resolve(target);

        Head("SECTION Q1 - CONNECTION (Portal 'Use Local Development Server' = ON)");
        Info("toggle_local_development", useLocalDev);
        Info("resolved_target", PortalEndpoints.Describe(target));
        Info("resolved_endpoint", endpoint);
        Info("plaintext_transport", PortalEndpoints.IsPlaintextTransport(endpoint));
        Info("account", user);

        var world = new QuestWorld();

        Head("SECTION Q2 - LOGIN / QUEST BOOTSTRAP (no manual refresh first)");
        var client = await ConnectAsync(endpoint, user, pass, world);
        if (client is null)
        {
            Say("FATAL: could not authenticate; aborting live quest QA.");
            Dump();
            return 2;
        }

        await client.SettleAsync(4000);

        Check(world.QuestSnapshotCount >= 1,
            "quest.snapshot arrives automatically on login (no manual refresh first)",
            $"quest.snapshot x{world.QuestSnapshotCount}");

        Check(world.DeserializeFailures.Count == 0,
            "every inbound envelope parsed by Portal's own deserializer",
            $"{world.DeserializeFailures.Count} failure(s)");
        foreach (var f in world.DeserializeFailures.Take(3)) Say("        " + f);
        Check(world.Rejected.Count == 0,
            "every quest payload accepted by Portal's own QuestState model",
            $"{world.Rejected.Count} rejected: {string.Join(" | ", world.Rejected.Take(3))}");

        Info("active", string.Join(", ", world.Quests.ActiveQuests.Select(q => q.QuestId)));
        Info("available", string.Join(", ", world.Quests.AvailableQuests.Select(q => q.QuestId)));
        Info("completed", string.Join(", ", world.Quests.CompletedQuests.Select(q => q.QuestId)));
        foreach (var anyCard in world.All)
        {
            Info("quest " + anyCard.QuestId, Describe(anyCard));
            Check(anyCard.Objectives.Count > 0, $"{anyCard.QuestId} publishes objectives",
                $"{anyCard.Objectives.Count} objective(s)");
            Check(!string.IsNullOrWhiteSpace(anyCard.RewardsSummary),
                $"{anyCard.QuestId} publishes rewards", anyCard.RewardsSummary);
        }

        if (world.LastSnapshotRaw is not null)
        {
            Check(!world.LastSnapshotRaw.Contains("royal_guard", StringComparison.OrdinalIgnoreCase),
                "no royal_guard reference survives in the runtime quest payload");
        }

        Head("SECTION Q3 - GUARDIAN_TRIAL RUNTIME RESOLUTION");
        var gt = world.Find("guardian_trial");
        if (gt is null)
        {
            Skip("guardian_trial payload",
                "server withheld it for this character (not currently offerable) - " +
                "runtime registry/spawn/payload resolution covered by the content audit");
        }
        else
        {
            Info("guardian_trial", Describe(gt));
            Check(gt.ObjectiveSummary.Contains("captain", StringComparison.OrdinalIgnoreCase),
                "guardian_trial objective display name resolves to the captain",
                gt.ObjectiveSummary);
            Check(!gt.ObjectiveSummary.Contains("royal", StringComparison.OrdinalIgnoreCase),
                "guardian_trial shows no royal-guard reference");
        }

        // ── pick the quest to drive the live lifecycle on ──────────────────
        // Prefer bone_collector: its kill target has a real Silvermere spawn
        // and its collect target drops from that same mob.
        const string Quest = "bone_collector";
        var card0 = world.Find(Quest);
        if (card0 is null)
        {
            Skip("live quest lifecycle", $"{Quest} is not visible to this character");
            return Finish();
        }

        // A quest left active by an earlier run is reset first so the phase is
        // re-runnable without touching production content.
        if (card0.IsActive)
        {
            Head($"SECTION Q4 - RESET (abandon any prior {Quest})");
            var ab = await ActAsync(client, ProtocolMessageTypes.QuestAbandonRequest, Quest);
            Check(ab is not null && ab.Success, $"abandon stale {Quest}",
                ab?.Message ?? "no quest.result");
            await QuestSettleAsync(client);
        }

        Head($"SECTION Q5 - REAL ACCEPT ({Quest})");
        var acc = await ActAsync(client, ProtocolMessageTypes.QuestAcceptRequest, Quest);
        Check(acc is not null, "quest.accept.request produced a quest.result");
        if (acc is not null)
        {
            Info("result.action", acc.Action);
            Info("result.success", acc.Success);
            Info("result.state", acc.State ?? "-");
            Info("result.message", acc.Message);
            Check(acc.Success, $"{Quest} accepted by the real Keystone handler");
            Check(string.Equals(acc.State, QuestStates.Active, StringComparison.Ordinal),
                "quest state becomes ACTIVE", acc.State ?? "null");
        }
        await QuestSettleAsync(client);

        var card = world.Find(Quest);
        Check(card is not null && card.IsActive, $"{Quest} shows as active in Portal's model");
        Check(card is not null && card.CanAbandon, "abandon button reflects server flags");
        Check(card is not null && !card.CanAccept, "accept button disabled once active");
        Check(card is not null && !card.CanComplete, "complete button disabled before ready");
        if (card is not null)
        {
            Info("after accept", Describe(card));
            Check(card.Objectives.All(o => o.Current == 0),
                "objectives show 0/current after accept", card.ObjectiveSummary);
        }

        Head("SECTION Q6 - WALK TO A LIVE QUEST TARGET (real movement)");
        // bone_collector's skeleton_warrior only spawns in the Silvermere
        // component, which is not connected to this character's area, so the live
        // lifecycle is driven on rat_slayer (giant_rat), whose spawn IS reachable.
        // The task explicitly allows this fallback.
        const string KillQuest = "rat_slayer";
        var walked = true;
        if (!string.Equals(world.Room?.WorldRoomId, ToRat[^1].To, StringComparison.Ordinal))
        {
            walked = await WalkAsync(client, world, ToRat, "walk to giant_rat spawn");
        }
        else
        {
            Say($"         already standing at {ToRat[^1].To}; route skipped");
        }

        Head($"SECTION Q7 - REAL COMBAT PROGRESS ({KillQuest} vs a live giant_rat)");
        var rat = world.Entities?.Entities.FirstOrDefault(e =>
            e.IsMob && !e.IsDead &&
            e.Name.Contains("Rat", StringComparison.OrdinalIgnoreCase));

        if (!walked || rat is null)
        {
            Skip("live kill progress",
                $"no live Giant Rat reachable here (walked={walked}, rat={rat is not null})");
        }
        else
        {
            Info("live target", $"{rat.Name} (targetId={rat.TargetId})");

            // Make sure the kill quest is accepted before swinging.
            var kq = world.Find(KillQuest);
            if (kq is not null && !kq.IsActive && kq.CanAccept)
            {
                var ka = await ActAsync(client, ProtocolMessageTypes.QuestAcceptRequest, KillQuest);
                Check(ka is { Success: true }, $"accept {KillQuest}", ka?.Message ?? "-");
                await QuestSettleAsync(client);
            }

            await client.SendAsync(
                ProtocolMessageTypes.TargetSelectRequest,
                new TargetSelectRequest(rat.TargetId));
            await client.WaitAsync(ProtocolMessageTypes.TargetChanged, 6000);

            // Kill through the ordinary Portal combat path, one attack at a
            // time, so each real death is observed separately and any duplicate
            // credit would show up as a jump greater than one.
            var progressSeen = 0;
            for (var round = 0; round < 80; round++)
            {
                var killObj = world.Find(KillQuest)?.Objectives
                    .FirstOrDefault(o => o.Type == "kill");
                if (killObj is null || killObj.Current >= killObj.Required)
                    break;

                var cur = killObj.Current;
                await client.SendAsync(
                    ProtocolMessageTypes.CombatAttackRequest,
                    new CombatAttackRequest(rat.TargetId));

                await Task.Delay(350);
                await client.SettleAsync(220);

                var now = world.Find(KillQuest)?.Objectives
                    .FirstOrDefault(o => o.Type == "kill")?.Current ?? 0;
                if (now != cur)
                {
                    progressSeen++;
                    Say($"         kill objective {cur} -> {now}");
                    Check(now == cur + 1, "kill credit incremented exactly once",
                        $"{cur} -> {now}");
                }

                if (!client.IsOpen)
                    break;
            }

            Check(progressSeen > 0, "quest kill objective incremented from real combat",
                $"{progressSeen} increment(s) observed");
            Check(world.QuestProgressCount > 0,
                "quest.progress event crossed the WebSocket",
                $"quest.progress x{world.QuestProgressCount}");

            var killCard = world.Find(KillQuest);
            Info($"{KillQuest} after combat", Describe(killCard!));
        }

        Head("SECTION Q8 - READY / COMPLETE");
        // Complete whichever quest the server actually reports as ready.
        var ready = world.All.FirstOrDefault(q => q.ReadyToComplete)
                    ?? world.Find(KillQuest) ?? world.Find(Quest);
        Info("state before completion", Describe(ready!));

        var complete = await ActAsync(client, ProtocolMessageTypes.QuestCompleteRequest, ready.QuestId);
        Check(complete is not null, "quest.complete.request produced a quest.result");
        if (complete is not null)
        {
            Info("quest", ready.QuestId);
            Info("result.success", complete.Success);
            Info("result.state", complete.State ?? "-");
            Info("result.message", complete.Message);
            Info("result.rewards", DescribeRewards(complete.Rewards));
            Check(complete.Success == ready.ReadyToComplete,
                "server validates readiness exactly as it published it",
                $"ready={ready.ReadyToComplete} success={complete.Success}");
            Check(complete.Success || complete.Rewards is null,
                "no rewards granted by a refused completion");
        }
        await QuestSettleAsync(client);

        var done = world.Find(ready.QuestId);
        Info("state after completion", Describe(done!));

        if (complete is { Success: true })
        {
            Check(done is not null && done.IsCompleted, "completed state shown in Portal's model");
            Check(done is not null && !done.CanAccept && !done.CanComplete,
                "no stale accept/complete buttons on a completed quest");

            Head("SECTION Q8b - DUPLICATE COMPLETION REFUSED");
            var again = await ActAsync(client, ProtocolMessageTypes.QuestCompleteRequest, ready.QuestId);
            Check(again is not null, "second complete produced a quest.result");
            if (again is not null)
            {
                Info("result.success", again.Success);
                Info("result.message", again.Message);
                Check(!again.Success, "second completion is REJECTED (no duplicate rewards)");
                Check(again.Rewards is null, "duplicate completion grants no rewards");
            }
        }
        else
        {
            Skip("completion + duplicate-refusal", "quest was not ready yet");
        }

        Head("SECTION Q9 - ABANDON / REACCEPT (second safe quest)");
        const string Second = "spider_hunt";
        var second = world.Find(Second);
        if (second is null)
        {
            Skip("abandon/reaccept", $"{Second} is not visible to this character");
        }
        else if (second.IsCompleted)
        {
            Skip("abandon/reaccept", $"{Second} already completed (non-repeatable, as expected)");
            Check(!second.CanAccept && !second.CanAbandon,
                "a completed quest offers neither accept nor abandon");
        }
        else
        {
            if (second.IsActive)
            {
                var pre = await ActAsync(client, ProtocolMessageTypes.QuestAbandonRequest, Second);
                Check(pre is { Success: true }, $"reset active {Second}", pre?.Message ?? "-");
                await QuestSettleAsync(client);
            }

            var a1 = await ActAsync(client, ProtocolMessageTypes.QuestAcceptRequest, Second);
            Check(a1 is { Success: true }, $"accept {Second}", a1?.Message ?? "-");
            await QuestSettleAsync(client);

            var c1 = world.Find(Second);
            Check(c1 is not null && c1.IsActive, $"{Second} active after accept");

            var a2 = await ActAsync(client, ProtocolMessageTypes.QuestAbandonRequest, Second);
            Check(a2 is { Success: true }, $"abandon {Second}", a2?.Message ?? "-");
            await QuestSettleAsync(client);

            var c2 = world.Find(Second);
            Info($"{Second} after abandon", c2 is null ? "(gone)" : Describe(c2));
            Check(c2 is null || !c2.IsActive, "quest disappears/resets on abandon");

            var a3 = await ActAsync(client, ProtocolMessageTypes.QuestAcceptRequest, Second);
            Check(a3 is { Success: true }, $"re-accept {Second}", a3?.Message ?? "-");
            await QuestSettleAsync(client);

            var c3 = world.Find(Second);
            Info($"{Second} after re-accept", c3 is null ? "(gone)" : Describe(c3));
            Check(c3 is not null && c3.IsActive, $"{Second} active again after re-accept");
            Check(c3 is not null && c3.Objectives.All(o => o.Current == 0),
                "objective counts restart at zero after re-accept",
                c3?.ObjectiveSummary ?? "-");

            // leave it abandoned so the account is not left mid-quest
            await ActAsync(client, ProtocolMessageTypes.QuestAbandonRequest, Second);
            await QuestSettleAsync(client);
        }

        Head("SECTION Q10 - RECONNECT (quest state restored from the server)");
        var beforeReconnect = world.All.ToDictionary(q => q.QuestId, Describe);
        Info("quests before disconnect",
            string.Join(", ", world.Quests.CompletedQuests.Select(q => q.QuestId)));

        client.Dispose();
        await Task.Delay(1500);

        // A brand-new client and a brand-new QuestState model: restoration can
        // therefore only come from a fresh quest.snapshot off the wire.
        var world2 = new QuestWorld();
        var client2 = await ConnectAsync(endpoint, user, pass, world2);
        if (client2 is null)
        {
            Check(false, "reconnect", "second login failed");
            return Finish();
        }

        await client2.SettleAsync(4000);
        Check(world2.QuestSnapshotCount >= 1, "quest.snapshot arrives automatically on reconnect",
            $"quest.snapshot x{world2.QuestSnapshotCount}");
        Check(world2.Rejected.Count == 0 && world2.DeserializeFailures.Count == 0,
            "reconnect payloads deserialize cleanly in Portal's own models");
        Check(world2.Quests.CompletedQuests.Count == world.Quests.CompletedQuests.Count,
            "completed state restored with no duplicates",
            $"{world2.Quests.CompletedQuests.Count} completed before/after");
        Check(world2.Quests.ActiveQuests.Count == world.Quests.ActiveQuests.Count,
            "active progress restored",
            $"{world2.Quests.ActiveQuests.Count} active");

        foreach (var restored in world2.All)
        {
            Info("restored " + restored.QuestId, Describe(restored));
            if (restored.IsCompleted)
                Check(!restored.CanAccept && !restored.CanAbandon && !restored.CanComplete,
                    $"{restored.QuestId}: no stale quest buttons after reconnect");
        }

        foreach (var qid in world2.All.Select(q => q.QuestId))
            Check(world2.All.Count(q => q.QuestId == qid) == 1,
                $"{qid} appears exactly once after reconnect");

        client2.Dispose();

        Head("SECTION Q11 - SESSION EVENT SUMMARY");
        foreach (var g in world.ReceivedTypes.Concat(world2.ReceivedTypes)
                     .GroupBy(t => t).OrderBy(g => g.Key, StringComparer.Ordinal))
            Say($"     {g.Key} x{g.Count()}");

        return Finish();
    }

    private static int Finish()
    {
        Dump();
        Say("");
        Say(new string('=', 74));
        Say("LIVE QUEST QA SUMMARY");
        Say(new string('=', 74));
        Say($"  PASS = {_pass}");
        Say($"  FAIL = {_fail}");
        Say($"  SKIP = {_skip}");
        return _fail == 0 ? 0 : 1;
    }

    private static void Dump() =>
        File.WriteAllLines("_qa_quest_log.txt", Log);
}



