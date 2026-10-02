namespace Portal.Protocol;

/// <summary>
/// Payload for a Portal-generated combat attack command sent to Keystone.
///
/// Contains only the authoritative target identifier so Keystone remains
/// fully authoritative over damage, hit chance, combat formulas, death, etc.
/// </summary>
public sealed class CombatAttackRequest
{
    /// <summary>
    /// The stable Keystone database primary key of the intended target,
    /// serialised as a string.
    /// </summary>
    public string TargetId { get; init; } = string.Empty;

    public CombatAttackRequest()
    {
    }

    public CombatAttackRequest(string targetId)
    {
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }
}