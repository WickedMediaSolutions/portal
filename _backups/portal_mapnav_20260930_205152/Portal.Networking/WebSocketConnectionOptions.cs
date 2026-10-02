namespace Portal.Networking;

/// <summary>
/// Configuration options for <see cref="WebSocketConnection"/>.
/// All values have sensible defaults suitable for most scenarios.
/// </summary>
public sealed class WebSocketConnectionOptions
{
    /// <summary>
    /// Interval between WebSocket keep-alive pings.
    /// Maps to <see cref="System.Net.WebSockets.ClientWebSocketOptions.KeepAliveInterval"/>.
    /// Default: 15 seconds.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum time to wait for the WebSocket handshake to complete.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum time to wait for a graceful close handshake.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan DisconnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Size of the buffer used for reading WebSocket frames.
    /// Default: 4096 bytes.
    /// </summary>
    public int ReceiveBufferSize { get; init; } = 4096;

    /// <summary>
    /// Maximum total size of a single complete WebSocket message.
    /// Messages exceeding this limit cause the connection to close with
    /// <see cref="System.Net.WebSockets.WebSocketCloseStatus.MessageTooBig"/>.
    /// Default: 1 MB.
    /// </summary>
    public int MaxMessageSize { get; init; } = 1024 * 1024;

    /// <summary>
    /// Capacity of the bounded channel that buffers received messages
    /// between the receive loop and consumers.
    /// Default: 256.
    /// </summary>
    public int ReceiveChannelCapacity { get; init; } = 256;
}