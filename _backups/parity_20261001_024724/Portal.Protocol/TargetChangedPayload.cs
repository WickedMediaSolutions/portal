namespace Portal.Protocol;

/// <summary>
/// Protocol V1 target changed event payload.
/// Pushed by the server via the <c>target.changed</c> event when the player's
/// current target changes, including when the target is cleared.
///
/// A cleared (no-current-target) state is represented by a null
/// <see cref="TargetId"/>; all other fields are also null in that case.
/// </summary>
public sealed class TargetChangedPayload
{
    /// <summary>
    /// Unique identifier of the targeted entity.
    /// Null when the target has been cleared (no current target).
    /// </summary>
    public string? TargetId { get; init; }

    /// <summary>Display name of the targeted entity, or null if no target.</summary>
    public string? Name { get; init; }

    /// <summary>Level of the targeted entity, or null if no target.</summary>
    public int? Level { get; init; }

    /// <summary>Current hit points of the targeted entity, or null if no target.</summary>
    public int? Hp { get; init; }

    /// <summary>Maximum hit points of the targeted entity, or null if no target.</summary>
    public int? MaxHp { get; init; }

    /// <summary>
    /// Identity classification of the targeted entity
    /// (e.g., race or monster type name), or null if no target.
    /// </summary>
    public string? Identity { get; init; }

    /// <summary>
    /// Whether the targeted entity is a mob (NPC / monster).
    /// Null when no target is selected.
    /// </summary>
    public bool? IsMob { get; init; }

    public TargetChangedPayload()
    {
    }

    public TargetChangedPayload(
        string? targetId,
        string? name,
        int? level,
        int? hp,
        int? maxHp,
        string? identity,
        bool? isMob)
    {
        TargetId = targetId;
        Name = name;
        Level = level;
        Hp = hp;
        MaxHp = maxHp;
        Identity = identity;
        IsMob = isMob;
    }
}