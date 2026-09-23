using Nexus.Core.Results;
using Nexus.Networking;
using Nexus.Protocol;

namespace Nexus.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IWebSocketConnection _connection;
    private readonly AuthenticationServiceOptions _options;
    private readonly object _stateLock = new();

    private AuthenticationState _state = AuthenticationState.Unauthenticated;
    private SessionInfo? _session;

    public AuthenticationState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public SessionInfo? Session
    {
        get
        {
            lock (_stateLock)
            {
                return _session;
            }
        }
    }

    public event EventHandler<AuthenticationStateChangedEventArgs>? StateChanged;
public AuthenticationService(
        IWebSocketConnection connection,
        AuthenticationServiceOptions options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<Result<SessionInfo>> AuthenticateAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(username))
            return Result<SessionInfo>.Failure(
                "AUTH_INVALID_USERNAME",
                "Username is required.");

        if (string.IsNullOrEmpty(password))
            return Result<SessionInfo>.Failure(
                "AUTH_INVALID_PASSWORD",
                "Password is required.");

        lock (_stateLock)
        {
            if (_state != AuthenticationState.Unauthenticated && _state != AuthenticationState.Failed)
            {
                return Result<SessionInfo>.Failure(
                    "AUTH_INVALID_STATE",
                    "Authentication cannot begin from the current state.");
            }
        }

        lock (_stateLock)
        {
            _session = null;
        }

        SetState(AuthenticationState.Authenticating);

        if (_connection.State == ConnectionState.Disconnected || _connection.State == ConnectionState.Faulted)
        {
            var connectResult = await _connection.ConnectAsync(_options.ServerUri, ct);
            if (connectResult.IsFailure)
            {
                SetState(AuthenticationState.Failed);
                return Result<SessionInfo>.Failure(connectResult.Errors);
            }
        }

        if (_connection.State != ConnectionState.Connected)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "AUTH_CONNECTION_UNAVAILABLE",
                "A connection to ROP is not available.");
        }
// Handshake
        var handshakeCorrelationId = Guid.NewGuid().ToString("N");

        var handshakeRequest = new HandshakeRequest(
            _options.ClientName,
            _options.ClientVersion,
            ProtocolVersion.Current,
            _options.Capabilities);

        var handshakeEnvelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.HandshakeRequest,
            handshakeCorrelationId,
            null,
            ProtocolSerializer.SerializePayload(handshakeRequest));

        var sendHandshakeResult = await _connection.SendAsync(handshakeEnvelope, ct);
        if (sendHandshakeResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(sendHandshakeResult.Errors);
        }

        var handshakeReceiveResult = await _connection.ReceiveAsync(ct);
        if (handshakeReceiveResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(handshakeReceiveResult.Errors);
        }

        var handshakeEnvelopeResponse = handshakeReceiveResult.Value;

        if (handshakeEnvelopeResponse.Category != MessageCategory.Response ||
            handshakeEnvelopeResponse.MessageType != ProtocolMessageTypes.HandshakeResponse ||
            handshakeEnvelopeResponse.CorrelationId != handshakeCorrelationId)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "HANDSHAKE_INVALID_RESPONSE",
                "ROP returned an invalid handshake response.");
        }

        var handshakeDeserializeResult = ProtocolSerializer.DeserializePayload<HandshakeResponse>(handshakeEnvelopeResponse);
        if (handshakeDeserializeResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(handshakeDeserializeResult.Errors);
        }

        var handshakeResponse = handshakeDeserializeResult.Value;

        if (!handshakeResponse.Accepted)
        {
            SetState(AuthenticationState.Failed);

            var rejectCode = !string.IsNullOrEmpty(handshakeResponse.ErrorCode) ? handshakeResponse.ErrorCode! : "HANDSHAKE_REJECTED";
            var rejectMessage = !string.IsNullOrEmpty(handshakeResponse.ErrorMessage) ? handshakeResponse.ErrorMessage! : "ROP rejected the Nexus handshake.";
            return Result<SessionInfo>.Failure(rejectCode, rejectMessage);
        }

        if (handshakeResponse.ProtocolVersion != ProtocolVersion.Current)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "HANDSHAKE_PROTOCOL_MISMATCH",
                "ROP accepted an incompatible Nexus protocol version.");
        }
