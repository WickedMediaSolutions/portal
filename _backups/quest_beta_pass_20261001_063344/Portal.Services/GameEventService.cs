using System.Linq;
using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;
using Portal.State;

namespace Portal.Services;

/// <summary>
/// Post-authentication background event receive loop.
/// 
/// Consumes unsolicited server-pushed <see cref="MessageEnvelope"/> messages
/// from the existing <see cref="IWebSocketConnection"/>, routes them to the
/// appropriate handler, and applies mutations to bound UI state on the WPF
/// Dispatcher thread.
///
/// <para>Lifecycle:</para>
/// <list type="bullet">
/// <item>Created once, wired alongside <see cref="IAuthenticationService"/>.</item>
/// <item><see cref="Start"/> is called after authentication reaches
/// <see cref="AuthenticationState.SessionActive"/>.</item>
/// <item>The loop terminates when the cancellation token is signalled,
/// the connection is closed, or <see cref="StopAsync"/> is called.</item>
/// </list>
///
/// <para>Single-reader guarantee:</para>
/// <list type="bullet">
/// <item>Only one call to <see cref="Start"/> creates a receive loop
/// (guarded by <see cref="Interlocked"/>).</item>
/// <item>Authentication waits to fully return before the event loop starts,
/// so no two callers simultaneously consume <c>ReceiveAsync</c>.</item>
/// </list>
/// </summary>
public sealed class GameEventService
{
    private readonly IWebSocketConnection _connection;
    private readonly CharacterStats _character;
    private readonly TargetStats _target;
    private readonly Action<Action> _dispatchToUi;
    private readonly Action<RoomEntityRecord[]> _onRoomEntitySnapshot;
    private readonly Action<SkillRecord[]> _onSkillsSnapshot;
    private readonly Action<string> _onMovementFailed;
    private readonly Action<InventoryItemRecord[], int> _onInventorySnapshot;
    private readonly Action<EquippedItemRecord[]> _onEquipmentSnapshot;
    private readonly IMapEventSink? _mapSink;
    private readonly IServiceContextSink? _serviceSink;
    private int _isRunning;
    private CancellationTokenSource? _eventLoopCts;
    private Task? _eventLoopTask;

    /// <summary>
    /// Creates the event service with a reference to the existing connection
    /// and the bindable character and target stats that will be mutated by
    /// incoming events.
    /// </summary>
    /// <param name="mapSink">
    /// Optional receiver for the map / fog-of-war events. When null the map
    /// events are still routed and validated but nothing is applied, so the
    /// service stays usable in contexts that do not show a map.
    /// </param>
    public GameEventService(
        IWebSocketConnection connection,
        CharacterStats character,
        TargetStats target,
        Action<Action> dispatchToUi,
        Action<RoomEntityRecord[]> onRoomEntitySnapshot,
        Action<SkillRecord[]> onSkillsSnapshot,
        Action<string> onMovementFailed,
        Action<InventoryItemRecord[], int> onInventorySnapshot,
        Action<EquippedItemRecord[]> onEquipmentSnapshot,
        IMapEventSink? mapSink = null,
        IServiceContextSink? serviceSink = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dispatchToUi = dispatchToUi ?? throw new ArgumentNullException(nameof(dispatchToUi));
        _onRoomEntitySnapshot = onRoomEntitySnapshot ?? throw new ArgumentNullException(nameof(onRoomEntitySnapshot));
        _onSkillsSnapshot = onSkillsSnapshot ?? throw new ArgumentNullException(nameof(onSkillsSnapshot));
        _onMovementFailed = onMovementFailed ?? throw new ArgumentNullException(nameof(onMovementFailed));
        _onInventorySnapshot = onInventorySnapshot ?? throw new ArgumentNullException(nameof(onInventorySnapshot));
        _onEquipmentSnapshot = onEquipmentSnapshot ?? throw new ArgumentNullException(nameof(onEquipmentSnapshot));
        _mapSink = mapSink;
        _serviceSink = serviceSink;
    }

    /// <summary>
    /// Whether the event receive loop is currently active.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _isRunning) == 1;

