namespace Portal.Protocol;

/// <summary>
/// Payload for the server's authoritative <c>combat.result</c> event, emitted
/// exactly once for every combat attack request the server processed.
/// </summary>
/// <remarks>
/// A refused swing (dead attacker, dead target, PvP block) is reported as
/// <see cref="Valid"/> = false with an <see cref="Error"/> string. Clients must
/// never infer an outcome from the button press itself, and must never treat a
/// missing event as a successful hit.
/// </remarks>
public sealed class CombatResultPayload
{
    /// <summary>Identifier of the entity that was attacked.</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Whether the attack was allowed to resolve at all.</summary>
    public bool Valid { get; init; }

    /// <summary>Why the attack was refused, or null when it resolved.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the attack landed.</summary>
    public bool Hit { get; init; }

    /// <summary>The 1-100 attack roll, or null when the attack was refused.</summary>
    public int? Roll { get; init; }

    /// <summary>Damage before mitigation, or null when there was no hit.</summary>
    public int? RawDamage { get; init; }

    /// <summary>Damage actually applied to the target.</summary>
    public int ActualDamage { get; init; }

    /// <summary>Target hit points before the attack resolved.</summary>
    public int TargetHpBefore { get; init; }

    /// <summary>Target hit points after the attack resolved.</summary>
    public int TargetHpAfter { get; init; }

    /// <summary>Whether this attack killed the target.</summary>
    public bool TargetKilled { get; init; }

    public CombatResultPayload()
    {
    }
}
