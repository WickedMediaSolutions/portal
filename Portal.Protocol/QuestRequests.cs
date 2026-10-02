namespace Portal.Protocol;

/// <summary>
/// Protocol V1 request that asks the server to re-send the authoritative quest
/// state, sent via <c>quest.snapshot.request</c>.
/// </summary>
/// <remarks>
/// The server already pushes <c>quest.snapshot</c> on every successful
/// authentication, so a normal client never needs this. It exists for an
/// explicit on-demand re-sync. The payload carries no quest state at all:
/// Keystone rebuilds the whole picture from its own quest service.
/// </remarks>
public sealed class QuestSnapshotRequest
{
    public QuestSnapshotRequest()
    {
    }
}

/// <summary>
/// Base shape shared by the accept, abandon and complete requests.
/// Every one of them carries only a quest id and nothing else.
/// </summary>
/// <remarks>
/// This is deliberately minimal. Portal sends intent; Keystone owns level
/// requirements, prerequisites, readiness, rewards and repeatability. The reply
/// always arrives as an authoritative <c>quest.result</c> event, so a client
/// must never treat the act of sending as success.
/// </remarks>
public abstract class QuestActionRequest
{
    /// <summary>The quest to act on.</summary>
    public string QuestId { get; init; } = string.Empty;

    protected QuestActionRequest()
    {
    }

    protected QuestActionRequest(string questId)
    {
        QuestId = questId ?? throw new ArgumentNullException(nameof(questId));
    }
}

/// <summary>
/// Protocol V1 request to accept a quest, sent via <c>quest.accept.request</c>.
/// </summary>
public sealed class QuestAcceptRequest : QuestActionRequest
{
    public QuestAcceptRequest()
    {
    }

    public QuestAcceptRequest(string questId) : base(questId)
    {
    }
}

/// <summary>
/// Protocol V1 request to abandon an active quest, sent via
/// <c>quest.abandon.request</c>.
/// </summary>
/// <remarks>
/// Abandoning discards recorded progress. A quest that was already completed
/// can never be abandoned, and its progress cannot be re-earned.
/// </remarks>
public sealed class QuestAbandonRequest : QuestActionRequest
{
    public QuestAbandonRequest()
    {
    }

    public QuestAbandonRequest(string questId) : base(questId)
    {
    }
}

/// <summary>
/// Protocol V1 request to complete a ready quest, sent via
/// <c>quest.complete.request</c>.
/// </summary>
/// <remarks>
/// Completion is explicit and never automatic: meeting every objective only
/// makes the quest ready, it never completes it. A request sent before the
/// quest is ready is rejected by the server with a refusal message, and a
/// second completion of an already-completed quest is rejected too, so
/// rewards are granted exactly once.
/// </remarks>
public sealed class QuestCompleteRequest : QuestActionRequest
{
    public QuestCompleteRequest()
    {
    }

    public QuestCompleteRequest(string questId) : base(questId)
    {
    }
}