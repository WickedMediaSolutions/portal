namespace Portal.Services;

public enum AuthenticationState
{
    Unauthenticated = 0,
    Authenticating = 1,
    Authenticated = 2,
    EstablishingSession = 3,
    SessionActive = 4,
    Reconnecting = 5,
    Failed = 6
}