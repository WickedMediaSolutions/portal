namespace Portal.Networking;

/// <summary>
/// Identifies which ROP deployment Portal's WebSocket client connects to.
///
/// Production is always the default: a player who never touches any setting
/// talks to the live game. Local development is strictly opt-in.
/// </summary>
public enum PortalEndpointTarget
{
    /// <summary>The live public deployment (secure transport).</summary>
    Production = 0,

    /// <summary>A developer machine running Keystone locally (plaintext).</summary>
    LocalDevelopment = 1,
}

/// <summary>
/// The single, authoritative source of Portal's WebSocket endpoints.
///
/// There is exactly one endpoint definition in Portal and it lives here.
/// Nothing else in the client constructs a WebSocket URI, so there can be no
/// second, competing endpoint system and no code path that quietly prefers
/// localhost over the real server.
///
/// Shape of both endpoints mirrors what the server actually speaks.
/// Keystone's Portal bridge is a raw <c>autobahn</c> <c>WebSocketServerFactory</c>
/// bound straight to a TCP port by <c>start_portal_bridge_service</c> — it is NOT
/// mounted anywhere in Evennia's web resource tree, so it has no URL path at
/// all. The endpoint is therefore scheme + authority only (path
/// <c>/</c>), exactly like the <c>ws://localhost:4013</c> value the client
/// already used. No path is invented here.
///
/// Transport rules:
/// <list type="bullet">
///   <item>Production always uses <c>wss://</c>. There is no plaintext
///   production variant and no insecure fallback.</item>
///   <item>Local development uses <c>ws://</c> on the loopback host, because
///   a developer's Keystone listener is not TLS terminated.</item>
/// </list>
/// TLS validation is never disabled: the client simply never asks for
/// <c>ws://</c> against a non-loopback authority.
/// </summary>
public static class PortalEndpoints
{
    /// <summary>Public production host.</summary>
    public const string ProductionHost = "ritesrpg.com";

    /// <summary>Loopback host used for local development.</summary>
    public const string LocalDevelopmentHost = "localhost";

    /// <summary>
    /// Keystone's Portal protocol bridge port (<c>PORTAL_PROTOCOL_PORT</c>).
    /// Deliberately not the Evennia webclient port (4012).
    /// </summary>
    public const int LocalDevelopmentPort = 4013;

    /// <summary>Short label shown in the connection target indicator.</summary>
    public const string ProductionDisplayName = "Production";

    /// <summary>Short label shown in the connection target indicator.</summary>
    public const string LocalDevelopmentDisplayName = "Local Development";

    /// <summary>
    /// The target Portal uses when the user has expressed no preference.
    /// Production, always: local development must be explicitly requested.
    /// </summary>
    public const PortalEndpointTarget DefaultTarget = PortalEndpointTarget.Production;

    /// <summary>
    /// Builds the endpoint URI for a target.  This is the only place in the
    /// client where a WebSocket URI is constructed.
    /// </summary>
    public static Uri Resolve(PortalEndpointTarget target) => target switch
    {
        PortalEndpointTarget.LocalDevelopment =>
            new Uri($"ws://{LocalDevelopmentHost}:{LocalDevelopmentPort}/"),

        PortalEndpointTarget.Production =>
            new Uri($"wss://{ProductionHost}/"),

        _ => throw new ArgumentOutOfRangeException(
            nameof(target), target, "Unknown Portal endpoint target."),
    };

    /// <summary>
    /// The concise, port-free label for the connection target indicator, so
    /// ordinary players never have to read a URL or a port number.
    /// </summary>
    public static string Describe(PortalEndpointTarget target) => target switch
    {
        PortalEndpointTarget.LocalDevelopment => LocalDevelopmentDisplayName,
        PortalEndpointTarget.Production => ProductionDisplayName,
        _ => throw new ArgumentOutOfRangeException(
            nameof(target), target, "Unknown Portal endpoint target."),
    };

    /// <summary>
    /// Maps the persisted client toggle onto an endpoint target.
    /// OFF (the default) is production; ON is local development.
    /// </summary>
    public static PortalEndpointTarget FromToggle(bool useLocalDevelopmentServer) =>
        useLocalDevelopmentServer
            ? PortalEndpointTarget.LocalDevelopment
            : PortalEndpointTarget.Production;

    /// <summary>The inverse of <see cref="FromToggle"/>, for persistence.</summary>
    public static bool ToToggle(PortalEndpointTarget target) =>
        target == PortalEndpointTarget.LocalDevelopment;

    /// <summary>
    /// True when a resolved endpoint uses plaintext <c>ws://</c>.
    ///
    /// Permitted only for the loopback development host. This exists so the
    /// "no insecure production fallback" rule can be asserted mechanically
    /// rather than by reading the switch expression.
    /// </summary>
    public static bool IsPlaintextTransport(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase);
    }
}