namespace Nexus.Protocol;

public sealed class HandshakeRequest
{
    public string ClientName { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;
    public IReadOnlyList<CapabilityInfo> Capabilities { get; init; } = Array.Empty<CapabilityInfo>();

    public HandshakeRequest()
    {
    }

    public HandshakeRequest(
        string clientName,
        string clientVersion,
        ProtocolVersion protocolVersion,
        IReadOnlyList<CapabilityInfo> capabilities)
    {
        ClientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        ClientVersion = clientVersion ?? throw new ArgumentNullException(nameof(clientVersion));
        ProtocolVersion = protocolVersion;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }
}