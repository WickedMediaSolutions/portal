using System.Text.Json;
using Portal.Protocol;
using Xunit;

namespace Portal.Protocol.Tests;

/// <summary>
/// Protocol-level tests for the room-service messages: room.state and the
/// shop, bank, door and WHO families.
/// </summary>
/// <remarks>
/// Two properties are pinned deliberately:
/// <list type="bullet">
/// <item>Portal never sends a price, a balance, a stock level or an expected
/// outcome — it sends intent only, so the server stays authoritative.</item>
/// <item>No healer or party message type exists, because Keystone has no
/// healer command and no party system.</item>
/// <item>Quest messages DO exist: Keystone's quest runtime is now driven by
/// real kill and item-acquisition hooks, so quest state is genuinely
/// server-authoritative and is fully covered here and in
/// QuestProtocolTests.</item>
/// </list>
/// </remarks>
public class ServiceProtocolTests
{
    private static MessageEnvelope EventEnvelope(string messageType, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Event,
            MessageType = messageType,
            SequenceNumber = 1,
            Payload = doc.RootElement.Clone()
        };
    }

    private static MessageEnvelope Request(string messageType, object payload)
    {
        return new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Request,
            MessageType = messageType,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Payload = ProtocolSerializer.SerializePayload(payload)
        };
    }

    private static string[] FieldNames(MessageEnvelope envelope) =>
        envelope.Payload.EnumerateObject().Select(p => p.Name).ToArray();

    // ─── Message type constants ───────────────────────────────────────

    [Fact]
    public void ServiceMessageTypes_AreStable()
    {
        Assert.Equal("room.state", ProtocolMessageTypes.RoomState);

        Assert.Equal("shop.snapshot.request", ProtocolMessageTypes.ShopSnapshotRequest);
        Assert.Equal("shop.snapshot", ProtocolMessageTypes.ShopSnapshot);
        Assert.Equal("shop.buy.request", ProtocolMessageTypes.ShopBuyRequest);
        Assert.Equal("shop.sell.request", ProtocolMessageTypes.ShopSellRequest);
        Assert.Equal("shop.result", ProtocolMessageTypes.ShopResult);

        Assert.Equal("bank.snapshot.request", ProtocolMessageTypes.BankSnapshotRequest);
        Assert.Equal("bank.snapshot", ProtocolMessageTypes.BankSnapshot);
        Assert.Equal("bank.deposit.request", ProtocolMessageTypes.BankDepositRequest);
        Assert.Equal("bank.withdraw.request", ProtocolMessageTypes.BankWithdrawRequest);
        Assert.Equal("bank.result", ProtocolMessageTypes.BankResult);

        Assert.Equal("door.action.request", ProtocolMessageTypes.DoorActionRequest);
        Assert.Equal("door.result", ProtocolMessageTypes.DoorResult);

        Assert.Equal("who.request", ProtocolMessageTypes.WhoRequest);
        Assert.Equal("who.snapshot", ProtocolMessageTypes.WhoSnapshot);
    }

    [Fact]
    public void NoHealerOrPartyMessageTypesExist()
    {
        var declared = typeof(ProtocolMessageTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        foreach (var forbidden in new[] { "healer.", "party." })
        {
            Assert.DoesNotContain(declared, t => t.StartsWith(forbidden, StringComparison.Ordinal));
        }
    }

    // ─── room.state ───────────────────────────────────────────────────

    [Fact]
    public void RoomState_ReadsNameDescriptionExitsAndServices()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.RoomState,
            """
            {
              "roomId": "42",
              "worldRoomId": "gc_t_healer",
              "name": "The Coldmarch Dispensary",
              "description": "Soft candlelight and the fragrance of healing herbs.",
              "exits": [
                {
                  "direction": "north", "name": "north", "displayName": "iron gate",
                  "isDoor": true, "isOpen": false, "isLocked": true, "hasLock": true,
                  "canOpen": false, "canClose": false, "canLock": false, "canUnlock": true
                },
                {
                  "direction": "south", "name": "south", "isDoor": false,
                  "isOpen": false, "isLocked": false, "hasLock": false,
                  "canOpen": false, "canClose": false, "canLock": false, "canUnlock": false
                }
              ],
              "services": { "shopIds": ["general_store"], "bank": true, "healingRoom": true }
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<RoomStatePayload>(envelope).Value;

        Assert.Equal("42", payload.RoomId);
        Assert.Equal("gc_t_healer", payload.WorldRoomId);
        Assert.Equal("The Coldmarch Dispensary", payload.Name);
        Assert.Contains("candlelight", payload.Description);

        Assert.Equal(2, payload.Exits.Count);
        var gate = payload.Exits[0];
        Assert.Equal("north", gate.Direction);
        Assert.Equal("iron gate", gate.DisplayName);
        Assert.True(gate.IsDoor);
        Assert.False(gate.IsOpen);
        Assert.True(gate.IsLocked);
        Assert.True(gate.CanUnlock);
        Assert.False(gate.CanOpen);

        // A plain exit is a plain exit, never a door with dead buttons.
        Assert.False(payload.Exits[1].IsDoor);

        Assert.True(payload.Services.HasShops);
        Assert.Equal(new[] { "general_store" }, payload.Services.ShopIds);
        Assert.True(payload.Services.Bank);
        Assert.True(payload.Services.HealingRoom);
    }

    [Fact]
    public void RoomState_RoomWithoutServices_ReportsNothingAvailable()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.RoomState,
            """
            {
              "name": "The Ashfield",
              "exits": [],
              "services": { "shopIds": [], "bank": false, "healingRoom": false }
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<RoomStatePayload>(envelope).Value;

        Assert.False(payload.Services.HasShops);
        Assert.False(payload.Services.Bank);
        Assert.False(payload.Services.HealingRoom);
        Assert.Empty(payload.Exits);
    }

    [Fact]
    public void DoorStateDisplay_SpellsStateOutInWords()
    {
        // A non-colour cue: the UI must be able to read the state as text.
        Assert.Equal("closed, unlocked",
            new RoomExitRecord { IsDoor = true }.DoorStateDisplay);
        Assert.Equal("open, unlocked",
            new RoomExitRecord { IsDoor = true, IsOpen = true }.DoorStateDisplay);
        Assert.Equal("closed, locked",
            new RoomExitRecord { IsDoor = true, IsLocked = true }.DoorStateDisplay);
        Assert.Equal("open passage",
            new RoomExitRecord { IsDoor = false }.DoorStateDisplay);
    }

    [Fact]
    public void RoomState_ExitRecord_CarriesNoDestination()
    {
        // Dynamic door state is restricted to the current room, so the record
        // must have no field that could disclose the room behind a door.
        var properties = typeof(RoomExitRecord)
            .GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Destination", "DestinationId", "To", "ToRoomId", "WorldRoomId" })
        {
            Assert.DoesNotContain(forbidden, properties);
        }
    }

    // ─── Shop ─────────────────────────────────────────────────────────

    [Fact]
    public void ShopSnapshot_ReadsServerComputedPricesAndStock()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.ShopSnapshot,
            """
            {
              "available": true,
              "message": "",
              "shops": [ { "shopId": "general_store", "name": "General Store" } ],
              "shop": {
                "shopId": "general_store",
                "name": "General Store",
                "wares": [
                  {
                    "itemId": "health_potion", "name": "Health Potion",
                    "description": "Restores vigour.", "category": "consumable",
                    "buyPrice": 500, "sellPrice": 250, "stock": null
                  },
                  {
                    "itemId": "rusty_sword", "name": "Rusty Sword",
                    "buyPrice": 1000, "sellPrice": 500, "stock": 0
                  }
                ]
              }
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<ShopSnapshotPayload>(envelope).Value;

        Assert.True(payload.Available);
        Assert.Single(payload.Shops);
        Assert.NotNull(payload.Shop);
        Assert.Equal(2, payload.Shop!.Wares.Count);

        var potion = payload.Shop.Wares[0];
        Assert.Equal(500, potion.BuyPrice);
        Assert.Equal(250, potion.SellPrice);
        // null stock genuinely means unlimited, and is not the same as 0.
        Assert.True(potion.UnlimitedStock);
        Assert.False(potion.SoldOut);
        Assert.True(potion.CanBuy);
        Assert.True(potion.CanSell);

        // Stock 0 means sold out, which is NOT unlimited.
        var sword = payload.Shop.Wares[1];
        Assert.Equal(0, sword.Stock);
        Assert.True(sword.SoldOut);
        Assert.False(sword.UnlimitedStock);
    }

    [Fact]
    public void ShopWare_NotSoldHere_HasNoPrice()
    {
        // null price means "this shop does not trade that item", which is
        // meaningfully different from a price of zero.
        var ware = new ShopWareRecord { BuyPrice = null, SellPrice = 0 };

        Assert.False(ware.CanBuy);
        Assert.True(ware.CanSell);
    }

    [Fact]
    public void ShopResult_CarriesAuthoritativeOutcome()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.ShopResult,
            """
            {
              "action": "buy", "shopId": "general_store", "success": false,
              "message": "Insufficient funds: need 500c, have 120c.",
              "itemId": "health_potion", "quantity": 1,
              "totalCost": 500, "newBalance": 120
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<ShopResultPayload>(envelope).Value;

        Assert.Equal("buy", payload.Action);
        Assert.False(payload.Success);
        Assert.Equal("Insufficient funds: need 500c, have 120c.", payload.Message);
        Assert.Equal("health_potion", payload.ItemId);
        Assert.Equal(120, payload.NewBalance);
    }

    [Fact]
    public void BuyRequest_SendsIntentOnly_NoPriceOrBalance()
    {
        var envelope = Request(
            ProtocolMessageTypes.ShopBuyRequest,
            new ShopBuyRequest("general_store", "health_potion", 2));

        var fields = FieldNames(envelope);
        Assert.Contains("shopId", fields);
        Assert.Contains("itemId", fields);
        Assert.Contains("quantity", fields);

        // Portal must never tell the server what something costs.
        foreach (var forbidden in new[] { "price", "buyPrice", "totalCost", "balance", "currency" })
        {
            Assert.DoesNotContain(forbidden, fields);
        }
    }

    [Fact]
    public void SellRequest_SendsIntentOnly()
    {
        var envelope = Request(
            ProtocolMessageTypes.ShopSellRequest,
            new ShopSellRequest("general_store", "health_potion", 1));

        foreach (var forbidden in new[] { "price", "sellPrice", "totalCost", "balance" })
        {
            Assert.DoesNotContain(forbidden, FieldNames(envelope));
        }
    }

    // ─── Bank ─────────────────────────────────────────────────────────

    [Fact]
    public void BankSnapshot_ReadsBothBalances()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.BankSnapshot,
            """
            { "available": true, "message": "", "carriedCurrency": 1200, "bankCurrency": 5000 }
            """);

        var payload = ProtocolSerializer.DeserializePayload<BankSnapshotPayload>(envelope).Value;

        Assert.True(payload.Available);
        Assert.Equal(1200, payload.CarriedCurrency);
        Assert.Equal(5000, payload.BankCurrency);
    }

    [Fact]
    public void BankSnapshot_HasNoItemStorageConcept()
    {
        // Keystone's bank is currency only, so no vault/item field may exist.
        var properties = typeof(BankSnapshotPayload)
            .GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Items", "Vault", "StoredItems", "ItemStorage" })
        {
            Assert.DoesNotContain(forbidden, properties);
        }
    }

    [Fact]
    public void BankResult_CarriesAuthoritativeOutcome()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.BankResult,
            """
            {
              "action": "withdraw", "success": false,
              "message": "Insufficient bank funds: need 900c, have 250c.",
              "amount": 900, "newBankCurrency": 250
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<BankResultPayload>(envelope).Value;

        Assert.Equal("withdraw", payload.Action);
        Assert.False(payload.Success);
        Assert.Equal(900, payload.Amount);
        Assert.Equal(250, payload.NewBankCurrency);
    }

    [Fact]
    public void BankRequests_SendOnlyAnAmount()
    {
        foreach (var (type, payload) in new (string, object)[]
        {
            (ProtocolMessageTypes.BankDepositRequest, new BankDepositRequest(500)),
            (ProtocolMessageTypes.BankWithdrawRequest, new BankWithdrawRequest(500)),
        })
        {
            Assert.Equal(new[] { "amount" }, FieldNames(Request(type, payload)));
        }
    }

    // ─── Doors ────────────────────────────────────────────────────────

    [Fact]
    public void DoorResult_CarriesAuthoritativeOutcome()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.DoorResult,
            """
            {
              "success": false, "direction": "north", "action": "unlock",
              "message": "You do not have the required key."
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<DoorResultPayload>(envelope).Value;

        Assert.False(payload.Success);
        Assert.Equal("north", payload.Direction);
        Assert.Equal("unlock", payload.Action);
        Assert.Equal("You do not have the required key.", payload.Message);
    }

    [Fact]
    public void DoorActionRequest_SendsOnlyDirectionAndAction()
    {
        var envelope = Request(
            ProtocolMessageTypes.DoorActionRequest,
            new DoorActionRequest("north", "open"));

        var fields = FieldNames(envelope);
        Assert.Equal(new[] { "direction", "action" }, fields);

        // Never a destination, a key, or an expected result.
        foreach (var forbidden in new[] { "destination", "keyId", "success", "expectedResult" })
        {
            Assert.DoesNotContain(forbidden, fields);
        }
    }

    [Fact]
    public void DoorResult_HasNoDestinationField()
    {
        var properties = typeof(DoorResultPayload)
            .GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Destination", "DestinationId", "RoomId", "WorldRoomId" })
        {
            Assert.DoesNotContain(forbidden, properties);
        }
    }

    // ─── WHO ──────────────────────────────────────────────────────────

    [Fact]
    public void WhoSnapshot_ReadsOnlyPublishedColumns()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.WhoSnapshot,
            """
            {
              "count": 1,
              "rows": [
                {
                  "name": "Hero", "level": 12, "race": "Human",
                  "profession": "Warrior", "faction": "Good", "guild": "", "sect": ""
                }
              ]
            }
            """);

        var payload = ProtocolSerializer.DeserializePayload<WhoSnapshotPayload>(envelope).Value;

        Assert.Equal(1, payload.Count);
        var row = Assert.Single(payload.Rows);
        Assert.Equal("Hero", row.Name);
        Assert.Equal(12, row.Level);
        Assert.Equal("Human", row.Race);
        Assert.Equal("Warrior", row.Profession);
        Assert.Equal("Good", row.Faction);
    }

    [Fact]
    public void WhoRecord_HasNoTitleField()
    {
        // Keystone has no runtime title system, so Portal must not bind one.
        var properties = typeof(WhoRecord)
            .GetProperties().Select(p => p.Name).ToArray();

        foreach (var forbidden in new[] { "Title", "Rank", "Prefix", "Honorific" })
        {
            Assert.DoesNotContain(forbidden, properties);
        }
    }

    [Fact]
    public void WhoRequest_HasTheRightEnvelopeShape()
    {
        var envelope = Request(ProtocolMessageTypes.WhoRequest, new WhoRequest());
        Assert.Equal(MessageCategory.Request, envelope.Category);
        Assert.Equal("who.request", envelope.MessageType);
    }
}
