namespace Portal.Protocol;

/// <summary>
/// Strongly-typed protocol version identifier.
/// Protocol V1 is represented as Major=1, Minor=0.
/// </summary>
public readonly struct ProtocolVersion : IEquatable<ProtocolVersion>
{
    public int Major { get; }
    public int Minor { get; }

    public static readonly ProtocolVersion V1 = new(1, 0);
    public static readonly ProtocolVersion Current = V1;

    public ProtocolVersion(int major, int minor)
    {
        if (major < 0)
            throw new ArgumentOutOfRangeException(nameof(major), "Major version must be non-negative.");
        if (minor < 0)
            throw new ArgumentOutOfRangeException(nameof(minor), "Minor version must be non-negative.");

        Major = major;
        Minor = minor;
    }

    public bool Equals(ProtocolVersion other) =>
        Major == other.Major && Minor == other.Minor;

    public override bool Equals(object? obj) =>
        obj is ProtocolVersion other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor);

    public override string ToString() =>
        $"{Major}.{Minor}";

    public static bool operator ==(ProtocolVersion left, ProtocolVersion right) =>
        left.Equals(right);

    public static bool operator !=(ProtocolVersion left, ProtocolVersion right) =>
        !left.Equals(right);
}