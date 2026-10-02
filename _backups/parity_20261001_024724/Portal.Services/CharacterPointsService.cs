using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends authoritative Character Point stat-allocation requests from Portal
/// to Keystone over the existing <see cref="IWebSocketConnection"/>.
///
/// Like <see cref="EquipmentService"/>, this is a fire-and-forget sender. It
/// does NOT consume <c>ReceiveAsync</c> and never creates its own connection.
/// The resulting authoritative state — including a rejection message —
/// returns through the existing <see cref="GameEventService"/> event stream
/// as a <c>character.points.snapshot</c>.
/// </summary>
public sealed class CharacterPointsService
{
    private readonly IWebSocketConnection _connection;

    public CharacterPointsService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Sends a complete, atomic stat-allocation proposal to Keystone.
    ///
    /// The whole set is sent as ONE request: Keystone validates it as a unit
    /// and either commits every stat or none of them. Portal deliberately
    /// performs no balance or cap arithmetic of its own — it proposes, and
    /// the server decides. The request carries no balance field, so a client
    /// can never claim points it does not have.
    /// </summary>
    /// <param name="allocations">
    /// The proposed allocation. Entries with a non-positive amount are
    /// rejected locally purely as a usability guard; every other rule
    /// (sufficiency, caps, duplicates, stat validity) is enforced by Keystone.
    /// </param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendAllocationAsync(
        IReadOnlyList<StatAllocationEntry> allocations,
        CancellationToken ct = default)
    {
        if (allocations is null || allocations.Count == 0)
            return Result.Failure(
                "ALLOCATE_NO_ALLOCATION",
                "Nothing to allocate: no stat was selected.");

        foreach (var entry in allocations)
        {
            if (string.IsNullOrWhiteSpace(entry.StatId))
                return Result.Failure(
                    "ALLOCATE_NO_STAT",
                    "Cannot allocate: a stat id was empty.");

            if (entry.Amount <= 0)
                return Result.Failure(
                    "ALLOCATE_BAD_AMOUNT",
                    $"Cannot allocate {entry.Amount} to {entry.StatId}: " +
                    "the amount must be positive.");
        }

        var request = new CharacterPointsAllocateRequest(allocations);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.CharacterPointsAllocateRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }
}