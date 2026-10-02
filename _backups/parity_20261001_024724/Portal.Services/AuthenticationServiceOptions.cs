using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

public sealed class AuthenticationServiceOptions
{
    private Uri _serverUri;

    /// <summary>
    /// The WebSocket endpoint this client connects to.
    ///
    /// Mutable on purpose: the login bar's local-development toggle changes the
    /// selected endpoint between connection attempts, and every subsequent
    /// connect — including a reconnect after a dropped session — must resolve
    /// through <see cref="PortalEndpoints.Resolve"/> from the current selection
    /// rather than replaying whatever was chosen at construction time.
    /// </summary>
    public Uri ServerUri
    {
        get => _serverUri;
        set => _serverUri = value ?? throw new ArgumentNullException(nameof(value));
    }

    public string ClientName { get; }
    public string ClientVersion { get; }
    public IReadOnlyList<CapabilityInfo> Capabilities { get; }

    public AuthenticationServiceOptions(
        Uri serverUri,
        string clientName,
        string clientVersion,
        IReadOnlyList<CapabilityInfo> capabilities)
    {
        _serverUri = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
        ClientName = clientName ?? throw new ArgumentNullException(nameof(clientName));
        ClientVersion = clientVersion ?? throw new ArgumentNullException(nameof(clientVersion));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    /// <summary>
    /// Re-points this client at the endpoint for the given target, using the
    /// single authoritative resolver in <see cref="PortalEndpoints"/>.
    /// </summary>
    public void UseTarget(PortalEndpointTarget target) => ServerUri = PortalEndpoints.Resolve(target);
}