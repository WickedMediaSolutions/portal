using System.Text.Json;
using Nexus.Core.Results;
using Nexus.Protocol;
using Xunit;

namespace Nexus.Protocol.Tests;

public class ProtocolSerializerTests
{
    private static MessageEnvelope CreateValidEnvelope(
        MessageCategory category = MessageCategory.Request,
        string messageType = "test",
        string? correlationId = null,
        long? sequenceNumber = null,
        string payloadJson = "{}")
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return new MessageEnvelope
        {
            Version = ProtocolVersion.V1,
            Category = category,
            MessageType = messageType,
            CorrelationId = correlationId,
            SequenceNumber = sequenceNumber,
            Payload = doc.RootElement.Clone()
        };
    }

    // 1. Valid envelope serialization
    [Fact]
    public void Serialize_ValidRequestEnvelope_ProducesJson()
    {
        var envelope = CreateValidEnvelope(
            MessageCategory.Request, "handshake", "corr-123", null, "{\"clientVersion\":\"1.0\"}");

        var json = ProtocolSerializer.Serialize(envelope);

        Assert.NotNull(json);
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"category\"", json);
        Assert.Contains("\"request\"", json);
        Assert.Contains("\"messageType\"", json);
        Assert.Contains("\"handshake\"", json);
        Assert.Contains("\"correlationId\"", json);
        Assert.Contains("\"corr-123\"", json);
    }

    // 2. Valid envelope deserialization
    [Fact]
    public void Deserialize_ValidJson_ReturnsSuccess()
    {
        var envelope = CreateValidEnvelope();
        var json = ProtocolSerializer.Serialize(envelope);

        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(ProtocolVersion.V1, result.Value.Version);
        Assert.Equal(MessageCategory.Request, result.Value.Category);
        Assert.Equal("test", result.Value.MessageType);
    }

    // 3. Request correlation
    [Fact]
    public void Deserialize_RequestWithCorrelationId_PreservesCorrelation()
    {
        var envelope = CreateValidEnvelope(
            MessageCategory.Request, "getData", "req-abc-456", null, "{\"id\":1}");

        var json = ProtocolSerializer.Serialize(envelope);
        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsSuccess);
        Assert.Equal("req-abc-456", result.Value.CorrelationId);
    }

    // 4. Event/sequence representation
    [Fact]
    public void Deserialize_EventWithSequenceNumber_PreservesSequence()
    {
        var envelope = CreateValidEnvelope(
            MessageCategory.Event, "stateChanged", null, 42, "{\"state\":\"active\"}");

        var json = ProtocolSerializer.Serialize(envelope);
        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(MessageCategory.Event, result.Value.Category);
        Assert.Equal(42, result.Value.SequenceNumber);
    }

    // 5. Invalid JSON handling
    [Fact]
    public void Deserialize_MalformedJson_ReturnsFailure()
    {
        var result = ProtocolSerializer.Deserialize("this is not json at all {{{");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MalformedJson);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsFailure()
    {
        var result = ProtocolSerializer.Deserialize("");

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MalformedJson);
    }

    // 6. Missing required fields
    [Fact]
    public void Deserialize_MissingVersion_ReturnsFailure()
    {
        var json = "{\"category\":\"request\",\"messageType\":\"test\",\"payload\":{}}";

        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Deserialize_MissingCategory_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"messageType\":\"test\",\"payload\":{}}";

        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Deserialize_MissingMessageType_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"request\",\"payload\":{}}";

        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Deserialize_MissingPayload_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"request\",\"messageType\":\"test\"}";

        var result = ProtocolSerializer.Deserialize(json);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }
// 7. Invalid protocol version
    [Fact]
    public void Deserialize_InvalidVersion_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":99,\"minor\":0},\"category\":\"request\",\"messageType\":\"test\",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidProtocolVersion);
    }

    [Fact]
    public void Deserialize_VersionNotAnObject_ReturnsFailure()
    {
        var json = "{\"version\":\"1.0\",\"category\":\"request\",\"messageType\":\"test\",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidProtocolVersion);
    }

    // 8. Unknown message type
    [Fact]
    public void Deserialize_UnknownCategory_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"invalidCategory\",\"messageType\":\"test\",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.UnknownMessageType);
    }

    // 9. Invalid payload handling
    [Fact]
    public void Deserialize_NullPayload_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"request\",\"messageType\":\"test\",\"payload\":null}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidPayload);
    }

    [Fact]
    public void Deserialize_ResponseWithoutCorrelationId_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"response\",\"messageType\":\"result\",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Deserialize_EventWithoutSequenceNumber_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"event\",\"messageType\":\"stateChanged\",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Deserialize_InvalidSequenceNumber_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"event\",\"messageType\":\"ev\",\"sequenceNumber\":-1,\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidSequence);
    }
