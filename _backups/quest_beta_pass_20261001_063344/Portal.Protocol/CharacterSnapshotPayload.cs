namespace Portal.Protocol;

/// <summary>
/// Protocol V1 character snapshot payload.
/// Represents a complete authoritative snapshot of a character's state
/// pushed by the server via the <c>character.snapshot</c> event.
/// </summary>
public sealed class CharacterSnapshotPayload
{
    /// <summary>Unique character identifier.</summary>
    public string CharacterId { get; init; } = string.Empty;

    /// <summary>Display name of the character.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Race identifier for this character.</summary>
    public string RaceId { get; init; } = string.Empty;

    /// <summary>Display name of the character's race.</summary>
    public string RaceName { get; init; } = string.Empty;

    /// <summary>Profession identifier for this character.</summary>
    public string ProfessionId { get; init; } = string.Empty;

    /// <summary>Display name of the character's profession.</summary>
    public string ProfessionName { get; init; } = string.Empty;

    /// <summary>Whether this character's profession supports mana.</summary>
    public bool HasMana { get; init; }

    /// <summary>Current character level.</summary>
    public int Level { get; init; }

    /// <summary>Current experience points.</summary>
    public int Xp { get; init; }

    /// <summary>Experience points required for the next level.</summary>
    public int XpForNextLevel { get; init; }

    /// <summary>Current hit points.</summary>
    public int Hp { get; init; }

    /// <summary>Maximum hit points.</summary>
    public int MaxHp { get; init; }

    /// <summary>Current mana points.</summary>
    public int Mana { get; init; }

    /// <summary>Maximum mana points.</summary>
    public int MaxMana { get; init; }

    /// <summary>Current stamina points.</summary>
    public int Stamina { get; init; }

    /// <summary>Maximum stamina points.</summary>
    public int MaxStamina { get; init; }

    public CharacterSnapshotPayload()
    {
    }

    public CharacterSnapshotPayload(
        string characterId,
        string name,
        string raceId,
        string raceName,
        string professionId,
        string professionName,
        bool hasMana,
        int level,
        int xp,
        int xpForNextLevel,
        int hp,
        int maxHp,
        int mana,
        int maxMana,
        int stamina,
        int maxStamina)
    {
        CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        RaceId = raceId ?? throw new ArgumentNullException(nameof(raceId));
        RaceName = raceName ?? throw new ArgumentNullException(nameof(raceName));
        ProfessionId = professionId ?? throw new ArgumentNullException(nameof(professionId));
        ProfessionName = professionName ?? throw new ArgumentNullException(nameof(professionName));
        HasMana = hasMana;
        Level = level;
        Xp = xp;
        XpForNextLevel = xpForNextLevel;
        Hp = hp;
        MaxHp = maxHp;
        Mana = mana;
        MaxMana = maxMana;
        Stamina = stamina;
        MaxStamina = maxStamina;
    }
}