    /// <summary>
    /// Starts the background event receive loop exactly once.
    /// Subsequent calls are silently ignored to prevent duplicate loops.
    /// </summary>
    /// <param name="ct">
    /// A cancellation token that signals the loop to terminate (e.g., on
    /// logout or application shutdown).
    /// </param>
    public void Start(CancellationToken ct = default)
    {
        // Prevent duplicate receive loops — only the first caller wins.
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            return;

        _eventLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _eventLoopTask = RunEventLoopAsync(_eventLoopCts.Token);
    }

    /// <summary>
    /// Signals the event loop to stop and waits for it to exit cleanly.
    /// Safe to call when the loop is not running.  Idempotent.
    /// </summary>
    public async Task StopAsync()
    {
        // Signal cancellation (idempotent, safe to call multiple times).
        try { _eventLoopCts?.Cancel(); } catch (ObjectDisposedException) { }

        // Await the task exactly once, regardless of _isRunning state.
        var task = Interlocked.Exchange(ref _eventLoopTask, null);
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        try { _eventLoopCts?.Dispose(); } catch { /* Best-effort */ }
        _eventLoopCts = null;
        Volatile.Write(ref _isRunning, 0);
    }
// ─── Event Loop ────────────────────────────────────────────────────

    private async Task RunEventLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Result<MessageEnvelope> receiveResult;

                try
                {
                    receiveResult = await _connection.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (receiveResult.IsFailure)
                {
                    // Connection closed or receive cancelled — exit the loop.
                    break;
                }

                var envelope = receiveResult.Value;
                await HandleEnvelopeAsync(envelope).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via cancellation
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    // ─── Routing ───────────────────────────────────────────────────────

    private Task HandleEnvelopeAsync(MessageEnvelope envelope)
    {
        if (envelope.Category != MessageCategory.Event)
            return Task.CompletedTask;

        switch (envelope.MessageType)
        {
            case ProtocolMessageTypes.CharacterSnapshot:
                return HandleCharacterSnapshotAsync(envelope);

            case ProtocolMessageTypes.CharacterUpdate:
                return HandleCharacterUpdateAsync(envelope);

            case ProtocolMessageTypes.CharacterStatusSnapshot:
                return HandleCharacterStatusSnapshotAsync(envelope);

            case ProtocolMessageTypes.TargetChanged:
                return HandleTargetChangedAsync(envelope);

            case ProtocolMessageTypes.TargetUpdate:
                return HandleTargetUpdateAsync(envelope);

            case ProtocolMessageTypes.RoomEntitySnapshot:
                return HandleRoomEntitySnapshotAsync(envelope);

            case ProtocolMessageTypes.CharacterSkillsSnapshot:
                return HandleCharacterSkillsSnapshotAsync(envelope);

            case ProtocolMessageTypes.InventorySnapshot:
                return HandleInventorySnapshotAsync(envelope);

            case ProtocolMessageTypes.EquipmentSnapshot:
                return HandleEquipmentSnapshotAsync(envelope);

            case ProtocolMessageTypes.MovementFailed:
                return HandleMovementFailedAsync(envelope);

            case ProtocolMessageTypes.CharacterPointsSnapshot:
                return HandleCharacterPointsSnapshotAsync(envelope);

            // ─── Map / fog-of-war ─────────────────────────────────────────
            // Keystone filters every one of these payloads before sending, so
            // Portal only ever applies already-filtered map data.
            case ProtocolMessageTypes.MapSnapshot:
                return HandleMapSnapshotAsync(envelope);

            case ProtocolMessageTypes.MapRoomDiscovered:
                return HandleMapRoomDiscoveredAsync(envelope);

            case ProtocolMessageTypes.MapPosition:
                return HandleMapPositionAsync(envelope);

            case ProtocolMessageTypes.MapState:
                return HandleMapStateAsync(envelope);

            // ─── Room service context ───────────────────────────────
            // These drive the contextual shop / bank / door controls and
            // the WHO popup. They follow the same shape as every handler
            // here: deserialize, bail out quietly on a malformed payload,
            // then apply on the Dispatcher thread.

            case ProtocolMessageTypes.RoomState:
                return HandleRoomStateAsync(envelope);

            case ProtocolMessageTypes.ShopSnapshot:
                return HandleShopSnapshotAsync(envelope);

            case ProtocolMessageTypes.ShopResult:
                return HandleShopResultAsync(envelope);

            case ProtocolMessageTypes.BankSnapshot:
                return HandleBankSnapshotAsync(envelope);

            case ProtocolMessageTypes.BankResult:
                return HandleBankResultAsync(envelope);

            case ProtocolMessageTypes.DoorResult:
                return HandleDoorResultAsync(envelope);

            case ProtocolMessageTypes.WhoSnapshot:
                return HandleWhoSnapshotAsync(envelope);

            // Valid Events with an unhandled MessageType are silently
            // ignored to preserve forward compatibility.
            default:
                return Task.CompletedTask;
        }
    }

    // ─── Handlers ──────────────────────────────────────────────────────

    private Task HandleCharacterSnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<CharacterSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // CharacterStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplySnapshot(payload));

        return Task.CompletedTask;
    }

