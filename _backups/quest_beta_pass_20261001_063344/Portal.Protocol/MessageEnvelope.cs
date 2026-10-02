using System.Text.Json;

namespace Portal.Protocol;

/// <summary>
/// The common protocol envelope that wraps all Portal Protocol V1 messages.
///
/// Every message on the wire is a JSON object conforming to this structure.
/// The <see cref="Payload"/> contains the category-specific body which is
/// deserialized separately by higher-level protocol handlers.
/// </summary>
public sealed class MessageEnvelope
{
    /// <summary>Protocol version used to encode this message.</summary>
    public ProtocolVersion Version { get; init; }

    /// <summary>Top-level message classification.</summary>
    public MessageCategory Category { get; init; }

    /// <summary>
    /// The specific message type name within the category
    /// (e.g., "handshake", "authentication", "snapshot").
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// Correlation identifier linking a response to its originating request.
    /// Required for <see cref="MessageCategory.Response"/> messages;
    /// optional for <see cref="MessageCategory.Request"/> messages;
    /// should be null for events and errors unless correlating to a prior exchange.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number for ordered event streams.
    /// Required for <see cref="MessageCategory.Event"/> messages when
    /// sequence ordering is in effect; null otherwise.
    /// </summary>
    public long? SequenceNumber { get; init; }

    /// <summary>
    /// The category-specific message body as a raw JSON element.
    /// Must not be <see cref="JsonValueKind.Undefined"/> or <see cref="JsonValueKind.Null"/>
    /// for well-formed protocol messages.
    /// </summary>
    public JsonElement Payload { get; init; }

    /// <summary>
    /// Creates a new envelope with the given parameters.
    /// </summary>
    public MessageEnvelope(
        ProtocolVersion version,
        MessageCategory category,
        string messageType,
        string? correlationId,
        long? sequenceNumber,
        JsonElement payload)
    {
        Version = version;
        Category = category;
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        CorrelationId = correlationId;
        SequenceNumber = sequenceNumber;
        Payload = payload;
    }

    /// <summary>
    /// Parameterless constructor for deserialization.
    /// </summary>
    public MessageEnvelope()
    {
    }
}