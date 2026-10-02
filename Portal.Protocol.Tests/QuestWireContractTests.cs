using System.Text.Json;
using Portal.Protocol;
using Portal.State;
using Xunit;

namespace Portal.Protocol.Tests;

/// <summary>
/// Closes the loop on the quest wire contract: REAL envelopes captured from
/// the running Keystone bridge must deserialize through Portal's actual
/// serializer and drive the quest UI state correctly.
/// </summary>
/// <remarks>
/// <c>quest_envelopes.json</c> is written by the Keystone end-to-end QA harness
/// (<c>_quest_audit/quest_e2e.py</c>), which drives the real
/// <c>PortalBridgeProtocol.onMessage</c> handlers. Whatever the server really
/// put on the wire must therefore be understood by the client.
/// </remarks>
public class QuestWireContractTests
{
    private static string? FindFixture()
    {
        var direct = Path.Combine(AppContext.BaseDirectory,
                                 "quest_envelopes.json");
        if (File.Exists(direct))
            return direct;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "_quest_audit",
                                         "quest_envelopes.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static JsonElement[] CapturedEnvelopes()
    {
        var path = FindFixture();
        if (path is null)
            return Array.Empty<JsonElement>();

        // Each element is CLONED out of its document: returning elements that
        // still reference a disposed JsonDocument would throw later.
        var results = new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var element in doc.RootElement.EnumerateArray())
            results.Add(element.Clone());
        return results.ToArray();
    }

    private static JsonElement[] OfType(string messageType) =>
        CapturedEnvelopes()
            .Where(e => e.GetProperty("messageType").GetString() == messageType)
            .ToArray();

    private static MessageEnvelope Envelope(JsonElement element)
    {
        var result = ProtocolSerializer.Deserialize(element.GetRawText());
        Assert.True(result.IsSuccess,
            $"Keystone produced an envelope Portal rejects: {element}");
        return result.Value;
    }

    /// <summary>
    /// Fails loudly when the captured fixture is missing, rather than
    /// silently passing: an empty capture would make every contract assertion
    /// below vacuous, which is exactly the regression these tests exist to
    /// catch.
    /// </summary>
    private static void RequireFixture(int count) =>
        Assert.True(count > 0,
            "quest_envelopes.json is missing or empty. Run the Keystone quest " +
            "E2E harness (_quest_audit/quest_e2e.py) before the Portal suite.");

    [Fact]
    public void RealEnvelopes_AllValidate()
    {
        var envelopes = CapturedEnvelopes();
        RequireFixture(envelopes.Length);

        foreach (var element in envelopes)
            Assert.True(ProtocolSerializer.Deserialize(
                element.GetRawText()).IsSuccess);
    }

    [Fact]
    public void RealQuestSnapshot_DeserializesAndGroups()
    {
        var envelopes = OfType(ProtocolMessageTypes.QuestSnapshot);
        RequireFixture(envelopes.Length);

        var state = new QuestState();
        var sawCompletedBoneCollector = false;

        foreach (var element in envelopes)
        {
            var payload = ProtocolSerializer.DeserializePayload<QuestSnapshotPayload>(
                Envelope(element));
            Assert.True(payload.IsSuccess);
            state.ApplySnapshot(payload.Value);

            foreach (var quest in state.ActiveQuests)
            {
                Assert.Equal(QuestStates.Active, quest.State);
                Assert.True(quest.CanAbandon);
                Assert.False(quest.CanAccept);
            }

            foreach (var quest in state.CompletedQuests)
            {
                Assert.Equal(QuestStates.Completed, quest.State);
                Assert.False(quest.CanAccept);
                Assert.False(quest.CanComplete);
            }

            if (state.FindById("bone_collector") is { } bc && bc.IsCompleted)
                sawCompletedBoneCollector = true;
        }

        Assert.True(sawCompletedBoneCollector,
            "the captured flow must end with bone_collector completed");
    }

    [Fact]
    public void RealQuestResults_AreAlwaysAuthoritative()
    {
        var envelopes = OfType(ProtocolMessageTypes.QuestResult);
        RequireFixture(envelopes.Length);

        var actions = new List<string>();
        foreach (var element in envelopes)
        {
            var payload = ProtocolSerializer.DeserializePayload<QuestResultPayload>(
                Envelope(element));
            Assert.True(payload.IsSuccess);
            var result = payload.Value;

            // Every result carries a human-readable authoritative message.
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
            Assert.Contains(result.Action,
                new[] { "accept", "abandon", "complete" });

            actions.Add($"{result.Action}:{(result.Success ? "ok" : "no")}");

            // Rewards only ever accompany a successful completion.
            if (result.Rewards is not null)
            {
                Assert.True(result.Success);
                Assert.Equal("complete", result.Action);
            }
        }

        Assert.Contains("accept:ok", actions);
        Assert.Contains("complete:ok", actions);
        // The server must refuse at least one action: a premature completion,
        // a duplicate completion, or an illegal re-accept.
        Assert.Contains(actions, a => a.EndsWith(":no"));
    }

    [Fact]
    public void RealQuestProgress_UsesAbsoluteCounts()
    {
        var envelopes = OfType(ProtocolMessageTypes.QuestProgress);

        var state = new QuestState();
        foreach (var element in envelopes)
        {
            var payload = ProtocolSerializer.DeserializePayload<QuestProgressPayload>(
                Envelope(element));
            Assert.True(payload.IsSuccess);

            foreach (var record in payload.Value.Quests)
            {
                Assert.False(string.IsNullOrWhiteSpace(record.QuestId));
                foreach (var objective in record.Objectives)
                {
                    Assert.False(string.IsNullOrWhiteSpace(objective.TargetName));
                    Assert.True(objective.Required > 0);
                    Assert.InRange(objective.Current, 0, objective.Required);
                }
            }
        }
    }

    [Fact]
    public void RealSnapshotObjectives_CarryDisplayNamesNotRawIds()
    {
        var envelopes = OfType(ProtocolMessageTypes.QuestSnapshot);
        RequireFixture(envelopes.Length);

        var checkedAny = false;
        foreach (var element in envelopes)
        {
            var payload = ProtocolSerializer.DeserializePayload<QuestSnapshotPayload>(
                Envelope(element));
            foreach (var quest in payload.Value.Quests)
            {
                foreach (var objective in quest.Objectives)
                {
                    Assert.False(string.IsNullOrWhiteSpace(objective.TargetName));
                    // The raw id is retained for identity, but the display
                    // name must be what a player actually reads.
                    Assert.NotEqual(objective.TargetId, objective.TargetName);
                    checkedAny = true;
                }
            }
        }

        Assert.True(checkedAny, "the captured snapshots contained no objectives");
    }

    [Fact]
    public void RealRewards_MatchTheAuthoritativeXpAward()
    {
        var envelopes = OfType(ProtocolMessageTypes.QuestResult);
        var sawXp = false;

        foreach (var element in envelopes)
        {
            var payload = ProtocolSerializer.DeserializePayload<QuestResultPayload>(
                Envelope(element));
            if (payload.Value.Rewards is not { Xp: > 0 } rewards)
                continue;

            sawXp = true;
            // bone_collector declares 150 XP; the server must report the
            // same figure the definition promises, not a client guess.
            Assert.Equal(150, rewards.Xp);
            Assert.NotEmpty(rewards.Items);
            foreach (var item in rewards.Items)
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
        }

        Assert.True(sawXp, "the captured flow must complete a quest that pays XP");
    }
}

