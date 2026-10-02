namespace Portal.Protocol;

/// <summary>
/// Protocol V1 character points snapshot payload.
/// Pushed by the server via the <c>character.points.snapshot</c> event to
/// convey the complete authoritative Character Point and permanent stat state.
/// </summary>
public sealed class CharacterPointsSnapshotPayload
{
    /// <summary>
    /// The character's unspent Character Points. Always the server's own
    /// value — Portal never supplies or trusts a client-side balance.
    /// </summary>
    public int AvailablePoints { get; init; }

    /// <summary>
    /// How many points Keystone awards per character level gained
    /// (Keystone's CHARACTER_POINTS_PER_LEVEL constant). Display only.
    /// </summary>
    public int PointsPerLevel { get; init; }

    /// <summary>
    /// The complete authoritative list of allocatable stats with their
    /// current values and caps. Replacement semantics — always the full list.
    /// </summary>
    public IReadOnlyList<AllocatableStatRecord> Stats { get; init; } =
        Array.Empty<AllocatableStatRecord>();

    /// <summary>
    /// Set when Keystone rejected an allocation request. Null on every
    /// ordinary state refresh. The accompanying state is still authoritative.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>Whether the last allocation request was rejected.</summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    public CharacterPointsSnapshotPayload()
    {
    }

    public CharacterPointsSnapshotPayload(
        int availablePoints,
        int pointsPerLevel,
        IReadOnlyList<AllocatableStatRecord> stats,
        string? error = null)
    {
        AvailablePoints = availablePoints;
        PointsPerLevel = pointsPerLevel;
        Stats = stats ?? throw new ArgumentNullException(nameof(stats));
        Error = error;
    }
}
