namespace Nexus.Protocol;

/// <summary>
/// Top-level message classification used in the protocol envelope.
/// Every protocol message belongs to exactly one category.
/// </summary>
public enum MessageCategory
{
    /// <summary>Outbound request expecting a correlated response.</summary>
    Request,

    /// <summary>Response correlated to a prior request.</summary>
    Response,

    /// <summary>Server-pushed event with optional sequence information.</summary>
    Event,

    /// <summary>Protocol-level error or control message.</summary>
    Error
}