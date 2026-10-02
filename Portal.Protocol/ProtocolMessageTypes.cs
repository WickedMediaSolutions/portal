namespace Portal.Protocol;

public static class ProtocolMessageTypes
{
    public const string HandshakeRequest = "handshake.request";

    public const string HandshakeResponse = "handshake.response";

    public const string AuthenticationRequest = "auth.request";

    public const string AuthenticationResponse = "auth.response";

    public const string LogoutRequest = "auth.logout";

    public const string SessionTerminated = "session.terminated";

    public const string CharacterSnapshot = "character.snapshot";

    public const string CharacterUpdate = "character.update";

    public const string TargetChanged = "target.changed";

    public const string TargetUpdate = "target.update";

    public const string CombatAttackRequest = "combat.attack.request";

    /// <summary>
    /// Authoritative outcome of a combat attack, emitted exactly once per
    /// <c>combat.attack.request</c> the server processed.
    ///
    /// The server projects this straight from its combat result, so a
    /// refused swing (dead attacker, dead target, PvP block) arrives as
    /// <c>valid=false</c> plus an <c>error</c> string rather than as silence.
    /// Portal must never infer an outcome from the button press itself.
    /// </summary>
    public const string CombatResult = "combat.result";

    // ─── Death recovery ──────────────────────────────────────────
    // A character that dies has no way back to play unless the client can
    // ask for it.  This is a thin intent message: Keystone owns the entire
    // recovery decision and the client only renders the result.

    /// <summary>Ask the server to run the authoritative respawn.</summary>
    public const string CharacterRespawnRequest = "character.respawn.request";

    /// <summary>Authoritative respawn outcome, successful or refused.</summary>
    public const string CharacterRespawnResult = "character.respawn.result";

    public const string CombatSkillRequest = "combat.skill.request";

    public const string TargetSelectRequest = "target.select.request";

    public const string RoomEntitySnapshot = "room.entity.snapshot";

    public const string CharacterSkillsSnapshot = "character.skills.snapshot";

    public const string CharacterStatusSnapshot = "character.status.snapshot";

    public const string EquipmentEquipRequest = "equipment.equip.request";

    public const string EquipmentUnequipRequest = "equipment.unequip.request";

    public const string InventorySnapshot = "inventory.snapshot";

    public const string EquipmentSnapshot = "equipment.snapshot";

    public const string MovementDirectionRequest = "movement.direction.request";

    public const string MovementFailed = "movement.failed";

    public const string CharacterPointsSnapshot = "character.points.snapshot";

    public const string CharacterPointsAllocateRequest =
        "character.points.allocate.request";

    // ─── Map / fog-of-war events ───────────────────────────────────────
    // These are server->client only. Keystone filters every map payload before
    // transmission (see Keystone world.data.character_map), so Portal never
    // receives the whole realm map and must never try to reconstruct it.

    /// <summary>Complete filtered map state for the authenticated character.</summary>
    public const string MapSnapshot = "map.snapshot";

    /// <summary>One newly discovered room after a genuine arrival.</summary>
    public const string MapRoomDiscovered = "map.room.discovered";

    /// <summary>The character's current room/area/position/floor.</summary>
    public const string MapPosition = "map.position";

    /// <summary>Discovery counters (no geography).</summary>
    public const string MapState = "map.state";

    // ─── Room state / service context ──────────────────────────────
    // The authoritative description of the room the character is actually
    // standing in. Deliberately separate from the map payloads: the map is
    // fog-filtered navigational geometry, while this is the current-room
    // "look" a graphical MUD client needs to render the room, its exits, its
    // observable door state and the services it offers.

    /// <summary>Live room name, description, exits, doors and services.</summary>
    public const string RoomState = "room.state";

    // ─── Shop ──────────────────────────────────────────────────────

    /// <summary>Request the room's shops, or one shop's authoritative wares.</summary>
    public const string ShopSnapshotRequest = "shop.snapshot.request";

