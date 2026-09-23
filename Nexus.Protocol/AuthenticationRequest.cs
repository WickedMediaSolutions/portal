namespace Nexus.Protocol;

/// <summary>
/// Protocol V1 authentication request payload.
/// Sent by a client to initiate authenticated session establishment.
/// </summary>
public sealed class AuthenticationRequest
{
    /// <summary>Account username credential.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Account password credential.</summary>
    public string Password { get; init; } = string.Empty;

    public AuthenticationRequest()
    {
    }

    public AuthenticationRequest(string username, string password)
    {
        Username = username ?? throw new ArgumentNullException(nameof(username));
        Password = password ?? throw new ArgumentNullException(nameof(password));
    }
}