namespace Portal.Protocol;

/// <summary>
/// Protocol V1 target update delta payload.
/// Pushed by the server via the <c>target.update</c> event to convey
/// incremental changes to the current target's resource or death state.
///
/// Null fields indicate no change since the last update.
/// </summary>
public sealed class TargetUpdatePayload
{
    /// <summary>Unique identifier of the targeted entity.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Updated current hit points, or null if unchanged.</summary>
    public int? Hp { get; init; }

    /// <summary>Updated maximum hit points, or null if unchanged.</summary>
    public int? MaxHp { get; init; }

    /// <summary>
    /// Whether the targeted entity is now dead.
    /// Null if death state has not changed.
    /// </summary>
    public bool? IsDead { get; init; }

    public TargetUpdatePayload()
    {
    }

    public TargetUpdatePayload(
        string targetId,
        int? hp,
        int? maxHp,
        bool? isDead)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        Hp = hp;
        MaxHp = maxHp;
        IsDead = isDead;
    }
}