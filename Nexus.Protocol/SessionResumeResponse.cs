namespace Nexus.Protocol;

public sealed class SessionResumeResponse
{
    public bool Success { get; init; } = false;
    public SessionInfo? Session { get; init; } = null;
    public string? ErrorCode { get; init; } = null;
    public string? ErrorMessage { get; init; } = null;

    public SessionResumeResponse()
    {
    }

    public SessionResumeResponse(
        bool success,
        SessionInfo? session,
        string? errorCode,
        string? errorMessage)
    {
        Success = success;
        Session = session;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }
}