// Authentication
        var authCorrelationId = Guid.NewGuid().ToString("N");

        var authenticationRequest = new AuthenticationRequest(username, password);

        var authEnvelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.AuthenticationRequest,
            authCorrelationId,
            null,
            ProtocolSerializer.SerializePayload(authenticationRequest));

        var sendAuthResult = await _connection.SendAsync(authEnvelope, ct);
        if (sendAuthResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(sendAuthResult.Errors);
        }

        var authReceiveResult = await _connection.ReceiveAsync(ct);
        if (authReceiveResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(authReceiveResult.Errors);
        }

        var authEnvelopeResponse = authReceiveResult.Value;

        if (authEnvelopeResponse.Category != MessageCategory.Response ||
            authEnvelopeResponse.MessageType != ProtocolMessageTypes.AuthenticationResponse ||
            authEnvelopeResponse.CorrelationId != authCorrelationId)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "AUTH_INVALID_RESPONSE",
                "ROP returned an invalid authentication response.");
        }

        var authDeserializeResult = ProtocolSerializer.DeserializePayload<AuthenticationResponse>(authEnvelopeResponse);
        if (authDeserializeResult.IsFailure)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(authDeserializeResult.Errors);
        }

        var authenticationResponse = authDeserializeResult.Value;

        if (!authenticationResponse.Success)
        {
            SetState(AuthenticationState.Failed);

            var authRejectCode = !string.IsNullOrEmpty(authenticationResponse.ErrorCode) ? authenticationResponse.ErrorCode! : "AUTH_REJECTED";
            var authRejectMessage = !string.IsNullOrEmpty(authenticationResponse.ErrorMessage) ? authenticationResponse.ErrorMessage! : "ROP rejected the authentication request.";
            return Result<SessionInfo>.Failure(authRejectCode, authRejectMessage);
        }

        SetState(AuthenticationState.Authenticated);
        SetState(AuthenticationState.EstablishingSession);

        if (authenticationResponse.Session is null)
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "AUTH_SESSION_MISSING",
                "ROP authenticated the account but did not return session information.");
        }

        if (string.IsNullOrEmpty(authenticationResponse.Session.SessionId))
        {
            SetState(AuthenticationState.Failed);
            return Result<SessionInfo>.Failure(
                "AUTH_SESSION_INVALID",
                "ROP returned invalid session information.");
        }

        lock (_stateLock)
        {
            _session = authenticationResponse.Session;
        }

        SetState(AuthenticationState.SessionActive);

        return Result<SessionInfo>.Success(authenticationResponse.Session);
    }
public async Task<Result> LogoutAsync(
        CancellationToken ct = default)
    {
        SessionInfo? session;

        lock (_stateLock)
        {
            if (_state != AuthenticationState.SessionActive || _session is null)
            {
                return Result.Failure(
                    "LOGOUT_INVALID_STATE",
                    "There is no active ROP session to log out.");
            }

            session = _session;
        }

        var logoutRequest = new LogoutRequest(session.SessionId);

        var logoutEnvelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.LogoutRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(logoutRequest));

        var sendResult = await _connection.SendAsync(logoutEnvelope, ct);
        if (sendResult.IsFailure)
        {
            return Result.Failure(sendResult.Errors);
        }

        var disconnectResult = await _connection.DisconnectAsync(ct);
        if (disconnectResult.IsFailure)
        {
            return Result.Failure(disconnectResult.Errors);
        }

        lock (_stateLock)
        {
            _session = null;
        }

        SetState(AuthenticationState.Unauthenticated);

        return Result.Success();
    }

    private void SetState(AuthenticationState newState)
    {
        AuthenticationState oldState;

        lock (_stateLock)
        {
            if (_state == newState)
            {
                return;
            }

            oldState = _state;
            _state = newState;
        }

        StateChanged?.Invoke(this, new AuthenticationStateChangedEventArgs(oldState, newState));
    }
}