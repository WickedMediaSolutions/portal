namespace Portal.Protocol;

/// <summary>
/// A single proposed stat allocation in a
/// <c>character.points.allocate.request</c>.
/// </summary>
public sealed class StatAllocationEntry
{
    /// <summary>Canonical Keystone stat id (e.g. "str", "con").</summary>
    public string StatId { get; init; } = string.Empty;

    /// <summary>Number of points to spend on this stat. Must be positive.</summary>
    public int Amount { get; init; }

    public StatAllocationEntry()
    {
    }

    public StatAllocationEntry(string statId, int amount)
    {
        StatId = statId ?? throw new ArgumentNullException(nameof(statId));
        Amount = amount;
    }
}

/// <summary>
/// Protocol V1 character points allocation request.
/// Sent by Portal to propose a complete, atomic stat allocation. Keystone
/// validates the whole set together and either applies all of it or none of it,
/// then replies with a <c>character.points.snapshot</c> carrying the resulting
/// authoritative state (plus an <c>error</c> when the request was rejected).
/// </summary>
public sealed class CharacterPointsAllocateRequest
{
    /// <summary>
    /// The complete proposed allocation. Portal may include several stats;
    /// the total cost must not exceed the character's unspent balance.
    /// </summary>
    public IReadOnlyList<StatAllocationEntry> Allocations { get; init; } =
        Array.Empty<StatAllocationEntry>();

    public CharacterPointsAllocateRequest()
    {
    }

    public CharacterPointsAllocateRequest(IReadOnlyList<StatAllocationEntry> allocations)
    {
        Allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
    }
}
