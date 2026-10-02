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
}