[Fact]
    public void Deserialize_SequenceNumberZero_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"event\",\"messageType\":\"ev\",\"sequenceNumber\":0,\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidSequence);
    }

    [Fact]
    public void Deserialize_EmptyMessageType_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"request\",\"messageType\":\"  \",\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidIdentifier);
    }

    [Fact]
    public void Deserialize_InvalidCorrelationIdType_ReturnsFailure()
    {
        var json = "{\"version\":{\"major\":1,\"minor\":0},\"category\":\"request\",\"messageType\":\"test\",\"correlationId\":42,\"payload\":{}}";
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidIdentifier);
    }

    // 10. Round-trip serialization/deserialization
    [Fact]
    public void RoundTrip_RequestWithPayload_PreservesAllFields()
    {
        var original = CreateValidEnvelope(
            MessageCategory.Request, "handshake.init", "corr-xyz-999", null,
            "{\"clientName\":\"NexusClient\",\"protocolVersion\":\"1.0\"}");
        var json = ProtocolSerializer.Serialize(original);
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsSuccess);
        var d = result.Value;
        Assert.Equal(original.Version, d.Version);
        Assert.Equal(original.Category, d.Category);
        Assert.Equal(original.MessageType, d.MessageType);
        Assert.Equal(original.CorrelationId, d.CorrelationId);
        Assert.Equal(original.SequenceNumber, d.SequenceNumber);
        Assert.Equal(original.Payload.GetRawText(), d.Payload.GetRawText());
    }

    [Fact]
    public void RoundTrip_EventWithSequence_PreservesAllFields()
    {
        var original = CreateValidEnvelope(
            MessageCategory.Event, "entity.updated", null, 100,
            "{\"entityId\":\"abc\",\"changes\":{\"hp\":80}}");
        var json = ProtocolSerializer.Serialize(original);
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsSuccess);
        var d = result.Value;
        Assert.Equal(original.Version, d.Version);
        Assert.Equal(100, d.SequenceNumber);
        Assert.Equal(original.Payload.GetRawText(), d.Payload.GetRawText());
    }

    [Fact]
    public void RoundTrip_ErrorCategory_PreservesAllFields()
    {
        var original = CreateValidEnvelope(
            MessageCategory.Error, "protocol.error", null, null,
            "{\"code\":\"TIMEOUT\",\"message\":\"Request timed out\"}");
        var json = ProtocolSerializer.Serialize(original);
        var result = ProtocolSerializer.Deserialize(json);
        Assert.True(result.IsSuccess);
        Assert.Equal(MessageCategory.Error, result.Value.Category);
    }

    [Fact]
    public void Deserialize_NullInput_ReturnsFailure()
    {
        var result = ProtocolSerializer.Deserialize(null!);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Deserialize_ArrayInsteadOfObject_ReturnsFailure()
    {
        var result = ProtocolSerializer.Deserialize("[1,2,3]");
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.MalformedJson);
    }

    [Fact]
    public void DeserializePayload_ValidPayload_ReturnsTypedObject()
    {
        var envelope = CreateValidEnvelope(
            MessageCategory.Request, "test", null, null, "{\"name\":\"example\",\"value\":42}");
        var result = ProtocolSerializer.DeserializePayload<TestPayload>(envelope);
        Assert.True(result.IsSuccess);
        Assert.Equal("example", result.Value.Name);
        Assert.Equal(42, result.Value.Value);
    }

    [Fact]
    public void DeserializePayload_InvalidPayload_ReturnsFailure()
    {
        var envelope = CreateValidEnvelope(
            MessageCategory.Request, "test", null, null, "{\"value\":\"not_a_number\"}");
        var result = ProtocolSerializer.DeserializePayload<TestPayload>(envelope);
        Assert.True(result.IsFailure);
        Assert.Contains(result.Errors, e => e.Code == ProtocolErrorCodes.InvalidPayload);
    }

    [Fact]
    public void SerializePayload_RoundTrip_PreservesData()
    {
        var payload = new TestPayload { Name = "roundtrip", Value = 99 };
        var element = ProtocolSerializer.SerializePayload(payload);
        var rawJson = element.GetRawText();
        Assert.Contains("roundtrip", rawJson);
        Assert.Contains("99", rawJson);
    }
}

public class TestPayload
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}