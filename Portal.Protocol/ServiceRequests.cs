namespace Portal.Protocol;

/// <summary>
/// Protocol V1 request for the shops (or one shop's wares) linked to the
/// character's CURRENT room, sent via <c>shop.snapshot.request</c>.
/// </summary>
/// <remarks>
/// Portal sends no price, no item list and no stock figure: the server is
/// the only source of all three. The reply arrives as a
/// <c>shop.snapshot</c> event.
/// </remarks>
public sealed class ShopSnapshotRequest
{
    /// <summary>
    /// Shop to fetch wares for. Null requests only the list of shops linked
    /// to the current room.
    /// </summary>
    public string? ShopId { get; init; }

    public ShopSnapshotRequest()
    {
    }

    public ShopSnapshotRequest(string? shopId)
    {
        ShopId = shopId;
    }
}

/// <summary>
/// Protocol V1 request to buy an item from a room-linked shop, sent via
/// <c>shop.buy.request</c>.
/// </summary>
/// <remarks>
/// Portal sends no price: Keystone owns validation, pricing, stock and
/// currency. The reply arrives as a <c>shop.result</c> event followed by
/// refreshed <c>inventory.snapshot</c> and <c>shop.snapshot</c> events.
/// </remarks>
public sealed class ShopBuyRequest
{
    /// <summary>Shop to buy from; must be linked to the current room.</summary>
    public string ShopId { get; init; } = string.Empty;

    /// <summary>Item definition id to buy.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>How many to buy. Must be positive; the server enforces it.</summary>
    public int Quantity { get; init; } = 1;

    public ShopBuyRequest()
    {
    }

    public ShopBuyRequest(string shopId, string itemId, int quantity = 1)
    {
        ShopId = shopId ?? throw new ArgumentNullException(nameof(shopId));
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Quantity = quantity;
    }
}

/// <summary>
/// Protocol V1 request to sell an item to a room-linked shop, sent via
/// <c>shop.sell.request</c>.
/// </summary>
public sealed class ShopSellRequest
{
    /// <summary>Shop to sell to; must be linked to the current room.</summary>
    public string ShopId { get; init; } = string.Empty;

    /// <summary>Item definition id to sell.</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>How many to sell. Must be positive; the server enforces it.</summary>
    public int Quantity { get; init; } = 1;

    public ShopSellRequest()
    {
    }

    public ShopSellRequest(string shopId, string itemId, int quantity = 1)
    {
        ShopId = shopId ?? throw new ArgumentNullException(nameof(shopId));
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Quantity = quantity;
    }
}

/// <summary>
/// Protocol V1 request for the character's bank balances, sent via
/// <c>bank.snapshot.request</c>.
/// </summary>
/// <remarks>
/// The reply is a <c>bank.snapshot</c> event carrying carried and stored
/// copper. Keystone's bank is currency only, so no item-storage concept
/// exists anywhere in this protocol.
/// </remarks>
public sealed class BankSnapshotRequest
{
    public BankSnapshotRequest()
    {
    }
}

/// <summary>
/// Protocol V1 request to move copper into the bank, sent via
/// <c>bank.deposit.request</c>.
/// </summary>
public sealed class BankDepositRequest
{
    /// <summary>Whole copper pieces to move from the pack into the bank.</summary>
    public int Amount { get; init; }

    public BankDepositRequest()
    {
    }

    public BankDepositRequest(int amount)
    {
        Amount = amount;
    }
}

/// <summary>
/// Protocol V1 request to move copper out of the bank, sent via
/// <c>bank.withdraw.request</c>.
/// </summary>
public sealed class BankWithdrawRequest
{
    /// <summary>Whole copper pieces to move from the bank into the pack.</summary>
    public int Amount { get; init; }

    public BankWithdrawRequest()
    {
    }

    public BankWithdrawRequest(int amount)
    {
        Amount = amount;
    }
}

/// <summary>
/// Protocol V1 door action request, sent via <c>door.action.request</c>.
/// </summary>
/// <remarks>
/// The direction identifies the real exit in the character's current room.
/// Every rule — including key possession — is enforced server-side, so this
/// request carries only intent, never an expected outcome.
/// </remarks>
public sealed class DoorActionRequest
{
    /// <summary>Canonical direction of the door ("north", "up", ...).</summary>
    public string Direction { get; init; } = string.Empty;

    /// <summary>One of "open", "close", "lock" or "unlock".</summary>
    public string Action { get; init; } = string.Empty;

    public DoorActionRequest()
    {
    }

    public DoorActionRequest(string direction, string action)
    {
        Direction = direction ?? throw new ArgumentNullException(nameof(direction));
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }
}

/// <summary>
/// Protocol V1 request for the connected-character list, sent via
/// <c>who.request</c>.
/// </summary>
public sealed class WhoRequest
{
    public WhoRequest()
    {
    }
}
