using System.Text.Json;
using Portal.Core.Results;

namespace Portal.Protocol;

/// <summary>
/// Provides deterministic JSON serialization and validated deserialization
/// for Portal Protocol V1 messages.
///
/// All deserialization validates untrusted input before producing a usable
/// <see cref="MessageEnvelope"/>. Invalid input never throws; it returns
/// a failed <see cref="Result{T}"/>.
/// </summary>
public static partial class ProtocolSerializer
{
    private static readonly HashSet<ProtocolVersion> SupportedVersions = new()
    {
        ProtocolVersion.V1
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };
    static ProtocolSerializer()
    {
        SerializeOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        DeserializeOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Serializes a <see cref="MessageEnvelope"/> to a deterministic JSON string.
    /// </summary>
    public static string Serialize(MessageEnvelope envelope)
    {
        if (envelope is null)
            throw new ArgumentNullException(nameof(envelope));

        return JsonSerializer.Serialize(envelope, SerializeOptions);
    }

    /// <summary>
    /// Deserializes and validates a JSON string into a <see cref="MessageEnvelope"/>.
    /// All validation is performed before returning a success result.
    /// </summary>
    /// <param name="json">The raw JSON string from the wire.</param>
    /// <returns>
    /// A successful <see cref="Result{MessageEnvelope}"/> with the parsed and validated
    /// envelope, or a failed result with one or more protocol errors.
    /// </returns>
    public static Result<MessageEnvelope> Deserialize(string json)
    {
        if (json is null)
            return Result<MessageEnvelope>.Failure(
                ProtocolErrorCodes.MissingRequiredField,
                "Input JSON string is null.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result<MessageEnvelope>.Failure(
                ProtocolErrorCodes.MalformedJson,
                $"Failed to parse JSON: {ex.Message}");
        }

        using (document)
        {
            return ValidateAndBuild(document.RootElement);
        }
    }

    /// <summary>
    /// Deserializes a typed payload from the envelope's <see cref="MessageEnvelope.Payload"/>.
    /// This is the second-stage deserialization used by higher-level handlers.
    /// </summary>
    public static Result<T> DeserializePayload<T>(MessageEnvelope envelope) where T : class
    {
        if (envelope is null)
            return Result<T>.Failure(
                ProtocolErrorCodes.InvalidPayload,
                "Envelope is null.");

        try
        {
            var payload = JsonSerializer.Deserialize<T>(
                envelope.Payload.GetRawText(), DeserializeOptions);

            if (payload is null)
                return Result<T>.Failure(
                    ProtocolErrorCodes.InvalidPayload,
                    $"Payload deserialized to null for type {typeof(T).Name}.");

            return Result<T>.Success(payload);
        }
        catch (JsonException ex)
        {
            return Result<T>.Failure(
                ProtocolErrorCodes.InvalidPayload,
                $"Failed to deserialize payload as {typeof(T).Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Serializes a typed payload object to a <see cref="JsonElement"/>
    /// suitable for embedding in a <see cref="MessageEnvelope.Payload"/>.
    /// </summary>
    public static JsonElement SerializePayload<T>(T payload)
    {
        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}