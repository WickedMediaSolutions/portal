namespace Portal.Protocol;

/// <summary>
/// Payload for a Portal-generated combat skill activation sent to Keystone.
///
/// Contains only the authoritative SkillId and optional TargetId so Keystone
/// remains fully authoritative over skill metadata, cost, damage, and effects.
/// </summary>
public sealed class CombatSkillRequest
{
    /// <summary>
    /// Unique skill identifier (e.g. "acid_blast") matching Keystone's
    /// SKILL_REGISTRY key. Must be non-empty.
    /// </summary>
    public string SkillId { get; init; } = string.Empty;

    /// <summary>
    /// The stable Keystone database primary key of the intended target,
    /// serialised as a string. Null when the skill does not require a target.
    /// </summary>
    public string? TargetId { get; init; }

    public CombatSkillRequest()
    {
    }

    public CombatSkillRequest(string skillId, string? targetId)
    {
        SkillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
        TargetId = targetId;
    }
}