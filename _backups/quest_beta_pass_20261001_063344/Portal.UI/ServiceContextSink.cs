using Portal.Protocol;
using Portal.State;

namespace Portal.UI;

/// <summary>
/// Routes the server's room-service events into the bound view-models.
/// </summary>
/// <remarks>
/// <para>
/// This is the UI-layer implementation of <see cref="IServiceContextSink"/>,
/// sitting alongside the map host. <see cref="GameEventService"/> owns
/// deserialization and Dispatcher marshalling; this type only forwards the
/// already-deserialized payload to the right view-model, exactly as the map
/// host does for map events.
/// </para>
/// <para>
/// The one piece of real logic here is the room-change housekeeping: when a
/// new <c>room.state</c> arrives for a DIFFERENT room, the shop panel is
/// cleared so one room's wares can never be shown under another room's
/// services. The same event for the same room (after a door action, or a
/// reconnect) leaves the panel alone.
/// </para>
/// </remarks>
internal sealed class ServiceContextSink : IServiceContextSink
{
    private readonly MainViewModel _viewModel;
    private string? _lastRoomId;
    private string? _lastWorldRoomId;
    private bool _hasRoom;

    public ServiceContextSink(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <summary>
    /// Forgets the last-seen room, so the next room.state is treated as a
    /// fresh arrival rather than a change within the same room.
    /// </summary>
    /// <remarks>
    /// Called when a session ends. The first room.state of the next session
    /// must not be compared against the previous character's room, or a
    /// coincidental id match would suppress the shop-panel reset.
    /// </remarks>
    public void Reset()
    {
        _lastRoomId = null;
        _lastWorldRoomId = null;
        _hasRoom = false;
    }

    public void ApplyRoomState(RoomStatePayload payload)
    {
        // Detect a genuine room change BEFORE applying, so the shop panel can
        // be cleared only when the character really moved somewhere else.
        bool roomChanged = _hasRoom
            && (payload.RoomId != _lastRoomId
                || payload.WorldRoomId != _lastWorldRoomId);

        _lastRoomId = payload.RoomId;
        _lastWorldRoomId = payload.WorldRoomId;
        _hasRoom = true;

        if (roomChanged)
        {
            // The character left the previous room: any wares or shop
            // selection shown for it are now meaningless.
            _viewModel.Shop.ClearForRoomChange();
        }

        _viewModel.Room.ApplyRoomState(payload);
    }

    public void ApplyShopSnapshot(ShopSnapshotPayload payload)
    {
        // A snapshot that reports the service is unavailable clears the
        // panel rather than leaving the last known wares on screen.
        if (!payload.Available && payload.Shop is null)
        {
            _viewModel.Shop.ClearForRoomChange();
        }

        _viewModel.Shop.ApplyShopSnapshot(payload);
    }

    public void ApplyShopResult(ShopResultPayload payload)
        => _viewModel.Shop.ApplyShopResult(payload);

    public void ApplyBankSnapshot(BankSnapshotPayload payload)
        => _viewModel.Bank.ApplyBankSnapshot(payload);

    public void ApplyBankResult(BankResultPayload payload)
        => _viewModel.Bank.ApplyBankResult(payload);

    public void ApplyDoorResult(DoorResultPayload payload)
        => _viewModel.Room.ApplyDoorResult(payload);

    public void ApplyWhoSnapshot(WhoSnapshotPayload payload)
        => _viewModel.Who.ApplyWhoSnapshot(payload);
}
