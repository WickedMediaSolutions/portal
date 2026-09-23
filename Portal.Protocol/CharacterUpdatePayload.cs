namespace Portal.Protocol;

/// <summary>
/// Protocol V1 character update delta payload.
/// Pushed by the server via the <c>character.update</c> event to convey
/// incremental changes to character resources and progression.
///
/// Fields that have not changed are omitted (null).
/// A present null-able field with the value zero is a legitimate update to zero.
/// </summary>
public sealed class CharacterUpdatePayload
{
    /// <summary>Updated current hit points, or null if unchanged.</summary>
    public int? Hp { get; init; }

    /// <summary>Updated maximum hit points, or null if unchanged.</summary>
    public int? MaxHp { get; init; }

    /// <summary>Updated current mana points, or null if unchanged.</summary>
    public int? Mana { get; init; }

    /// <summary>Updated maximum mana points, or null if unchanged.</summary>
    public int? MaxMana { get; init; }

    /// <summary>Updated current stamina points, or null if unchanged.</summary>
    public int? Stamina { get; init; }

    /// <summary>Updated maximum stamina points, or null if unchanged.</summary>
    public int? MaxStamina { get; init; }

    /// <summary>Updated experience points, or null if unchanged.</summary>
    public int? Xp { get; init; }

    /// <summary>Updated XP required for next level, or null if unchanged.</summary>
    public int? XpForNextLevel { get; init; }

    /// <summary>Updated character level, or null if unchanged.</summary>
    public int? Level { get; init; }

    public CharacterUpdatePayload()
    {
    }

    public CharacterUpdatePayload(
        int? hp,
        int? maxHp,
        int? mana,
        int? maxMana,
        int? stamina,
        int? maxStamina,
        int? xp,
        int? xpForNextLevel,
        int? level)
    {
        Hp = hp;
        MaxHp = maxHp;
        Mana = mana;
        MaxMana = maxMana;
        Stamina = stamina;
        MaxStamina = maxStamina;
        Xp = xp;
        XpForNextLevel = xpForNextLevel;
        Level = level;
    }
}