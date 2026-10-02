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
}
