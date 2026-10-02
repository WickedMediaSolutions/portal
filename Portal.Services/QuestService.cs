using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends quest intent from Portal to Keystone over the existing
/// <see cref="IWebSocketConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="EquipmentService"/> and
/// <see cref="CharacterPointsService"/>, this is a fire-and-forget sender: it
/// does not consume <c>ReceiveAsync</c> and never creates its own connection.
/// </para>
/// <para>
/// Every method here carries only a quest id. Keystone owns level
/// requirements, prerequisites, readiness, repeatability and rewards, and
/// answers with an authoritative <c>quest.result</c>. A successful send means
/// only that the request was transmitted — never that the action succeeded.
/// </para>
/// </remarks>
public sealed class QuestService
{
    private readonly IWebSocketConnection _connection;

    public QuestService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Asks the server to re-send the authoritative quest state.
    /// </summary>
    /// <remarks>
    /// The server already pushes <c>quest.snapshot</c> on every successful
    /// authentication, so this is only needed for an explicit manual re-sync.
    /// </remarks>
    public Task<Result> RequestSnapshotAsync(CancellationToken ct = default)
    {
        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.QuestSnapshotRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(new QuestSnapshotRequest()));

        return _connection.SendAsync(envelope, ct);
    }

    /// <summary>Asks the server to accept a quest.</summary>
    public Task<Result> SendAcceptAsync(string questId, CancellationToken ct = default)
        => SendActionAsync(ProtocolMessageTypes.QuestAcceptRequest, questId, ct);

    /// <summary>Asks the server to abandon an active quest.</summary>
    public Task<Result> SendAbandonAsync(string questId, CancellationToken ct = default)
        => SendActionAsync(ProtocolMessageTypes.QuestAbandonRequest, questId, ct);

    /// <summary>
    /// Asks the server to complete a quest.
    /// </summary>
    /// <remarks>
    /// The UI only offers this when the server reported the quest as ready, but
    /// the server re-validates readiness regardless, so a stale button press
    /// is refused rather than double-paying a reward.
    /// </remarks>
    public Task<Result> SendCompleteAsync(string questId, CancellationToken ct = default)
        => SendActionAsync(ProtocolMessageTypes.QuestCompleteRequest, questId, ct);

    private Task<Result> SendActionAsync(
        string messageType, string questId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(questId))
            return Task.FromResult(Result.Failure(
                "QUEST_NO_ID",
                "Cannot act on a quest: no quest was selected."));

        var request = new QuestAcceptRequest(questId);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            messageType,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return _connection.SendAsync(envelope, ct);
    }
}