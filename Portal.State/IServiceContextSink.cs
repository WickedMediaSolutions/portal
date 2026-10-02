using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Receives the server-pushed room-service events and applies them to the
/// client's current-room state.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the UI layer, mirroring <see cref="IMapEventSink"/>:
/// <see cref="GameEventService"/> owns deserialization and UI-thread
/// dispatching exactly as it does for every other event, and this interface
/// is only the hand-off point.
/// </para>
/// <para>
/// Implementations are called on the WPF Dispatcher thread and are the ONLY
/// place a room-service payload is allowed to mutate visible state.
/// </para>
/// </remarks>
public interface IServiceContextSink
{
    /// <summary>
    /// Applies the authoritative current room: name, description, exits,
    /// observable door state and available services. Sent on login, after
    /// every movement, and after every door action, so contextual controls
    /// always match the room the character is really in.
    /// </summary>
    void ApplyRoomState(RoomStatePayload payload);

    /// <summary>Applies a shop list and/or one shop's authoritative wares.</summary>
    void ApplyShopSnapshot(ShopSnapshotPayload payload);

    /// <summary>Applies the authoritative outcome of a buy or sell.</summary>
    void ApplyShopResult(ShopResultPayload payload);

    /// <summary>Applies the authoritative carried and banked copper.</summary>
    void ApplyBankSnapshot(BankSnapshotPayload payload);

    /// <summary>Applies the authoritative outcome of a deposit or withdraw.</summary>
    void ApplyBankResult(BankResultPayload payload);

    /// <summary>Applies the authoritative outcome of a door action.</summary>
    void ApplyDoorResult(DoorResultPayload payload);

    /// <summary>Applies the connected-character list.</summary>
    void ApplyWhoSnapshot(WhoSnapshotPayload payload);

    /// <summary>
    /// Replaces the client's whole quest view with the server's authoritative
    /// snapshot. Sent on login and after every quest action, so a reconnect
    /// restores exactly the state the server holds.
    /// </summary>
    void ApplyQuestSnapshot(QuestSnapshotPayload payload);

    /// <summary>
    /// Applies incremental objective progress for the quests that advanced.
    /// Only quests named in the payload are touched; everything else is left
    /// exactly as the server last described it.
    /// </summary>
    void ApplyQuestProgress(QuestProgressPayload payload);

    /// <summary>
    /// Applies the authoritative accept / abandon / complete outcome. The UI
    /// must render this result rather than assuming a button press succeeded.
    /// </summary>
    void ApplyQuestResult(QuestResultPayload payload);
}
