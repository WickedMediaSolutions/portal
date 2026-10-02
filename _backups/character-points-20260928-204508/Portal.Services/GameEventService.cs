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
    private int _isRunning;
    private CancellationTokenSource? _eventLoopCts;
    private Task? _eventLoopTask;

    /// <summary>
    /// Creates the event service with a reference to the existing connection
    /// and the bindable character and target stats that will be mutated by
    /// incoming events.
    /// </summary>
    public GameEventService(
        IWebSocketConnection connection,
        CharacterStats character,
        TargetStats target,
        Action<Action> dispatchToUi,
        Action<RoomEntityRecord[]> onRoomEntitySnapshot,
        Action<SkillRecord[]> onSkillsSnapshot,
        Action<string> onMovementFailed,
        Action<InventoryItemRecord[], int> onInventorySnapshot,
        Action<EquippedItemRecord[]> onEquipmentSnapshot)
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
}