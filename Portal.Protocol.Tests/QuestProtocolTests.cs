using System.Text.Json;
using Portal.Protocol;
using Portal.State;
using Xunit;

namespace Portal.Protocol.Tests;

/// <summary>
/// Protocol and state tests for the quest message family.
/// </summary>
/// <remarks>
/// These pin the client half of the contract:
/// <list type="bullet">
/// <item>Portal sends intent only — a quest id and a verb — never an expected
/// outcome, a readiness claim or a reward.</item>
/// <item>Every actionable button flag is copied from the server, so a quest the
/// server will refuse offers no enabled control.</item>
/// <item>Progress is applied idempotently, because the wire carries absolute
/// counts rather than deltas.</item>
/// <item>A reconnect snapshot fully replaces prior client state.</item>
/// </list>
/// </remarks>
public class QuestProtocolTests
{
    private static MessageEnvelope Event(string messageType, string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Event,
            MessageType = messageType,
            SequenceNumber = 1,
            Payload = doc.RootElement.Clone()
        };
    }

    private static MessageEnvelope Request(string messageType, object payload) =>
        new()
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Request,
            MessageType = messageType,
            CorrelationId = Guid.NewGuid().ToString("N"),
            Payload = ProtocolSerializer.SerializePayload(payload)
        };

    private static string[] FieldNames(MessageEnvelope envelope) =>
        envelope.Payload.EnumerateObject().Select(p => p.Name).ToArray();

    private const string ActiveQuestJson = """
    {
      "quests": [
        {
          "questId": "bone_collector",
          "name": "Bone Collector",
          "description": "Defeat 2 Skeleton Warriors and collect a health potion.",
          "state": "active",
          "levelRequired": 2,
          "prerequisites": [],
          "objectives": [
            { "index": 0, "type": "kill", "targetId": "skeleton_warrior",
              "targetName": "Skeleton Warrior", "current": 1, "required": 2,
              "met": false },
            { "index": 1, "type": "collect", "targetId": "health_potion",
              "targetName": "Health Potion", "current": 0, "required": 1,
              "met": false }
          ],
          "readyToComplete": false,
          "rewards": {
            "xp": 150, "currency": 0,
            "items": [
              { "itemId": "health_potion", "name": "Health Potion",
                "quantity": 1 },
              { "itemId": "leather_cap", "name": "Leather Cap", "quantity": 1 }
            ],
            "loot": null
          },
          "canAccept": false, "canAbandon": true, "canComplete": false,
          "acceptBlockedReason": "Quest 'bone_collector' is already active.",
          "abandonBlockedReason": null, "completeBlockedReason": null,
          "giverName": null, "turnInName": null
        }
      ]
    }
    """;

    private static QuestSnapshotPayload Snapshot(string json) =>
        ProtocolSerializer.DeserializePayload<QuestSnapshotPayload>(
            Event(ProtocolMessageTypes.QuestSnapshot, json)).Value;

    private static QuestProgressPayload Progress(string json) =>
        ProtocolSerializer.DeserializePayload<QuestProgressPayload>(
            Event(ProtocolMessageTypes.QuestProgress, json)).Value;

    // ─── Message types ────────────────────────────────────────────────

    [Fact]
    public void QuestMessageTypes_AreStable()
    {
        Assert.Equal("quest.snapshot.request", ProtocolMessageTypes.QuestSnapshotRequest);
        Assert.Equal("quest.snapshot", ProtocolMessageTypes.QuestSnapshot);
        Assert.Equal("quest.accept.request", ProtocolMessageTypes.QuestAcceptRequest);
        Assert.Equal("quest.abandon.request", ProtocolMessageTypes.QuestAbandonRequest);
        Assert.Equal("quest.complete.request", ProtocolMessageTypes.QuestCompleteRequest);
        Assert.Equal("quest.result", ProtocolMessageTypes.QuestResult);
        Assert.Equal("quest.progress", ProtocolMessageTypes.QuestProgress);
    }

    // ─── Requests carry intent only ───────────────────────────────────

    [Theory]
    [InlineData("quest.accept.request")]
    [InlineData("quest.abandon.request")]
    [InlineData("quest.complete.request")]
    public void QuestActionRequests_SendOnlyQuestId(string messageType)
    {
        var envelope = Request(messageType, new QuestAcceptRequest("bone_collector"));

        Assert.Equal(MessageCategory.Request, envelope.Category);
        Assert.Equal(messageType, envelope.MessageType);
        Assert.Equal(new[] { "questId" }, FieldNames(envelope));
        Assert.Equal("bone_collector",
            envelope.Payload.GetProperty("questId").GetString());
    }

    [Fact]
    public void QuestSnapshotRequest_SendsNoQuestState()
    {
        var envelope = Request(
            ProtocolMessageTypes.QuestSnapshotRequest, new QuestSnapshotRequest());

        Assert.Equal(MessageCategory.Request, envelope.Category);
        Assert.Empty(FieldNames(envelope));
    }

    // ─── Snapshot deserialization ─────────────────────────────────────

    [Fact]
    public void QuestSnapshot_Deserializes()
    {
        var result = ProtocolSerializer.DeserializePayload<QuestSnapshotPayload>(
            Event(ProtocolMessageTypes.QuestSnapshot, ActiveQuestJson));

        Assert.True(result.IsSuccess);
        var quest = Assert.Single(result.Value.Quests);
        Assert.Equal("bone_collector", quest.QuestId);
        Assert.Equal("Bone Collector", quest.Name);
        Assert.Equal("active", quest.State);
        Assert.Equal(2, quest.LevelRequired);
        Assert.True(quest.CanAbandon);
        Assert.False(quest.CanAccept);
        Assert.False(quest.ReadyToComplete);
        Assert.Equal(150, quest.Rewards.Xp);
        Assert.Equal(2, quest.Rewards.Items.Count);
    }

    [Fact]
    public void QuestObjectives_CarryDisplayNamesAndCounts()
    {
        var payload = Snapshot(ActiveQuestJson);

        var kill = payload.Quests[0].Objectives[0];
        Assert.Equal("kill", kill.Type);
        Assert.Equal("Skeleton Warrior", kill.TargetName);
        Assert.Equal(1, kill.Current);
        Assert.Equal(2, kill.Required);

        var collect = payload.Quests[0].Objectives[1];
        Assert.Equal("collect", collect.Type);
        Assert.Equal("Health Potion", collect.TargetName);
    }

    // ─── Grouping ─────────────────────────────────────────────────────

    [Fact]
    public void Snapshot_GroupsActiveAvailableAndCompleted()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""
        {
          "quests": [
            { "questId": "rat_slayer", "name": "Rat Slayer", "state": "available",
              "canAccept": true },
            { "questId": "bone_collector", "name": "Bone Collector",
              "state": "active", "canAbandon": true },
            { "questId": "potion_collector", "name": "Potion Collector",
              "state": "completed" }
          ]
        }
        """));

        Assert.Equal("rat_slayer", Assert.Single(state.AvailableQuests).QuestId);
        Assert.Equal("bone_collector", Assert.Single(state.ActiveQuests).QuestId);
        Assert.Equal("potion_collector", Assert.Single(state.CompletedQuests).QuestId);
        Assert.True(state.HasAnyQuest);
        Assert.True(state.HasActiveQuests);
    }

    [Fact]
    public void Snapshot_WithNoQuests_LeavesEmptyGroups()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""{"quests": []}"""));

        Assert.Empty(state.ActiveQuests);
        Assert.Empty(state.AvailableQuests);
        Assert.Empty(state.CompletedQuests);
        Assert.False(state.HasAnyQuest);
    }

    // ─── Button enablement is server-driven ──────────────────────────

    [Fact]
    public void Buttons_AreEnabledFromServerFlagsOnly()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        var quest = Assert.Single(state.ActiveQuests);
        Assert.False(quest.CanAccept);
        Assert.True(quest.CanAbandon);
        Assert.False(quest.CanComplete);
    }

    [Fact]
    public void AvailableQuest_EnablesAcceptOnly()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""
        { "quests": [ { "questId": "rat_slayer", "name": "Rat Slayer",
          "state": "available", "canAccept": true } ] }
        """));

        var quest = Assert.Single(state.AvailableQuests);
        Assert.True(quest.CanAccept);
        Assert.False(quest.CanAbandon);
        Assert.False(quest.CanComplete);
    }

    [Fact]
    public void CompletedQuest_OffersNoAcceptOrComplete()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "completed", "canAccept": true, "canComplete": true } ] }
        """));

        var quest = Assert.Single(state.CompletedQuests);
        Assert.True(quest.IsCompleted);
        Assert.False(quest.CanAccept);
        Assert.False(quest.CanComplete);
