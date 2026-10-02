using System.Text.Json;
using Portal.Core.Results;

namespace Portal.Protocol;

public static partial class ProtocolSerializer
{
    private static Result<MessageEnvelope> ValidateAndBuild(JsonElement root)
    {
        var errors = new List<Core.Results.Error>();

        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MalformedJson,
                "Protocol message root must be a JSON object."));
            return Result<MessageEnvelope>.Failure(errors);
        }

        var versionResult = ValidateVersion(root, errors);
        var categoryResult = ValidateCategory(root, errors);
        var messageTypeResult = ValidateMessageType(root, errors);
        var correlationId = ValidateCorrelationId(root, errors);
        var sequenceNumber = ValidateSequenceNumber(root, errors);
        var payload = ValidatePayload(root, errors);

        if (categoryResult.valid)
        {
            switch (categoryResult.category)
            {
                case MessageCategory.Response:
                    if (string.IsNullOrEmpty(correlationId))
                        errors.Add(new Core.Results.Error(
                            ProtocolErrorCodes.MissingRequiredField,
                            "Response messages must include a non-empty correlationId."));
                    break;
                case MessageCategory.Event:
                    if (!sequenceNumber.HasValue)
                        errors.Add(new Core.Results.Error(
                            ProtocolErrorCodes.MissingRequiredField,
                            "Event messages must include a positive sequenceNumber."));
                    break;
            }
        }

        if (errors.Count > 0)
            return Result<MessageEnvelope>.Failure(errors);

        if (!versionResult.valid || !categoryResult.valid || string.IsNullOrEmpty(messageTypeResult.messageType))
            return Result<MessageEnvelope>.Failure(
                ProtocolErrorCodes.MalformedJson,
                "Failed to construct envelope after validation.");

        return Result<MessageEnvelope>.Success(new MessageEnvelope
        {
            Version = versionResult.version,
            Category = categoryResult.category,
            MessageType = messageTypeResult.messageType,
            CorrelationId = correlationId,
            SequenceNumber = sequenceNumber,
            Payload = payload
        });
    }
private static (ProtocolVersion version, bool valid) ValidateVersion(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "version", out var ve))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Required field 'version' is missing."));
            return (default, false);
        }
        if (ve.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidProtocolVersion,
                "Field 'version' must be a JSON object."));
            return (default, false);
        }
        if (!TryGetProperty(ve, "major", out var maj) || !maj.TryGetInt32(out var major) ||
            !TryGetProperty(ve, "minor", out var min) || !min.TryGetInt32(out var minor))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidProtocolVersion,
                "Version must contain integer 'major' and 'minor' fields."));
            return (default, false);
        }
        var v = new ProtocolVersion(major, minor);
        if (!SupportedVersions.Contains(v))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidProtocolVersion,
                $"Protocol version {v} is not supported."));
            return (default, false);
        }
        return (v, true);
    }
private static (MessageCategory category, bool valid) ValidateCategory(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "category", out var ce))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Required field 'category' is missing."));
            return (default, false);
        }
        if (ce.ValueKind != JsonValueKind.String)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Field 'category' must be a string."));
            return (default, false);
        }
        var catStr = ce.GetString();
        if (!Enum.TryParse<MessageCategory>(catStr, ignoreCase: true, out var cat))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.UnknownMessageType,
                $"Unknown message category '{catStr}'."));
            return (default, false);
        }
        return (cat, true);
    }

    private static (string messageType, bool valid) ValidateMessageType(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "messageType", out var te))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Required field 'messageType' is missing."));
            return (string.Empty, false);
        }
        if (te.ValueKind != JsonValueKind.String)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Field 'messageType' must be a string."));
            return (string.Empty, false);
        }
        var mt = te.GetString()!;
        if (string.IsNullOrWhiteSpace(mt))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidIdentifier,
                "Field 'messageType' must not be empty or whitespace."));
            return (string.Empty, false);
        }
        return (mt, true);
    }

    private static string? ValidateCorrelationId(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "correlationId", out var ce))
            return null;
        if (ce.ValueKind == JsonValueKind.Null)
            return null;
        if (ce.ValueKind != JsonValueKind.String)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidIdentifier,
                "Field 'correlationId' must be a string or null."));
            return null;
        }
        var cid = ce.GetString();
        if (string.IsNullOrWhiteSpace(cid))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidIdentifier,
                "Field 'correlationId' must not be empty or whitespace."));
            return null;
        }
        return cid;
    }

    private static long? ValidateSequenceNumber(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "sequenceNumber", out var se))
            return null;
        if (se.ValueKind == JsonValueKind.Null)
            return null;
        if (se.ValueKind != JsonValueKind.Number)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidSequence,
                "Field 'sequenceNumber' must be a number or null."));
            return null;
        }
        if (!se.TryGetInt64(out var seq))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidSequence,
                "Field 'sequenceNumber' must be a valid 64-bit integer."));
            return null;
        }
        if (seq <= 0)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidSequence,
                "Field 'sequenceNumber' must be positive when present."));
            return null;
        }
        return seq;
    }

    private static JsonElement ValidatePayload(
        JsonElement root, List<Core.Results.Error> errors)
    {
        if (!TryGetProperty(root, "payload", out var pe))
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.MissingRequiredField,
                "Required field 'payload' is missing."));
            return default;
        }
        if (pe.ValueKind == JsonValueKind.Null)
        {
            errors.Add(new Core.Results.Error(
                ProtocolErrorCodes.InvalidPayload,
                "Field 'payload' must not be null. Use {} for empty bodies."));
            return default;
        }
        return pe.Clone();
    }
private static bool TryGetProperty(
        JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
            return true;

        var camel = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
        if (camel != propertyName && element.TryGetProperty(camel, out value))
            return true;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Name, camel, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}