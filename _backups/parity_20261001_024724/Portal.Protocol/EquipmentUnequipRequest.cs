namespace Portal.Protocol;

/// <summary>
/// Protocol V1 equipment unequip request.
/// Sent by Portal to request that the authenticated character unequip
/// whatever is currently occupying the specified equipment slot.
/// Keystone resolves the slot authoritatively.
/// Does NOT expect a direct response — the result arrives as an
/// inventory.snapshot + equipment.snapshot event pair.
/// </summary>
public sealed class EquipmentUnequipRequest
{
    /// <summary>
    /// Equipment slot identifier (e.g. "head", "main_hand").
    /// Must be an occupied equipment slot on the character.
    /// </summary>
    public string Slot { get; init; } = string.Empty;

    public EquipmentUnequipRequest()
    {
    }

    public EquipmentUnequipRequest(string slot)
    {
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
    }
}