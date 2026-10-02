namespace Portal.Protocol;

/// <summary>
/// A single permanent character stat as reported by Keystone's authoritative
/// <c>character.points.snapshot</c>.
/// </summary>
public sealed class AllocatableStatRecord
{
    /// <summary>Canonical Keystone stat id (e.g. "str", "con").</summary>
    public string StatId { get; init; } = string.Empty;

    /// <summary>Player-facing display name (e.g. "Strength").</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The character's current permanent value.</summary>
    public int Value { get; init; }

    /// <summary>The authoritative maximum this stat may reach.</summary>
    public int Cap { get; init; }

    /// <summary>Whether the stat has reached its cap and cannot be raised.</summary>
    public bool IsCapped => Value >= Cap;

    public AllocatableStatRecord()
    {
    }

    public AllocatableStatRecord(string statId, string name, int value, int cap)
    {
        StatId = statId ?? throw new ArgumentNullException(nameof(statId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Value = value;
        Cap = cap;
    }
}
