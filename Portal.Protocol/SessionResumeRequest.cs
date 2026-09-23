namespace Portal.Protocol;

public sealed class SessionResumeRequest
{
    public string SessionId { get; init; } = string.Empty;

    public SessionResumeRequest()
    {
    }

    public SessionResumeRequest(string sessionId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }
}