    /// <summary>Shop list and/or wares with server-computed prices and stock.</summary>
    public const string ShopSnapshot = "shop.snapshot";

    /// <summary>Buy a quantity of an item from a room-linked shop.</summary>
    public const string ShopBuyRequest = "shop.buy.request";

    /// <summary>Sell a quantity of an item to a room-linked shop.</summary>
    public const string ShopSellRequest = "shop.sell.request";

    /// <summary>Authoritative buy/sell outcome, successful or not.</summary>
    public const string ShopResult = "shop.result";

    // ─── Bank ──────────────────────────────────────────────────────
    // Keystone's bank is currency only: deposit / withdraw copper. There is
    // no item storage, so no item-banking message types exist.

    /// <summary>Request carried + stored copper balances.</summary>
    public const string BankSnapshotRequest = "bank.snapshot.request";

    /// <summary>Authoritative carried and banked copper.</summary>
    public const string BankSnapshot = "bank.snapshot";

    /// <summary>Move copper from the pack into the bank.</summary>
    public const string BankDepositRequest = "bank.deposit.request";

    /// <summary>Move copper from the bank into the pack.</summary>
    public const string BankWithdrawRequest = "bank.withdraw.request";

    /// <summary>Authoritative deposit/withdraw outcome.</summary>
    public const string BankResult = "bank.result";

    // ─── Doors ─────────────────────────────────────────────────────
    // Dynamic door state travels only in RoomState, restricted to doors
    // genuinely observable from the current room. The canonical map keeps
    // its static door marker untouched.

    /// <summary>Open, close, lock or unlock a door in a given direction.</summary>
    public const string DoorActionRequest = "door.action.request";

    /// <summary>Authoritative door action outcome.</summary>
    public const string DoorResult = "door.result";

    // ─── WHO ───────────────────────────────────────────────────────

    /// <summary>Request the list of connected characters.</summary>
    public const string WhoRequest = "who.request";

    /// <summary>Connected characters, using only server-published fields.</summary>
    public const string WhoSnapshot = "who.snapshot";

    // ─── Quests ─────────────────────────────────────────────────────
    // Keystone is the single source of truth for every quest fact. Portal
    // sends only intent (a quest id) and a verb, and renders exactly the
    // server-reported state, availability, readiness and rewards. It never
    // computes whether a quest can be accepted, abandoned or completed, and
    // never infers success from a button press.

    /// <summary>Explicitly re-request the authoritative quest state.</summary>
    public const string QuestSnapshotRequest = "quest.snapshot.request";

    /// <summary>
    /// Full quest state for the authenticated character. Pushed automatically
    /// on every successful authentication, so a reconnect restores active
    /// quests, objective progress, ready state and completed history with no
    /// further action from the player.
    /// </summary>
    public const string QuestSnapshot = "quest.snapshot";

    /// <summary>Ask the server to accept a quest.</summary>
    public const string QuestAcceptRequest = "quest.accept.request";

    /// <summary>Ask the server to abandon an active quest.</summary>
    public const string QuestAbandonRequest = "quest.abandon.request";

    /// <summary>Ask the server to complete a ready quest.</summary>
    public const string QuestCompleteRequest = "quest.complete.request";

    /// <summary>
    /// Authoritative accept / abandon / complete outcome, successful or not.
    /// Portal must never treat a button press as success.
    /// </summary>
    public const string QuestResult = "quest.result";

    /// <summary>
    /// Incremental objective progress, emitted only when a kill or an item
    /// acquisition actually advanced a quest, so the UI updates immediately
    /// without re-requesting a full snapshot.
    /// </summary>
    public const string QuestProgress = "quest.progress";

    // ─── Deliberately NOT defined ──────────────────────────────────
    // No healer.* types: a Keystone healing_room is a passive regeneration
    // multiplier, not a command with a price and a result, so there is
    // nothing for a client to invoke.
    //
    // No party.* types: Keystone has no party system.
}