using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends authoritative equipment equip / unequip requests from Portal to
/// Keystone over the existing <see cref="IWebSocketConnection"/>.
///
/// This service is a fire-and-forget sender. It does NOT consume
/// <c>ReceiveAsync</c> and never creates its own connection.  All resulting
/// state (inventory.snapshot, equipment.snapshot) returns through the
/// existing <see cref="GameEventService"/> event stream.
/// </summary>
public sealed class EquipmentService
{
    private readonly IWebSocketConnection _connection;

    public EquipmentService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Sends an equipment.equip.request to Keystone to equip the specified
    /// carried item.  Keystone resolves the target slot authoritatively from
    /// the item definition and applies its existing equip validation.
    ///
    /// Does NOT read a response.  The resulting inventory / equipment state
    /// arrives through the existing event stream as refreshed
    /// inventory.snapshot and equipment.snapshot events.
    /// </summary>
    /// <param name="itemId">The stable Keystone item definition identifier
    /// (e.g. "rusty_sword"). Must be non-empty.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendEquipAsync(string itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return Result.Failure(
                "EQUIP_NO_ITEM",
                "Cannot equip: no ItemId provided.");

        var request = new EquipmentEquipRequest(itemId);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.EquipmentEquipRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }

    /// <summary>
    /// Sends an equipment.unequip.request to Keystone to unequip whatever is
    /// currently occupying the specified equipment slot.  Keystone resolves
    /// the slot authoritatively and applies its existing unequip validation.
    ///
    /// Does NOT read a response.  The resulting inventory / equipment state
    /// arrives through the existing event stream as refreshed
    /// inventory.snapshot and equipment.snapshot events.
    /// </summary>
    /// <param name="slot">The equipment slot identifier (e.g. "head",
    /// "main_hand"). Must be non-empty.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendUnequipAsync(string slot, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slot))
            return Result.Failure(
                "UNEQUIP_NO_SLOT",
                "Cannot unequip: no Slot provided.");

        var request = new EquipmentUnequipRequest(slot);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.EquipmentUnequipRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }
}