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
}