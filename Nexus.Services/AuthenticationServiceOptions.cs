using Nexus.Protocol;

namespace Nexus.Services;

public sealed class AuthenticationServiceOptions
{
    public Uri ServerUri { get; }
    public string ClientName { get; }
    public string ClientVersion { get; }
    public IReadOnlyList<CapabilityInfo> Capabilities { get; }

    public AuthenticationServiceOptions(
        Uri serverUri,
        string clientName,
        string clientVersion,
        IReadOnlyList<CapabilityInfo> capabilities)
    {
        ServerUri = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
        ClientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        ClientVersion = clientVersion ?? throw new ArgumentNullException(nameof(clientVersion));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }
}