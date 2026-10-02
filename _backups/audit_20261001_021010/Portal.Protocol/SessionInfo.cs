namespace Portal.Protocol;

/// <summary>
/// Protocol V1 session information payload.
/// Represents an established authenticated session and its associated character identity.
/// </summary>
public sealed class SessionInfo
{
    /// <summary>Unique session identifier assigned by the server.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Unique character identifier associated with this session.</summary>
    public string CharacterId { get; init; } = string.Empty;

    /// <summary>Display name of the character associated with this session.</summary>
    public string CharacterName { get; init; } = string.Empty;

    public SessionInfo()
    {
    }

    public SessionInfo(string sessionId, string characterId, string characterName)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        CharacterId = characterId ?? throw new ArgumentNullException(nameof(characterId));
        CharacterName = characterName ?? throw new ArgumentNullException(nameof(characterName));
    }
}