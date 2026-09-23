using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Portal.Core.Results;
using Portal.Protocol;

namespace Portal.Networking;

/// <summary>
/// Default implementation of <see cref="IWebSocketConnection"/> using
/// the .NET <see cref="ClientWebSocket"/>.
///
/// <para>Thread safety:</para>
/// <list type="bullet">
/// <item>Sends are serialized via an internal semaphore (WebSocket requirement).</item>
/// <item>Receives flow through a bounded <see cref="Channel{T}"/> consumed by callers.</item>
/// <item>State transitions are guarded by a lock and reported via <see cref="StateChanged"/>.</item>
/// </list>
///
/// <para>Lifecycle:</para>
/// <list type="bullet">
/// <item><see cref="ConnectAsync"/> starts the receive loop and heartbeat.</item>
/// <item><see cref="DisconnectAsync"/> sends a close frame, cancels the loop, and cleans up.</item>
/// <item>Call <see cref="DisposeAsync"/> to release all resources when done.</item>
/// </list>
/// </summary>
public sealed class WebSocketConnection : IWebSocketConnection
{
    private readonly WebSocketConnectionOptions _options;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _connectionCts;
    private Channel<MessageEnvelope>? _receiveChannel;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _receiveLoopTask;
    private ConnectionState _state = ConnectionState.Disconnected;
    private readonly object _stateLock = new();
    private Uri? _remoteUri;
    private bool _disposed;

    /// <inheritdoc />
    public ConnectionState State
    {
        get { lock (_stateLock) return _state; }
    }

    /// <inheritdoc />
    public Uri? RemoteUri => _remoteUri;

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Creates a new <see cref="WebSocketConnection"/> with the specified options.
    /// </summary>
    public WebSocketConnection(WebSocketConnectionOptions? options = null)
    {
        _options = options ?? new WebSocketConnectionOptions();
    }
// ─── Connect ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result> ConnectAsync(Uri uri, CancellationToken ct = default)
    {
        if (_disposed)
            return Result.Failure("CONNECTION_DISPOSED", "Connection has been disposed.");

        var currentState = State;
        if (currentState != ConnectionState.Disconnected && currentState != ConnectionState.Faulted)
            return Result.Failure("INVALID_STATE",
                $"Cannot connect from state '{currentState}'. Must be Disconnected or Faulted.");

        if (uri is null)
            return Result.Failure("INVALID_URI", "URI cannot be null.");

        if (uri.Scheme != "ws" && uri.Scheme != "wss")
            return Result.Failure("INVALID_URI",
                $"Unsupported URI scheme '{uri.Scheme}'. Expected 'ws' or 'wss'.");

        TransitionState(ConnectionState.Connecting);
        _remoteUri = uri;

        _connectionCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _connectionCts.Token);

        try
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = _options.HeartbeatInterval;

            using var timeoutCts = new CancellationTokenSource(_options.ConnectTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                linkedCts.Token, timeoutCts.Token);

            await _webSocket.ConnectAsync(uri, combinedCts.Token).ConfigureAwait(false);

            _receiveChannel = Channel.CreateBounded<MessageEnvelope>(
                new BoundedChannelOptions(_options.ReceiveChannelCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait
                });

            TransitionState(ConnectionState.Connected);

            _receiveLoopTask = RunReceiveLoopAsync(linkedCts.Token);

            return Result.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await CleanupResourcesAsync().ConfigureAwait(false);
            TransitionState(ConnectionState.Disconnected);
            return Result.Failure("CONNECTION_CANCELLED", "Connection was cancelled by caller.");
        }
        catch (OperationCanceledException)
        {
            await CleanupResourcesAsync().ConfigureAwait(false);
            TransitionState(ConnectionState.Disconnected);
            return Result.Failure("CONNECTION_TIMEOUT",
                $"Connection to '{uri}' timed out after {_options.ConnectTimeout.TotalSeconds:F0}s.");
        }
        catch (WebSocketException ex)
        {
            await CleanupResourcesAsync().ConfigureAwait(false);
            TransitionState(ConnectionState.Faulted);
            return Result.Failure("CONNECTION_FAILED", ex.Message);
        }
    }
// ─── Disconnect ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result> DisconnectAsync(CancellationToken ct = default)
    {
        var currentState = State;
        if (currentState != ConnectionState.Connected && currentState != ConnectionState.Connecting)
            return Result.Failure("INVALID_STATE",
                $"Cannot disconnect from state '{currentState}'.");

        TransitionState(ConnectionState.Disconnecting);

        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                using var timeoutCts = new CancellationTokenSource(_options.DisconnectTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    ct, timeoutCts.Token);

                try
                {
                    await _webSocket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnecting",
                        combinedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Close timed out — proceed with forced cleanup
                }
                catch (WebSocketException)
                {
                    // Connection already broken — proceed with cleanup
                }
            }
        }
        finally
        {
            _connectionCts?.Cancel();

            if (_receiveLoopTask is not null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on cancellation
                }
                catch (Exception)
                {
                    // Swallow — we are tearing down
                }
            }

            await CleanupResourcesAsync().ConfigureAwait(false);
            TransitionState(ConnectionState.Disconnected);
        }

        return Result.Success();
    }