    private Task HandleCharacterUpdateAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<CharacterUpdatePayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // CharacterStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyUpdate(payload));

        return Task.CompletedTask;
    }

    private void ApplySnapshot(CharacterSnapshotPayload payload)
    {
        _character.Name = payload.Name;
        _character.RaceName = payload.RaceName;
        _character.ProfessionName = payload.ProfessionName;
        _character.Level = payload.Level;
        _character.Xp = payload.Xp;
        _character.XpForNextLevel = payload.XpForNextLevel;
        _character.Hp = payload.Hp;
        _character.MaxHp = payload.MaxHp;
        _character.Mana = payload.Mana;
        _character.MaxMana = payload.MaxMana;
        _character.Stamina = payload.Stamina;
        _character.MaxStamina = payload.MaxStamina;
        _character.HasMana = payload.HasMana;
    }

    private void ApplyUpdate(CharacterUpdatePayload payload)
    {
        // Apply only non-null fields. null = unchanged.
        // Zero is a legitimate authoritative value and MUST be applied.
        if (payload.Hp.HasValue)         _character.Hp = payload.Hp.Value;
        if (payload.MaxHp.HasValue)     _character.MaxHp = payload.MaxHp.Value;
        if (payload.Mana.HasValue)      _character.Mana = payload.Mana.Value;
        if (payload.MaxMana.HasValue)   _character.MaxMana = payload.MaxMana.Value;
        if (payload.Stamina.HasValue)   _character.Stamina = payload.Stamina.Value;
        if (payload.MaxStamina.HasValue) _character.MaxStamina = payload.MaxStamina.Value;
        if (payload.Xp.HasValue)        _character.Xp = payload.Xp.Value;
        if (payload.XpForNextLevel.HasValue) _character.XpForNextLevel = payload.XpForNextLevel.Value;
        if (payload.Level.HasValue)     _character.Level = payload.Level.Value;
    }

    private Task HandleTargetUpdateAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<TargetUpdatePayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // TargetStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyTargetUpdate(payload));

        return Task.CompletedTask;
    }

    private void ApplyTargetUpdate(TargetUpdatePayload payload)
    {
        // ─── Target ID guard ────────────────────────────────────────
        // Only apply updates when a target is currently selected AND
        // the payload's TargetId matches the active target.
        // Do NOT replace or clear the target from target.update;
        // only target.changed may do that.
        if (!_target.HasTarget)
            return;

        if (_target.TargetId != payload.TargetId)
            return;

        // ─── Delta application ─────────────────────────────────────
        // Apply only non-null fields. null = unchanged.
        // Zero is a legitimate authoritative value and MUST be applied.
        if (payload.Hp.HasValue)     _target.Hp = payload.Hp.Value;
        if (payload.MaxHp.HasValue)  _target.MaxHp = payload.MaxHp.Value;

        // Apply IsDead directly from the payload when present.
        // Do NOT infer death from Hp == 0. Keystone is authoritative.
        if (payload.IsDead.HasValue) _target.IsDead = payload.IsDead.Value;
    }

    private Task HandleTargetChangedAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<TargetChangedPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // TargetStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyTargetChanged(payload));

        return Task.CompletedTask;
    }

    private void ApplyTargetChanged(TargetChangedPayload payload)
    {
        // ─── Target clear ────────────────────────────────────────────
        // A null TargetId is the canonical clear signal.
        if (payload.TargetId is null)
        {
            _target.Clear();
            return;
        }

        // ─── Target set / replacement ────────────────────────────────
        // Clear old target state first so stale values from the previous
        // target cannot survive into the newly selected target.
        _target.Clear();

        // Apply authoritative fields from the payload where present.
        if (payload.Name is not null)     _target.Name = payload.Name;
        if (payload.Level.HasValue)       _target.Level = payload.Level.Value;
        if (payload.Hp.HasValue)          _target.Hp = payload.Hp.Value;
        if (payload.MaxHp.HasValue)       _target.MaxHp = payload.MaxHp.Value;
        if (payload.Identity is not null) _target.Identity = payload.Identity;
        if (payload.IsMob.HasValue)       _target.IsMob = payload.IsMob.Value;

        // Clear() resets IsDead to false, which is correct for a new target.
        // TargetChangedPayload does not carry IsDead; when changing to a
        // NEW target, IsDead must be false (already handled by Clear).

        // Set TargetId last so HasTarget becomes true only after state
        // is fully prepared. This avoids a transient flicker where
        // HasTarget is true but Name/Level/Hp are still stale.
        _target.TargetId = payload.TargetId;
    }

    private Task HandleRoomEntitySnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<RoomEntitySnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // Mutate room entity state on the UI dispatcher thread.
        _dispatchToUi(() => ApplyRoomEntitySnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplyRoomEntitySnapshot(RoomEntitySnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // room.entity.snapshot is the complete authoritative list.
        // Replace the existing room entity list contents.
        _onRoomEntitySnapshot(payload.Entities?.ToArray() ?? Array.Empty<RoomEntityRecord>());
    }

    private Task HandleCharacterSkillsSnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<CharacterSkillsSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // Skills list is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplySkillsSnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplySkillsSnapshot(CharacterSkillsSnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // character.skills.snapshot is the complete authoritative list.
        // Replace the existing skills collection contents.
        _onSkillsSnapshot(payload.Skills?.ToArray() ?? Array.Empty<SkillRecord>());
    }

    private Task HandleMovementFailedAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<MovementFailedPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // MovementMessage is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyMovementFailed(payload));

        return Task.CompletedTask;
    }

    private void ApplyMovementFailed(MovementFailedPayload payload)
    {
        _onMovementFailed(payload.Message ?? string.Empty);
    }

    private Task HandleInventorySnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<InventorySnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // InventoryItems and Currency are bound to WPF controls. All
        // mutations must occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyInventorySnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplyInventorySnapshot(InventorySnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // inventory.snapshot is the complete authoritative list plus currency.
        _onInventorySnapshot(
            payload.Items?.ToArray() ?? Array.Empty<InventoryItemRecord>(),
            payload.Currency);
    }

    private Task HandleEquipmentSnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<EquipmentSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // EquippedItems is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyEquipmentSnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplyEquipmentSnapshot(EquipmentSnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // equipment.snapshot is the complete authoritative list.
        _onEquipmentSnapshot(
            payload.Equipped?.ToArray() ?? Array.Empty<EquippedItemRecord>());
    }

    private Task HandleCharacterStatusSnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<CharacterStatusSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // CharacterStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyCharacterStatusSnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplyCharacterStatusSnapshot(CharacterStatusSnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // character.status.snapshot is the complete authoritative status.
        _character.ApplyStatusSnapshot(payload.State, payload.IsAlive);
    }

    private Task HandleCharacterPointsSnapshotAsync(MessageEnvelope envelope)
    {
        var deserializeResult =
            ProtocolSerializer.DeserializePayload<CharacterPointsSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // CharacterStats is bound to WPF controls. All mutations must
        // occur on the Dispatcher/UI thread.
        _dispatchToUi(() => ApplyCharacterPointsSnapshot(payload));

        return Task.CompletedTask;
    }

    private void ApplyCharacterPointsSnapshot(CharacterPointsSnapshotPayload payload)
    {
        // ─── Replacement semantics ─────────────────────────────────────
        // character.points.snapshot is always the COMPLETE authoritative
        // Character Point and permanent stat state — never a delta. It also
        // carries Keystone's rejection message when an allocation failed, so
        // the same event both reports the error and refreshes the values.
        var stats = (payload.Stats ?? Array.Empty<AllocatableStatRecord>())
            .Select(s => (s.StatId, s.Name, s.Value, s.Cap))
            .ToArray();

        _character.ApplyCharacterPointsSnapshot(
            payload.AvailablePoints,
            payload.PointsPerLevel,
            stats,
            payload.Error);
    }

    // ─── Map handlers ────────────────────────────────────────────────────
    //
    // All four follow the same shape as every other handler here: deserialize,
    // bail out quietly on failure, then apply on the Dispatcher thread. A
    // malformed map payload is ignored rather than allowed to tear down the
    // event loop, because map data is navigational and never authoritative for
    // any gameplay decision.

    /// <summary>
    /// Handles <c>map.snapshot</c> — the complete filtered map for this
    /// character. Full replacement semantics; see <see cref="MapState.ApplySnapshot"/>.
    /// </summary>
    private Task HandleMapSnapshotAsync(MessageEnvelope envelope)
    {
        if (_mapSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<MapSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        // The map binds to UI state, so this must run on the Dispatcher thread.
        _dispatchToUi(() => _mapSink.ApplyMapSnapshot(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>map.room.discovered</c> — additive, single room.</summary>
    private Task HandleMapRoomDiscoveredAsync(MessageEnvelope envelope)
    {
        if (_mapSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<MapRoomDiscoveredPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _mapSink.ApplyMapRoomDiscovered(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>map.position</c> — moves the player's marker only.</summary>
    private Task HandleMapPositionAsync(MessageEnvelope envelope)
    {
        if (_mapSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<MapPositionPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _mapSink.ApplyMapPosition(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>map.state</c> — discovery counters only.</summary>
    private Task HandleMapStateAsync(MessageEnvelope envelope)
    {
        if (_mapSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<MapStatePayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _mapSink.ApplyMapState(payload));

        return Task.CompletedTask;
    }

    // ─── Room service context handlers ───────────────────────────────
    //
    // Each of these mirrors the shape of every handler above: deserialize
    // the payload, return quietly when it is malformed or when no sink is
    // attached, then apply on the Dispatcher thread. A bad service payload
    // is ignored rather than allowed to tear down the receive loop, because
    // none of these is authoritative for a gameplay decision the client
    // makes on its own — the client only ever renders what arrives here.

    /// <summary>Handles <c>room.state</c> — the authoritative current room.</summary>
    private Task HandleRoomStateAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<RoomStatePayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyRoomState(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>shop.snapshot</c> — shop list and/or wares.</summary>
    private Task HandleShopSnapshotAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<ShopSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyShopSnapshot(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>shop.result</c> — a buy or sell outcome.</summary>
    private Task HandleShopResultAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<ShopResultPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyShopResult(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>bank.snapshot</c> — carried and banked copper.</summary>
    private Task HandleBankSnapshotAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<BankSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyBankSnapshot(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>bank.result</c> — a deposit or withdraw outcome.</summary>
    private Task HandleBankResultAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<BankResultPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyBankResult(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>door.result</c> — a door action outcome.</summary>
    private Task HandleDoorResultAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<DoorResultPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyDoorResult(payload));

        return Task.CompletedTask;
    }

    /// <summary>Handles <c>who.snapshot</c> — the connected-character list.</summary>
    private Task HandleWhoSnapshotAsync(MessageEnvelope envelope)
    {
        if (_serviceSink is null)
            return Task.CompletedTask;

        var deserializeResult =
            ProtocolSerializer.DeserializePayload<WhoSnapshotPayload>(envelope);

        if (deserializeResult.IsFailure)
            return Task.CompletedTask;

        var payload = deserializeResult.Value;

        _dispatchToUi(() => _serviceSink.ApplyWhoSnapshot(payload));

        return Task.CompletedTask;
    }
}