namespace Portal.Protocol;

/// <summary>
/// Protocol V1 character skills snapshot payload.
/// Pushed by the server via the <c>character.skills.snapshot</c> event to convey
/// the complete authoritative list of skills unlocked by the authenticated character.
/// </summary>
public sealed class CharacterSkillsSnapshotPayload
{
    /// <summary>
    /// The complete list of skill records for this character.
    /// Replacement semantics — this is the full authoritative list.
    /// </summary>
    public IReadOnlyList<SkillRecord> Skills { get; init; } = Array.Empty<SkillRecord>();

    public CharacterSkillsSnapshotPayload()
    {
    }

    public CharacterSkillsSnapshotPayload(IReadOnlyList<SkillRecord> skills)
    {
        Skills = skills ?? throw new ArgumentNullException(nameof(skills));
    }
}

/// <summary>
/// Protocol V1 individual skill record.
/// Mirrors Keystone's SkillDefinition metadata for a single unlocked skill/spell.
/// </summary>
public sealed class SkillRecord
{
    /// <summary>Unique skill identifier (e.g. "acid_blast").</summary>
    public string SkillId { get; init; } = string.Empty;

    /// <summary>Display name of the skill.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short description of the skill's effect.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Skill category (universal, profession, weapon).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Resource cost type ("mana", "stamina", or null for no cost).</summary>
    public string? CostType { get; init; }

    /// <summary>Resource amount consumed when using the skill.</summary>
    public int CostAmount { get; init; }

    /// <summary>Whether this skill requires a target to use.</summary>
    public bool TargetRequired { get; init; }

    /// <summary>Whether the target must be an enemy.</summary>
    public bool TargetEnemy { get; init; }

    /// <summary>Whether this is a passive skill (not activatable).</summary>
    public bool IsPassive { get; init; }

    public SkillRecord()
    {
    }

    public SkillRecord(
        string skillId,
        string name,
        string description,
        string category,
        string? costType,
        int costAmount,
        bool targetRequired,
        bool targetEnemy,
        bool isPassive)
    {
        SkillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        CostType = costType;
        CostAmount = costAmount;
        TargetRequired = targetRequired;
        TargetEnemy = targetEnemy;
        IsPassive = isPassive;
    }
}