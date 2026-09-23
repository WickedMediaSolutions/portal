namespace Nexus.Protocol;

/// <summary>
/// Represents a single capability or feature claim exchanged during handshake.
/// Each entry declares a feature name and the version supported by the claiming peer.
/// </summary>
public sealed class CapabilityInfo
{
    /// <summary>Name of the capability or feature (e.g., "authentication", "compression").</summary>
    public string Feature { get; init; } = string.Empty;

    /// <summary>Version string for the claimed feature (e.g., "1.0").</summary>
    public string Version { get; init; } = string.Empty;

    public CapabilityInfo()
    {
    }

    public CapabilityInfo(string feature, string version)
    {
        Feature = feature ?? throw new ArgumentNullException(nameof(feature));
        Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    public override string ToString() => $"{Feature}@{Version}";
}