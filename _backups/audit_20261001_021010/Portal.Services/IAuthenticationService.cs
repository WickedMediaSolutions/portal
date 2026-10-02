using Portal.Core.Results;
using Portal.Protocol;

namespace Portal.Services;

public interface IAuthenticationService
{
    AuthenticationState State { get; }

    SessionInfo? Session { get; }

    event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;

    Task<Result<SessionInfo>> AuthenticateAsync(
        string username,
        string password,
        CancellationToken ct = default);

    Task<Result> LogoutAsync(
        CancellationToken ct = default);
}