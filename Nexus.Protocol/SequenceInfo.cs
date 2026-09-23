namespace Nexus.Protocol;

/// <summary>
/// Sequence tracking information used for ordered event streams and resynchronization.
///
/// Each event in a sequenced stream carries a monotonically increasing
/// <see cref="SequenceNumber"/> and may optionally acknowledge the last
/// sequence number received from the peer.
/// </summary>
public readonly struct SequenceInfo : IEquatable<SequenceInfo>
{
    /// <summary>
    /// The monotonically increasing sequence number of this message
    /// within its stream. Must be positive.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// The last sequence number acknowledged from the peer.
    /// Null if no acknowledgement is being sent with this message.
    /// </summary>
    public long? Acknowledged { get; }

    public SequenceInfo(long sequenceNumber, long? acknowledged)
    {
        if (sequenceNumber <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber), "Sequence number must be positive.");

        SequenceNumber = sequenceNumber;
        Acknowledged = acknowledged;
    }

    public bool Equals(SequenceInfo other) =>
        SequenceNumber == other.SequenceNumber && Acknowledged == other.Acknowledged;

    public override bool Equals(object? obj) =>
        obj is SequenceInfo other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(SequenceNumber, Acknowledged);

    public override string ToString() =>
        Acknowledged.HasValue
            ? $"Seq={SequenceNumber}, Ack={Acknowledged}"
            : $"Seq={SequenceNumber}";

    public static bool operator ==(SequenceInfo left, SequenceInfo right) =>
        left.Equals(right);

    public static bool operator !=(SequenceInfo left, SequenceInfo right) =>
        !left.Equals(right);
}