namespace Portal.Protocol;

public sealed class HandshakeResponse
{
    public bool Accepted { get; init; } = false;
    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;
    public IReadOnlyList<CapabilityInfo> Capabilities { get; init; } = Array.Empty<CapabilityInfo>();
    public string? ErrorCode { get; init; } = null;
    public string? ErrorMessage { get; init; } = null;

    public HandshakeResponse()
    {
    }

    public HandshakeResponse(
        bool accepted,
        ProtocolVersion protocolVersion,
        IReadOnlyList<CapabilityInfo> capabilities,
        string? errorCode,
        string? errorMessage)
    {
        Accepted = accepted;
        ProtocolVersion = protocolVersion;
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}