namespace Portal.Protocol;

/// <summary>
/// Protocol V1 quest snapshot payload.
/// Pushed by the server via the <c>quest.snapshot</c> event on every successful
/// authentication.
/// </summary>
/// <remarks>
/// Everything here is server-authored. Portal renders it and never recomputes
/// availability, readiness or rewards. Quests the server considers unavailable
/// (wrong level, unmet prerequisite, or content not completable in beta) are
/// simply absent, so the UI can never offer a button the server would refuse.
/// </remarks>
public sealed class QuestSnapshotPayload
{
    /// <summary>Every quest visible to this character, in server order.</summary>
    public IReadOnlyList<QuestRecord> Quests { get; init; } =
        Array.Empty<QuestRecord>();

    public QuestSnapshotPayload()
    {
    }
}

/// <summary>One quest as the server describes it for this character.</summary>
public sealed class QuestRecord
{
    /// <summary>Stable quest identifier; used to key the UI.</summary>
    public string QuestId { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Objective/flavour description text.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Server state: available, active, completed or locked.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Minimum character level to accept.</summary>
    public int LevelRequired { get; init; }

    /// <summary>Quests that must be completed first.</summary>
    public IReadOnlyList<QuestPrerequisite> Prerequisites { get; init; } =
        Array.Empty<QuestPrerequisite>();

    /// <summary>Objectives with their authoritative current counts.</summary>
    public IReadOnlyList<QuestObjective> Objectives { get; init; } =
        Array.Empty<QuestObjective>();

    /// <summary>Whether every objective is satisfied right now.</summary>
    public bool ReadyToComplete { get; init; }

    /// <summary>Rewards the server grants on completion.</summary>
    public QuestRewards Rewards { get; init; } = new();

    /// <summary>Whether the server would accept this quest right now.</summary>
    public bool CanAccept { get; init; }

    /// <summary>Whether the server would abandon this quest right now.</summary>
    public bool CanAbandon { get; init; }

    /// <summary>Whether the server would complete this quest right now.</summary>
    public bool CanComplete { get; init; }

    /// <summary>Why acceptance is currently refused, when it is.</summary>
    public string? AcceptBlockedReason { get; init; }

    /// <summary>Why abandonment is currently refused, when it is.</summary>
    public string? AbandonBlockedReason { get; init; }

    /// <summary>Why completion is currently refused, when it is.</summary>
    public string? CompleteBlockedReason { get; init; }

    /// <summary>
    /// Giver name, when the server legitimately knows one. Keystone's quest
    /// model has no NPC dialogue, so this is normally null and acceptance is a
    /// global character action rather than a proximity one.
    /// </summary>
    public string? GiverName { get; init; }

    /// <summary>Turn-in name, when the server legitimately knows one.</summary>
    public string? TurnInName { get; init; }

    public QuestRecord()
    {
    }
}

/// <summary>A prerequisite quest, named so the UI can explain the lock.</summary>
public sealed class QuestPrerequisite
{
    public string QuestId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public QuestPrerequisite()
    {
    }
}

/// <summary>
/// One quest objective with its authoritative progress.
/// </summary>
/// <remarks>
/// The server supplies a canonical display name, so Portal renders text and
/// never has to interpret a raw mob or item id. The raw id is still carried
/// for stable identity.
/// </remarks>
public sealed class QuestObjective
{
    /// <summary>Objective slot index within the quest.</summary>
    public int Index { get; init; }

    /// <summary>"kill" or "collect".</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Stable target id (mob_id or item_id).</summary>
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Canonical display name for the target.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Progress so far, as the server counts it.</summary>
    public int Current { get; init; }

    /// <summary>Number required to satisfy this objective.</summary>
    public int Required { get; init; }

    /// <summary>Whether this objective is satisfied.</summary>
    public bool Met { get; init; }

    public QuestObjective()
    {
    }
}

/// <summary>What a completed quest grants.</summary>
public sealed class QuestRewards
{
    /// <summary>Experience awarded on completion.</summary>
    public int Xp { get; init; }

    /// <summary>Copper awarded on completion.</summary>
    public int Currency { get; init; }

    /// <summary>Items awarded on completion.</summary>
    public IReadOnlyList<QuestRewardItem> Items { get; init; } =
        Array.Empty<QuestRewardItem>();

    /// <summary>
    /// Optional bonus loot table, described as a possibility only: the roll
    /// happens server-side at completion, so no specific outcome is promised.
    /// </summary>
    public QuestRewardLoot? Loot { get; init; }

    public QuestRewards()
    {
    }
}

/// <summary>One item reward, with the display name the server resolved.</summary>
public sealed class QuestRewardItem
{
    public string ItemId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public QuestRewardItem()
    {
    }
}

/// <summary>An optional bonus drop rolled at completion time.</summary>
public sealed class QuestRewardLoot
{
    public string LootTableId { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public QuestRewardLoot()
    {
    }
}

/// <summary>
/// Protocol V1 quest result payload.
/// Pushed by the server via the <c>quest.result</c> event after every accept,
/// abandon or complete attempt.
/// </summary>
/// <remarks>
/// This is the authoritative outcome. A client must render <see cref="Success"/>
/// and <see cref="Message"/> verbatim and must never treat a button press as
/// success.
/// </remarks>
public sealed class QuestResultPayload
{
    /// <summary>Which action this reports: accept, abandon or complete.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>The quest the action targeted.</summary>
    public string? QuestId { get; init; }

    /// <summary>Whether the server carried the action out.</summary>
    public bool Success { get; init; }

    /// <summary>Authoritative outcome text, verbatim from the server.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The quest state after the action, or null when it has none.</summary>
    public string? State { get; init; }

    /// <summary>Rewards granted, present on a successful completion.</summary>
    public QuestRewards? Rewards { get; init; }

    public QuestResultPayload()
    {
    }
}

/// <summary>
/// Protocol V1 quest progress payload.
/// Pushed by the server via the <c>quest.progress</c> event when runtime quest
/// progress actually changed because of a mob kill or an item acquisition.
/// </summary>
/// <remarks>
/// Only the quests that actually advanced are described. An unrelated kill or
/// a pickup that matched no objective produces no event at all. Repeating the
/// same progress is harmless: the payload carries absolute counts, not deltas.
/// </remarks>
public sealed class QuestProgressPayload
{
    /// <summary>Only the quests whose progress changed.</summary>
    public IReadOnlyList<QuestProgressRecord> Quests { get; init; } =
        Array.Empty<QuestProgressRecord>();

    public QuestProgressPayload()
    {
    }
}

/// <summary>Incremental progress for one advanced quest.</summary>
public sealed class QuestProgressRecord
{
    public string QuestId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Server state: available, active, completed or locked.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Absolute objective counts, matching the snapshot shape.</summary>
    public IReadOnlyList<QuestObjective> Objectives { get; init; } =
        Array.Empty<QuestObjective>();

    /// <summary>Whether every objective is now satisfied.</summary>
    public bool ReadyToComplete { get; init; }

    public bool CanAbandon { get; init; }

    public bool CanComplete { get; init; }

    public QuestProgressRecord()
    {
    }
}
