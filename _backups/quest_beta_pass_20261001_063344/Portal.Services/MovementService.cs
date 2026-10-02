using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends authoritative directional movement requests from Portal to Keystone
/// over the existing <see cref="IWebSocketConnection"/>.
///
/// This service is a fire-and-forget sender. It does NOT consume
/// <c>ReceiveAsync</c> and never creates its own connection.  All resulting
/// state (room.entity.snapshot, target.changed) returns through the existing
/// <see cref="GameEventService"/> event stream.
/// </summary>
public sealed class MovementService
{
    private readonly IWebSocketConnection _connection;

    public MovementService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Sends a movement.direction.request to Keystone for the specified
    /// canonical direction.
    ///
    /// Does NOT read a response.  Successful movement is reflected back
    /// through the existing event stream (room.entity.snapshot broadcasts and
    /// target.changed clears) driven by Keystone's authoritative movement
    /// hooks.
    /// </summary>
    /// <param name="direction">The canonical direction (e.g. "north"). Must be
    /// non-empty.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendDirectionAsync(string direction, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(direction))
            return Result.Failure(
                "MOVEMENT_NO_DIRECTION",
                "Cannot move: no direction provided.");

        var request = new MovementDirectionRequest(direction);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.MovementDirectionRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }
}
