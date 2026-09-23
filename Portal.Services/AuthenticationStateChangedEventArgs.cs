namespace Portal.Services;

public sealed class AuthenticationStateChangedEventArgs : EventArgs
{
    public AuthenticationState OldState { get; }
    public AuthenticationState NewState { get; }

    public AuthenticationStateChangedEventArgs(
        AuthenticationState oldState,
        AuthenticationState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}