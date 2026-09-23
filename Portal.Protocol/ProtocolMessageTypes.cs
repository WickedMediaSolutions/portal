namespace Portal.Protocol;

public static class ProtocolMessageTypes
{
    public const string HandshakeRequest = "handshake.request";

    public const string HandshakeResponse = "handshake.response";

    public const string AuthenticationRequest = "auth.request";

    public const string AuthenticationResponse = "auth.response";

    public const string LogoutRequest = "auth.logout";

    public const string SessionTerminated = "session.terminated";

    public const string CharacterSnapshot = "character.snapshot";

    public const string CharacterUpdate = "character.update";

    public const string TargetChanged = "target.changed";

    public const string TargetUpdate = "target.update";
}