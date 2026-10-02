namespace Portal.Protocol;

/// <summary>
/// Protocol V1 shop snapshot payload.
/// Pushed by the server via the <c>shop.snapshot</c> event.
/// </summary>
/// <remarks>
/// Every price and stock value here is computed by the server. Portal never
/// calculates, rounds or caches a price: it displays exactly what this
/// payload says, so a graphical client and a text client can never disagree
/// about what an item costs.
/// </remarks>
public sealed class ShopSnapshotPayload
{
    /// <summary>
    /// Whether any shop is usable here. False when the room links no shops,
    /// or when a specific shop was requested that is not linked to this room.
    /// </summary>
    public bool Available { get; init; }

    /// <summary>Authoritative reason when <see cref="Available"/> is false.</summary>
    public string? Message { get; init; }

    /// <summary>Shops linked to the current room.</summary>
    public IReadOnlyList<ShopSummaryRecord> Shops { get; init; } = Array.Empty<ShopSummaryRecord>();

    /// <summary>
    /// Wares for one specific shop, or null when only the shop list was
    /// requested.
    /// </summary>
    public ShopDetailRecord? Shop { get; init; }

    public ShopSnapshotPayload()
    {
    }
}

/// <summary>A single shop linked to the character's current room.</summary>
public sealed class ShopSummaryRecord
{
    /// <summary>Stable shop identifier used in buy/sell requests.</summary>
    public string ShopId { get; init; } = string.Empty;

    /// <summary>Authoritative display name of the shop.</summary>
    public string Name { get; init; } = string.Empty;

    public ShopSummaryRecord()
    {
    }

    public ShopSummaryRecord(string shopId, string name)
    {
        ShopId = shopId ?? throw new ArgumentNullException(nameof(shopId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}

/// <summary>One shop's authoritative wares.</summary>
public sealed class ShopDetailRecord
{
    /// <summary>Stable shop identifier.</summary>
    public string ShopId { get; init; } = string.Empty;

    /// <summary>Authoritative display name of the shop.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The goods this shop offers, in server order.</summary>
    public IReadOnlyList<ShopWareRecord> Wares { get; init; } = Array.Empty<ShopWareRecord>();

    public ShopDetailRecord()
    {
    }
}

/// <summary>A single purchasable/sellable line in a shop.</summary>
public sealed class ShopWareRecord
{
    /// <summary>Stable item definition identifier.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Authoritative display name of the item.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Item description, when the item definition has one.</summary>
    public string? Description { get; init; }

    /// <summary>Item category (weapon, armor, consumable, ...).</summary>
    public string? Category { get; init; }

    /// <summary>Equipment slot this item fits in, if applicable.</summary>
    public string? Slot { get; init; }

    /// <summary>Required level, when the item definition has one.</summary>
    public int? Level { get; init; }

    /// <summary>
    /// Server-computed purchase price in copper, or null when this shop does
    /// not sell the item. Null is meaningfully different from zero.
    /// </summary>
    public int? BuyPrice { get; init; }

    /// <summary>
    /// Server-computed sale price in copper, or null when this shop does not
    /// buy the item.
    /// </summary>
    public int? SellPrice { get; init; }

    /// <summary>
    /// Remaining stock, or null when stock is unlimited. Zero means sold out.
    /// </summary>
    public int? Stock { get; init; }

    public ShopWareRecord()
    {
    }

    /// <summary>True when this shop sells the item.</summary>
    public bool CanBuy => BuyPrice.HasValue;

    /// <summary>True when this shop buys the item back.</summary>
    public bool CanSell => SellPrice.HasValue;

    /// <summary>True when the server reports this ware as sold out.</summary>
    public bool SoldOut => Stock.HasValue && Stock.Value <= 0;

    /// <summary>True when this ware currently has no stock limit at all.</summary>
    public bool UnlimitedStock => !Stock.HasValue;
}

/// <summary>
/// Protocol V1 shop result payload.
/// Pushed by the server via the <c>shop.result</c> event after every buy or
/// sell attempt, successful or not.
/// </summary>
public sealed class ShopResultPayload
{
    /// <summary>Which operation this reports: "buy" or "sell".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>The shop the transaction targeted.</summary>
    public string? ShopId { get; init; }

    /// <summary>Whether the server accepted the transaction.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// Authoritative outcome text, verbatim from the server. This is the same
    /// wording a text player reads, including refusals such as
    /// "Insufficient funds: need 500c, have 120c."
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The item the transaction concerned.</summary>
    public string? ItemId { get; init; }

    /// <summary>The quantity the transaction concerned.</summary>
    public int Quantity { get; init; }

    /// <summary>Total copper moved, when the server reports one.</summary>
    public int TotalCost { get; init; }

    /// <summary>Resulting carried balance, when the server reports one.</summary>
    public int? NewBalance { get; init; }

    public ShopResultPayload()
    {
    }
}
