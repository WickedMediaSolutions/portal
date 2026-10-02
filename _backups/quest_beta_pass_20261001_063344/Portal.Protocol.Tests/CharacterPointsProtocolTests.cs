using System.Text.Json;
using Portal.Protocol;
using Xunit;

namespace Portal.Protocol.Tests;

/// <summary>
/// Protocol-level tests for the Character Point / stat-allocation messages.
///
/// Verifies the wire shapes Keystone produces and Portal consumes, and that
/// Portal never sends a client-side point balance (Keystone stays the sole
/// authority for the balance and every validation rule).
/// </summary>
public class CharacterPointsProtocolTests
{
    private static MessageEnvelope EventEnvelope(string messageType, string payloadJson)
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

    [Fact]
    public void MessageTypes_AreStable()
    {
        Assert.Equal("character.points.snapshot",
            ProtocolMessageTypes.CharacterPointsSnapshot);
        Assert.Equal("character.points.allocate.request",
            ProtocolMessageTypes.CharacterPointsAllocateRequest);
    }

    [Fact]
    public void Deserialize_SnapshotPayload_ReadsBalanceAndStats()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsSnapshot,
            """
            {
              "availablePoints": 16,
              "pointsPerLevel": 8,
              "stats": [
                { "statId": "str", "name": "Strength", "value": 25, "cap": 27 },
                { "statId": "con", "name": "Constitution", "value": 26, "cap": 27 }
              ]
            }
            """);

        var result = ProtocolSerializer.DeserializePayload<CharacterPointsSnapshotPayload>(envelope);
        Assert.True(result.IsSuccess);

        var payload = result.Value;
        Assert.Equal(16, payload.AvailablePoints);
        Assert.Equal(8, payload.PointsPerLevel);
        Assert.Equal(2, payload.Stats.Count);
        Assert.Equal("str", payload.Stats[0].StatId);
        Assert.Equal("Strength", payload.Stats[0].Name);
        Assert.Equal(25, payload.Stats[0].Value);
        Assert.Equal(27, payload.Stats[0].Cap);
        Assert.False(payload.Stats[0].IsCapped);
    }

    [Fact]
    public void SnapshotPayload_CappedStat_IsFlagged()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsSnapshot,
            """
            {
              "availablePoints": 0,
              "pointsPerLevel": 8,
              "stats": [ { "statId": "str", "name": "Strength", "value": 27, "cap": 27 } ]
            }
            """);

        var payload = ProtocolSerializer
            .DeserializePayload<CharacterPointsSnapshotPayload>(envelope).Value;

        Assert.True(payload.Stats[0].IsCapped);
    }

    [Fact]
    public void SnapshotPayload_NoErrorField_MeansNoError()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsSnapshot,
            """{ "availablePoints": 4, "pointsPerLevel": 8, "stats": [] }""");

        var payload = ProtocolSerializer
            .DeserializePayload<CharacterPointsSnapshotPayload>(envelope).Value;

        Assert.False(payload.HasError);
        Assert.Null(payload.Error);
    }

    [Fact]
    public void SnapshotPayload_CarriesKeystoneRejectionMessage()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsSnapshot,
            """
            {
              "availablePoints": 2,
              "pointsPerLevel": 8,
              "stats": [],
              "error": "That allocation costs 8 Character Points but you only have 2 unspent."
            }
            """);

        var payload = ProtocolSerializer
            .DeserializePayload<CharacterPointsSnapshotPayload>(envelope).Value;

        Assert.True(payload.HasError);
        Assert.Contains("only have 2 unspent", payload.Error);
        // Authoritative state still accompanies the rejection.
        Assert.Equal(2, payload.AvailablePoints);
    }

    [Fact]
    public void SnapshotPayload_EmptyStatsList_IsAccepted()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsSnapshot,
            """{ "availablePoints": 0, "pointsPerLevel": 8, "stats": [] }""");

        var payload = ProtocolSerializer
            .DeserializePayload<CharacterPointsSnapshotPayload>(envelope).Value;

        Assert.NotNull(payload.Stats);
        Assert.Empty(payload.Stats);
    }

    [Fact]
    public void SerializeAllocation_MatchesKeystoneExpectedShape()
    {
        var request = new CharacterPointsAllocateRequest(new[]
        {
            new StatAllocationEntry("str", 2),
            new StatAllocationEntry("dex", 3),
            new StatAllocationEntry("con", 3)
        });

        var json = ProtocolSerializer.SerializePayload(request).GetRawText();

        Assert.Contains("\"allocations\"", json);
        Assert.Contains("\"statId\":\"str\"", json);
        Assert.Contains("\"amount\":2", json);
        Assert.Contains("\"statId\":\"dex\"", json);
        Assert.Contains("\"amount\":3", json);
    }

    [Fact]
    public void SerializeAllocation_CarriesNoBalanceField()
    {
        // Keystone must never receive a client-asserted point balance.
        var request = new CharacterPointsAllocateRequest(new[]
        {
            new StatAllocationEntry("str", 2)
        });

        var json = ProtocolSerializer.SerializePayload(request).GetRawText();

        Assert.DoesNotContain("availablePoints", json);
        Assert.DoesNotContain("pointsAvailable", json);
    }

    [Fact]
    public void RoundTrip_AllocationRequest_PreservesEntries()
    {
        var request = new CharacterPointsAllocateRequest(new[]
        {
            new StatAllocationEntry("str", 2),
            new StatAllocationEntry("con", 3)
        });

        var payload = ProtocolSerializer.SerializePayload(request);
        using var doc = JsonDocument.Parse(payload.GetRawText());
        var envelope = new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = MessageCategory.Request,
            MessageType = ProtocolMessageTypes.CharacterPointsAllocateRequest,
            CorrelationId = "corr-cp-1",
            Payload = doc.RootElement.Clone()
        };

        var result = ProtocolSerializer
            .DeserializePayload<CharacterPointsAllocateRequest>(envelope);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Allocations.Count);
        Assert.Equal("str", result.Value.Allocations[0].StatId);
        Assert.Equal(2, result.Value.Allocations[0].Amount);
    }

    [Fact]
    public void EnvelopeWithAllocationRequest_IsValidProtocolMessage()
    {
        var request = new CharacterPointsAllocateRequest(new[]
        {
            new StatAllocationEntry("wis", 1)
        });

        var envelope = new MessageEnvelope(
            ProtocolVersion.V1,
            MessageCategory.Request,
            ProtocolMessageTypes.CharacterPointsAllocateRequest,
            "corr-cp-2",
            null,
            ProtocolSerializer.SerializePayload(request));

        var result = ProtocolSerializer.Deserialize(ProtocolSerializer.Serialize(envelope));

        Assert.True(result.IsSuccess);
        Assert.Equal(ProtocolMessageTypes.CharacterPointsAllocateRequest,
            result.Value.MessageType);
        Assert.Equal(MessageCategory.Request, result.Value.Category);
    }

    [Fact]
    public void EmptyAllocation_DeserializesToEmptyList()
    {
        var envelope = EventEnvelope(
            ProtocolMessageTypes.CharacterPointsAllocateRequest,
            """{ "allocations": [] }""");

        var payload = ProtocolSerializer
            .DeserializePayload<CharacterPointsAllocateRequest>(envelope).Value;

        Assert.NotNull(payload.Allocations);
        Assert.Empty(payload.Allocations);
    }

    [Fact]
    public void Records_RejectNullStatId()
    {
        Assert.Throws<ArgumentNullException>(() => new StatAllocationEntry(null!, 1));
        Assert.Throws<ArgumentNullException>(() => new AllocatableStatRecord(null!, "X", 1, 2));
    }
}
