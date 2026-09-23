namespace Nexus.Core;

/// <summary>
/// A strongly-typed identifier for game entities, providing type safety
/// over raw string or GUID identifiers shared across Nexus layers.
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>
{
    public string Value { get; }

    public EntityId(string value)
    {
        Value = value;
    }

    public bool Equals(EntityId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value;

    public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

    public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);

    public static implicit operator string(EntityId id) => id.Value;

    public static implicit operator EntityId(string value) => new(value);
}