// A completed quest is never repeatable, even if a payload claims so.
    }

    // ─── Progress ─────────────────────────────────────────────────────

    [Fact]
    public void Progress_UpdatesTheMatchingQuestOnly()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        state.ApplyProgress(Progress("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "active",
          "objectives": [
            { "index": 0, "type": "kill", "targetId": "skeleton_warrior",
              "targetName": "Skeleton Warrior", "current": 2, "required": 2,
              "met": true },
            { "index": 1, "type": "collect", "targetId": "health_potion",
              "targetName": "Health Potion", "current": 1, "required": 1,
              "met": true }
          ],
          "readyToComplete": true, "canAbandon": true,
          "canComplete": true } ] }
        """));

        var quest = Assert.Single(state.ActiveQuests);
        Assert.Equal(2, quest.Objectives[0].Current);
        Assert.True(quest.ReadyToComplete);
        Assert.True(quest.CanComplete);
        Assert.Equal("2 / 2", quest.Objectives[0].ProgressText);
    }

    [Fact]
    public void Progress_ForAnUnknownQuest_IsIgnored()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        state.ApplyProgress(Progress("""
        { "quests": [ { "questId": "no_such_quest", "name": "Ghost",
          "state": "active", "objectives": [], "readyToComplete": true } ] }
        """));

        // An unrelated progress event must not disturb existing state.
        var quest = Assert.Single(state.ActiveQuests);
        Assert.Equal(1, quest.Objectives[0].Current);
    }

    [Fact]
    public void RepeatedProgressEvent_IsIdempotent()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        var progress = Progress("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "active",
          "objectives": [
            { "index": 0, "type": "kill", "targetId": "skeleton_warrior",
              "targetName": "Skeleton Warrior", "current": 2, "required": 2,
              "met": true }
          ],
          "readyToComplete": true, "canComplete": true } ] }
        """);

        state.ApplyProgress(progress);
        state.ApplyProgress(progress);
        state.ApplyProgress(progress);

        var quest = Assert.Single(state.ActiveQuests);
        Assert.Equal(2, quest.Objectives[0].Current);
        Assert.True(quest.ReadyToComplete);
    }

    [Fact]
    public void Progress_AppliesTheCountsTheServerReports()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        state.ApplyProgress(Progress("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "active",
          "objectives": [
            { "index": 0, "type": "kill", "targetId": "skeleton_warrior",
              "targetName": "Skeleton Warrior", "current": 0, "required": 2,
              "met": false }
          ],
          "readyToComplete": false } ] }
        """));

        var quest = Assert.Single(state.ActiveQuests);
        Assert.Equal(0, quest.Objectives[0].Current);
    }

    // ─── Results ──────────────────────────────────────────────────────

    private static QuestResultPayload Result(string json) =>
        ProtocolSerializer.DeserializePayload<QuestResultPayload>(
            Event(ProtocolMessageTypes.QuestResult, json)).Value;

    [Fact]
    public void SuccessfulResult_ExposesServerMessageAndState()
    {
        var state = new QuestState();
        state.ApplyResult(Result("""
        { "action": "accept", "questId": "rat_slayer", "success": true,
          "message": "Quest accepted: 'Rat Slayer'.",
          "state": "active", "rewards": null }
        """));

        Assert.Equal("Quest accepted: 'Rat Slayer'.", state.LastResultMessage);
        Assert.True(state.HasResultMessage);
    }

    [Fact]
    public void FailedResult_ShowsTheServersRefusal()
    {
        var state = new QuestState();
        state.ApplyResult(Result("""
        { "action": "complete", "questId": "bone_collector", "success": false,
          "message": "Objective 0: kill 'skeleton_warrior' - 0/2 completed.",
          "state": "active", "rewards": null }
        """));

        Assert.Contains("0/2 completed", state.LastResultMessage);
    }

    [Fact]
    public void CompletionResult_CarriesRewards()
    {
        var payload = Result("""
        { "action": "complete", "questId": "bone_collector", "success": true,
          "message": "Quest completed!", "state": "completed",
          "rewards": { "xp": 150, "currency": 0,
            "items": [ { "itemId": "leather_cap", "name": "Leather Cap",
                         "quantity": 1 } ], "loot": null } }
        """);

        Assert.True(payload.Success);
        Assert.Equal("completed", payload.State);
        Assert.Equal(150, payload.Rewards!.Xp);
    }

    [Fact]
    public void DuplicateCompletionResult_IsReportedAsFailure()
    {
        var payload = Result("""
        { "action": "complete", "questId": "bone_collector", "success": false,
          "message": "Quest 'bone_collector' has already been completed.",
          "state": "completed", "rewards": null }
        """);

        Assert.False(payload.Success);
        Assert.Contains("already been completed", payload.Message);
        Assert.Null(payload.Rewards);
    }

    // ─── Rewards display ──────────────────────────────────────────────

    [Fact]
    public void Rewards_SummariseXpCurrencyAndItems()
    {
        var rewards = QuestRewardsViewModel.FromRewards(new QuestRewards
        {
            Xp = 500,
            Currency = 25,
            Items = new[]
            {
                new QuestRewardItem
                {
                    ItemId = "rusty_sword", Name = "Rusty Sword", Quantity = 1
                }
            },
            Loot = new QuestRewardLoot
            {
                LootTableId = "boss_loot",
                Description = "Possible bonus item (rolled on completion)"
            }
        });

        Assert.Contains("500 XP", rewards.Summary);
        Assert.Contains("25c", rewards.Summary);
        Assert.Contains("1x Rusty Sword", rewards.Summary);
        Assert.Contains("rolled on completion", rewards.Summary);
        Assert.False(rewards.IsEmpty);
    }

    [Fact]
    public void Rewards_WithNothing_ReportNoRewards()
    {
        Assert.Equal("No rewards", QuestRewardsViewModel.Empty.Summary);
        Assert.True(QuestRewardsViewModel.Empty.IsEmpty);
    }

    // ─── Reconnect replaces state ─────────────────────────────────────

    [Fact]
    public void ReconnectSnapshot_ReplacesAllPreviousState()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));
        Assert.Single(state.ActiveQuests);

        state.ApplySnapshot(Snapshot("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "completed" } ] }
        """));

        Assert.Empty(state.ActiveQuests);
        Assert.Single(state.CompletedQuests);
    }

    [Fact]
    public void ReconnectSnapshot_RestoresProgressAndReadyState()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""
        { "quests": [ { "questId": "bone_collector", "name": "Bone Collector",
          "state": "active", "canAbandon": true, "canComplete": true,
          "readyToComplete": true,
          "objectives": [
            { "index": 0, "type": "kill", "targetId": "skeleton_warrior",
              "targetName": "Skeleton Warrior", "current": 2, "required": 2,
              "met": true }
          ] } ] }
        """));

        var quest = Assert.Single(state.ActiveQuests);
        Assert.True(quest.ReadyToComplete);
        Assert.True(quest.CanComplete);
        Assert.Equal("2 / 2", quest.Objectives[0].ProgressText);
        Assert.Equal("Ready to Complete", quest.StatusText);
    }

    [Fact]
    public void QuestId_LooksUpAcrossEveryGroup()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot("""
        { "quests": [
          { "questId": "a", "name": "A", "state": "available" },
          { "questId": "b", "name": "B", "state": "active" },
          { "questId": "c", "name": "C", "state": "completed" } ] }
        """));

        Assert.Equal("a", state.FindById("a")!.QuestId);
        Assert.Equal("b", state.FindById("b")!.QuestId);
        Assert.Equal("c", state.FindById("c")!.QuestId);
        Assert.Null(state.FindById("missing"));
    }

    [Fact]
    public void StatusText_IsReadableText_NotColourAlone()
    {
        var state = new QuestState();
        state.ApplySnapshot(Snapshot(ActiveQuestJson));

        var quest = Assert.Single(state.ActiveQuests);
        Assert.Equal("In progress", quest.StatusText);
        Assert.Contains("1 / 2", quest.ObjectiveSummary);
        Assert.Contains("Skeleton Warrior", quest.ObjectiveSummary);
        Assert.Contains("150 XP", quest.RewardsSummary);
    }
}