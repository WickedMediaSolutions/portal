namespace Portal.Protocol;

/// <summary>
/// Protocol V1 equipment equip request.
/// Sent by Portal to request that the authenticated character equip
/// the specified item from inventory into its default slot.
/// Keystone resolves the slot authoritatively from the item definition.
/// Does NOT expect a direct response — the result arrives as an
/// inventory.snapshot + equipment.snapshot event pair.
/// </summary>
public sealed class EquipmentEquipRequest
{
    /// <summary>
    /// Stable unique item definition identifier (e.g. "rusty_sword").
    /// Must be carried in the character's inventory.
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    public EquipmentEquipRequest()
    {
    }

    public EquipmentEquipRequest(string itemId)
    {
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
    }
}