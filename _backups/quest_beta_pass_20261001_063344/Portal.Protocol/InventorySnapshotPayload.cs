namespace Portal.Protocol;

/// <summary>
/// Protocol V1 inventory snapshot payload.
/// Pushed by the server via the <c>inventory.snapshot</c> event to convey
/// the complete authoritative carried inventory and currency balance.
/// </summary>
public sealed class InventorySnapshotPayload
{
    /// <summary>
    /// The complete list of carried inventory items.
    /// Replacement semantics — this is the full authoritative list.
    /// </summary>
    public IReadOnlyList<InventoryItemRecord> Items { get; init; } = Array.Empty<InventoryItemRecord>();

    /// <summary>
    /// Current currency balance in copper pieces.
    /// </summary>
    public int Currency { get; init; }

    public InventorySnapshotPayload()
    {
    }

    public InventorySnapshotPayload(IReadOnlyList<InventoryItemRecord> items, int currency)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Currency = currency;
    }
}

/// <summary>
/// Protocol V1 individual inventory item record.
/// Mirrors Keystone's authoritative inventory item state.
/// Extended with minimal player-visible ITEM_REGISTRY metadata for the
/// item information card.
/// </summary>
public sealed class InventoryItemRecord
{
    /// <summary>Stable unique item definition identifier (e.g. "rusty_sword").</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Display name of the item.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Quantity carried in inventory.</summary>
    public int Quantity { get; init; }

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

    /// <summary>Equipment slot this item fits in, if applicable.</summary>
    public string? Slot { get; init; }

    public InventoryItemRecord()
    {
    }

    public InventoryItemRecord(string itemId, string name, int quantity)
    {
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Quantity = quantity;
    }
}