namespace Portal.Protocol;

/// <summary>
/// Standard protocol error codes used in <see cref="ProtocolError"/> messages
/// and in deserialization validation results.
/// </summary>
public static class ProtocolErrorCodes
{
    public const string MalformedJson = "MALFORMED_JSON";
    public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
    public const string InvalidProtocolVersion = "INVALID_PROTOCOL_VERSION";
    public const string UnknownMessageType = "UNKNOWN_MESSAGE_TYPE";
    public const string InvalidIdentifier = "INVALID_IDENTIFIER";
    public const string InvalidSequence = "INVALID_SEQUENCE";
    public const string InvalidPayload = "INVALID_PAYLOAD";
}