// ─── Send ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result> SendAsync(MessageEnvelope message, CancellationToken ct = default)
    {
        if (message is null)
            return Result.Failure("INVALID_MESSAGE", "Message envelope cannot be null.");

        var ws = _webSocket;
        if (ws is null || ws.State != WebSocketState.Open)
            return Result.Failure("NOT_CONNECTED",
                $"Cannot send: WebSocket is in state '{ws?.State.ToString() ?? "null"}'.");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var json = ProtocolSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct).ConfigureAwait(false);

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("SEND_CANCELLED", "Send operation was cancelled.");
        }
        catch (WebSocketException ex)
        {
            TransitionState(ConnectionState.Faulted);
            return Result.Failure("SEND_FAILED", ex.Message);
        }
        finally
        {
            _sendLock.Release();
        }
    }
// ─── Receive ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<Result<MessageEnvelope>> ReceiveAsync(CancellationToken ct = default)
    {
        var channel = _receiveChannel;
        if (channel is null)
            return Result<MessageEnvelope>.Failure("NOT_CONNECTED",
                "Cannot receive: not connected.");

        try
        {
            var message = await channel.Reader.ReadAsync(ct).ConfigureAwait(false);
            return Result<MessageEnvelope>.Success(message);
        }
        catch (OperationCanceledException)
        {
            return Result<MessageEnvelope>.Failure("RECEIVE_CANCELLED",
                "Receive operation was cancelled.");
        }
        catch (ChannelClosedException)
        {
            return Result<MessageEnvelope>.Failure("CONNECTION_CLOSED",
                "The connection has been closed; no more messages will be received.");
        }
    }

    // ─── Receive Loop ───────────────────────────────────────────────────

    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[_options.ReceiveBufferSize];
        var channel = _receiveChannel!;

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                MessageEnvelope? envelope;

                try
                {
                    envelope = await ReadSingleMessageAsync(buffer, ct).ConfigureAwait(false);
                }
                catch (WebSocketException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (WebSocketException)
                {
                    TransitionState(ConnectionState.Faulted);
                    break;
                }

                if (envelope is null)
                {
                    // Null means either a close frame was received (state already
                    // transitioned) or malformed input was silently dropped.
                    // Only exit if the WebSocket is actually closing/closed.
                    if (_webSocket?.State != WebSocketState.Open)
                        break;
                    continue;
                }

                await channel.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            TransitionState(ConnectionState.Faulted);
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    }

    private async Task<MessageEnvelope?> ReadSingleMessageAsync(
        byte[] buffer, CancellationToken ct)
    {
        var ws = _webSocket!;
        using var messageStream = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                .ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await ws.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Acknowledging server close",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort
                }

                TransitionState(ConnectionState.Disconnected);
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                await ws.CloseOutputAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "Only text messages are supported",
                    CancellationToken.None).ConfigureAwait(false);
                TransitionState(ConnectionState.Faulted);
                return null;
            }

            messageStream.Write(buffer, 0, result.Count);

            if (messageStream.Length > _options.MaxMessageSize)
            {
                await ws.CloseOutputAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    $"Message exceeds maximum size of {_options.MaxMessageSize} bytes",
                    CancellationToken.None).ConfigureAwait(false);
                TransitionState(ConnectionState.Faulted);
                return null;
            }

        } while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(messageStream.ToArray());
        var deserializeResult = ProtocolSerializer.Deserialize(json);

        if (deserializeResult.IsSuccess)
            return deserializeResult.Value;

        // Malformed protocol input — silently dropped; must not crash the loop
        return null;
    }
// ─── State Management ───────────────────────────────────────────────

    private void TransitionState(ConnectionState newState)
    {
        ConnectionState oldState;
        EventHandler<ConnectionStateChangedEventArgs>? handler;

        lock (_stateLock)
        {
            if (_state == newState) return;
            oldState = _state;
            _state = newState;
            handler = StateChanged;
        }

        handler?.Invoke(this, new ConnectionStateChangedEventArgs(oldState, newState));
    }

    // ─── Cleanup ────────────────────────────────────────────────────────

    private async Task CleanupResourcesAsync()
    {
        _receiveChannel?.Writer.TryComplete();

        if (_webSocket is not null)
        {
            try { _webSocket.Dispose(); }
            catch { /* Best-effort */ }
            _webSocket = null;
        }

        if (_connectionCts is not null)
        {
            try { _connectionCts.Dispose(); }
            catch { /* Best-effort */ }
            _connectionCts = null;
        }

        _receiveChannel = null;
        _receiveLoopTask = null;

        await Task.CompletedTask;
    }

    // ─── IAsyncDisposable ───────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        await CleanupResourcesAsync().ConfigureAwait(false);

        try { _sendLock.Dispose(); }
        catch { /* Best-effort */ }

        GC.SuppressFinalize(this);
    }
}