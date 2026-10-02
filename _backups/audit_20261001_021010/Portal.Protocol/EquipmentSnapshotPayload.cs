namespace Portal.Protocol;

/// <summary>
/// Protocol V1 equipment snapshot payload.
/// Pushed by the server via the <c>equipment.snapshot</c> event to convey
/// the complete authoritative equipped state of the character.
/// </summary>
public sealed class EquipmentSnapshotPayload
{
    /// <summary>
    /// The complete list of equipped items.
    /// Replacement semantics — this is the full authoritative list.
    /// Empty slots are omitted by the server.
    /// </summary>
    public IReadOnlyList<EquippedItemRecord> Equipped { get; init; } = Array.Empty<EquippedItemRecord>();

    public EquipmentSnapshotPayload()
    {
    }

    public EquipmentSnapshotPayload(IReadOnlyList<EquippedItemRecord> equipped)
    {
        Equipped = equipped ?? throw new ArgumentNullException(nameof(equipped));
    }
}

/// <summary>
/// Protocol V1 individual equipped item record.
/// Mirrors Keystone's authoritative equipment slot state.
/// Extended with minimal player-visible ITEM_REGISTRY metadata for the
/// item information card.
/// </summary>
public sealed class EquippedItemRecord
{
    /// <summary>Equipment slot identifier (e.g. "head", "main_hand").</summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>Stable unique item definition identifier (e.g. "rusty_sword").</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Display name of the equipped item.</summary>
    public string Name { get; init; } = string.Empty;

    // ─── Extended metadata (item information card) ──────────────────
    // All fields are nullable — they are only present when the Keystone
    // ITEM_REGISTRY provides them for the item definition.

    /// <summary>Item category: weapon, armor, consumable, quest, misc.</summary>
    public string? Category { get; init; }

    /// <summary>Description / flavour text from the item definition.</summary>
    public string? Description { get; init; }

    /// <summary>Damage type for weapons (e.g. "slashing", "piercing").</summary>
    public string? DamageType { get; init; }

    /// <summary>Base damage for weapons.</summary>
    public int? BaseDamage { get; init; }

    /// <summary>Minimum damage for weapons (MudCentral items).</summary>
    public int? DamageMin { get; init; }

    /// <summary>Maximum damage for weapons (MudCentral items).</summary>
    public int? DamageMax { get; init; }

    /// <summary>Armor class for armor pieces.</summary>
    public int? ArmorClass { get; init; }

    /// <summary>Required level to equip/use.</summary>
    public int? Level { get; init; }

    /// <summary>Required strength to equip/use.</summary>
    public int? Strength { get; init; }

    /// <summary>Weapon speed (milliseconds between attacks).</summary>
    public int? Speed { get; init; }

    /// <summary>Encumbrance / weight of the item.</summary>
    public int? Encumbrance { get; init; }

    public EquippedItemRecord()
    {
    }

    public EquippedItemRecord(string slot, string itemId, string name)
    {
        Slot = slot ?? throw new ArgumentNullException(nameof(slot));
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}