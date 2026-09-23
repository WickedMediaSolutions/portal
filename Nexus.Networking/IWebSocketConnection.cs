using Nexus.Core.Results;
using Nexus.Protocol;

namespace Nexus.Networking;

/// <summary>
/// Abstraction over a WebSocket connection that carries Nexus Protocol V1 messages.
///
/// Provides methods for connecting, disconnecting, sending, and receiving
/// protocol envelopes. All public operations return <see cref="Result"/> or
/// <see cref="Result{T}"/> rather than throwing on expected failure paths.
///
/// Implementations must be safe for concurrent use (send and receive may
/// operate concurrently from different callers).
/// </summary>
public interface IWebSocketConnection : IAsyncDisposable
{
    /// <summary>The current connection state.</summary>
    ConnectionState State { get; }

    /// <summary>
    /// The URI of the remote endpoint, or <c>null</c> if not connected.
    /// </summary>
    Uri? RemoteUri { get; }

    /// <summary>
    /// Raised whenever the connection transitions to a new state.
    /// Handlers are invoked synchronously on the thread that performed
    /// the transition; avoid blocking in event handlers.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Initiates a WebSocket connection to the specified URI.
    /// Returns <see cref="Result.Success"/> once the handshake completes
    /// and the receive loop has started.
    /// </summary>
    /// <param name="uri">The WebSocket endpoint URI (ws:// or wss://).</param>
    /// <param name="ct">Cancellation token for the connection attempt.</param>
    Task<Result> ConnectAsync(Uri uri, CancellationToken ct = default);

    /// <summary>
    /// Gracefully closes the WebSocket connection.
    /// Sends a close frame, cancels the receive loop, and waits for
    /// cleanup to complete.
    /// </summary>
    /// <param name="ct">Cancellation token for the disconnect sequence.</param>
    Task<Result> DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Serializes and sends a <see cref="MessageEnvelope"/> over the WebSocket.
    /// Sends are serialized internally; concurrent callers are safe.
    /// </summary>
    /// <param name="message">The protocol envelope to send.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    Task<Result> SendAsync(MessageEnvelope message, CancellationToken ct = default);

    /// <summary>
    /// Waits for the next available <see cref="MessageEnvelope"/> received
    /// from the remote endpoint. Messages are validated by the protocol
    /// deserializer before being returned.
    /// </summary>
    /// <param name="ct">Cancellation token for the receive wait.</param>
    Task<Result<MessageEnvelope>> ReceiveAsync(CancellationToken ct = default);
}