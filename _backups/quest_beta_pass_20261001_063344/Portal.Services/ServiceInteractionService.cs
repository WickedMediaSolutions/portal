using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends authoritative room-service requests from Portal to Keystone over the
/// existing <see cref="IWebSocketConnection"/>: shop browsing, buying and
/// selling, banking, door interaction and WHO.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="MovementService"/>, these are fire-and-forget senders.
/// They do NOT consume <c>ReceiveAsync</c> and never open a connection of
/// their own; every resulting state change returns through the existing
/// <see cref="GameEventService"/> event stream.
/// </para>
/// <para>
/// Portal deliberately sends only INTENT here. It never sends a price, a
/// balance, a stock level, a key or an expected outcome: validation,
/// pricing, stock, currency, keys and door state are all resolved by
/// Keystone, which remains the single authority for all of them.
/// </para>
/// </remarks>
public sealed class ServiceInteractionService
{
    private readonly IWebSocketConnection _connection;

    public ServiceInteractionService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    // ─── Shop ──────────────────────────────────────────────────────────

    /// <summary>
    /// Requests the shops linked to the current room, or one shop's wares.
    /// The reply is a <c>shop.snapshot</c> event.
    /// </summary>
    public Task<Result> RequestShopAsync(string? shopId = null, CancellationToken ct = default)
        => _sendAsync(
            ProtocolMessageTypes.ShopSnapshotRequest,
            new ShopSnapshotRequest(shopId),
            ct);

    /// <summary>
    /// Asks Keystone to buy an item. The server decides whether it is
    /// affordable, in stock and actually sold here; the reply is a
    /// <c>shop.result</c> event.
    /// </summary>
    public Task<Result> BuyAsync(string shopId, string itemId, int quantity = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shopId))
            return Task.FromResult(Result.Failure("SHOP_NO_SHOP", "Cannot buy: no shop specified."));

        if (string.IsNullOrWhiteSpace(itemId))
            return Task.FromResult(Result.Failure("SHOP_NO_ITEM", "Cannot buy: no item specified."));

        return _sendAsync(
            ProtocolMessageTypes.ShopBuyRequest,
            new ShopBuyRequest(shopId, itemId, quantity),
            ct);
    }

    /// <summary>
    /// Asks Keystone to sell an item. The server decides whether the item is
    /// sellable and whether this shop buys it; the reply is a
    /// <c>shop.result</c> event.
    /// </summary>
    public Task<Result> SellAsync(string shopId, string itemId, int quantity = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(shopId))
            return Task.FromResult(Result.Failure("SHOP_NO_SHOP", "Cannot sell: no shop specified."));

        if (string.IsNullOrWhiteSpace(itemId))
            return Task.FromResult(Result.Failure("SHOP_NO_ITEM", "Cannot sell: no item specified."));

        return _sendAsync(
            ProtocolMessageTypes.ShopSellRequest,
            new ShopSellRequest(shopId, itemId, quantity),
            ct);
    }

    // ─── Bank ──────────────────────────────────────────────────────────

    /// <summary>Requests the authoritative carried and banked copper.</summary>
    public Task<Result> RequestBankAsync(CancellationToken ct = default)
        => _sendAsync(
            ProtocolMessageTypes.BankSnapshotRequest,
            new BankSnapshotRequest(),
            ct);

    /// <summary>Asks Keystone to move copper from the pack into the bank.</summary>
    public Task<Result> DepositAsync(int amount, CancellationToken ct = default)
    {
        // The client refuses an obviously invalid amount before spending a
        // round trip, but the server re-validates regardless: this is a
        // usability shortcut, never the authority.
        if (amount <= 0)
            return Task.FromResult(Result.Failure("BANK_INVALID_AMOUNT", "Amount must be a positive whole number."));

        return _sendAsync(
            ProtocolMessageTypes.BankDepositRequest,
            new BankDepositRequest(amount),
            ct);
    }

    /// <summary>Asks Keystone to move copper from the bank into the pack.</summary>
    public Task<Result> WithdrawAsync(int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return Task.FromResult(Result.Failure("BANK_INVALID_AMOUNT", "Amount must be a positive whole number."));

        return _sendAsync(
            ProtocolMessageTypes.BankWithdrawRequest,
            new BankWithdrawRequest(amount),
            ct);
    }

    // ─── Doors ─────────────────────────────────────────────────────────

    /// <summary>
    /// Asks Keystone to open, close, lock or unlock the door in
    /// <paramref name="direction"/>. Every rule, including key possession,
    /// is enforced server-side; the reply is a <c>door.result</c> event
    /// followed by a fresh <c>room.state</c>.
    /// </summary>
    public Task<Result> DoorActionAsync(
        string direction,
        string action,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return Task.FromResult(Result.Failure("DOOR_NO_DIRECTION", "Cannot act on a door: no direction given."));

        if (string.IsNullOrWhiteSpace(action))
            return Task.FromResult(Result.Failure("DOOR_NO_ACTION", "Cannot act on a door: no action given."));

        return _sendAsync(
            ProtocolMessageTypes.DoorActionRequest,
            new DoorActionRequest(direction, action),
            ct);
    }

    // ─── WHO ───────────────────────────────────────────────────────────

    /// <summary>Requests the connected-character list.</summary>
    public Task<Result> RequestWhoAsync(CancellationToken ct = default)
        => _sendAsync(ProtocolMessageTypes.WhoRequest, new WhoRequest(), ct);

    // ─── Shared send path ──────────────────────────────────────────────

    private async Task<Result> _sendAsync<T>(
        string messageType,
        T payload,
        CancellationToken ct)
    {
        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            messageType,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(payload));

        return await _connection.SendAsync(envelope, ct);
    }
}
