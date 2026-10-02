namespace Portal.Protocol;

/// <summary>
/// Protocol V1 bank snapshot payload.
/// Pushed by the server via the <c>bank.snapshot</c> event.
/// </summary>
/// <remarks>
/// Keystone's bank is currency only: deposit and withdraw copper, with no
/// fees, interest, overdraft, transfers or item storage. This payload
/// therefore carries the two balances and nothing more. No item-vault model
/// exists because there is no server-side contract to back one.
/// </remarks>
public sealed class BankSnapshotPayload
{
    /// <summary>Whether banking is permitted where the character stands.</summary>
    public bool Available { get; init; }

    /// <summary>Authoritative refusal when <see cref="Available"/> is false.</summary>
    public string? Message { get; init; }

    /// <summary>Copper carried on the character.</summary>
    public int CarriedCurrency { get; init; }

    /// <summary>Copper held in the bank.</summary>
    public int BankCurrency { get; init; }

    public BankSnapshotPayload()
    {
    }
}

/// <summary>
/// Protocol V1 bank result payload.
/// Pushed by the server via the <c>bank.result</c> event after every deposit
/// or withdraw attempt, successful or not.
/// </summary>
public sealed class BankResultPayload
{
    /// <summary>Which operation this reports: "deposit" or "withdraw".</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Whether the server accepted the operation.</summary>
    public bool Success { get; init; }

    /// <summary>
    /// Authoritative outcome text, verbatim from the server, including
    /// refusals such as "Insufficient bank funds: need 900c, have 250c."
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The amount the operation concerned, in copper.</summary>
    public int Amount { get; init; }

    /// <summary>Resulting bank balance, when the server reports one.</summary>
    public int? NewBankCurrency { get; init; }

    public BankResultPayload()
    {
    }
}

/// <summary>
/// Protocol V1 door result payload.
/// Pushed by the server via the <c>door.result</c> event after every door
/// action attempt, successful or not.
/// </summary>
/// <remarks>
/// This payload never contains destination data, so a locked or closed door
/// cannot leak the room beyond it to a client.
/// </remarks>
public sealed class DoorResultPayload
{
    /// <summary>Whether the server carried the action out.</summary>
    public bool Success { get; init; }

    /// <summary>The direction the action targeted, when one was supplied.</summary>
    public string? Direction { get; init; }

    /// <summary>Which action was requested: open, close, lock or unlock.</summary>
    public string? Action { get; init; }

    /// <summary>
    /// Authoritative outcome text, verbatim from the server: "You open the
    /// iron gate.", "The iron gate is locked.", "You do not have the
    /// required key.", "The iron gate is already open." and so on.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    public DoorResultPayload()
    {
    }
}

/// <summary>
/// Protocol V1 WHO snapshot payload.
/// Pushed by the server via the <c>who.snapshot</c> event.
/// </summary>
/// <remarks>
/// Every field here is one the server genuinely publishes through its own
/// WHO command. There is deliberately no title field, because Keystone has
/// no runtime title system to be authoritative about.
/// </remarks>
public sealed class WhoSnapshotPayload
{
    /// <summary>Number of connected characters.</summary>
    public int Count { get; init; }

    /// <summary>One record per connected character, in server order.</summary>
    public IReadOnlyList<WhoRecord> Rows { get; init; } = Array.Empty<WhoRecord>();

    public WhoSnapshotPayload()
    {
    }
}

/// <summary>One connected character, as published by the server's WHO.</summary>
public sealed class WhoRecord
{
    /// <summary>Character name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Current level.</summary>
    public int Level { get; init; }

    /// <summary>Race, as the server formats it.</summary>
    public string Race { get; init; } = string.Empty;

    /// <summary>Profession, as the server formats it.</summary>
    public string Profession { get; init; } = string.Empty;

    /// <summary>Faction affiliation, or empty when unaffiliated.</summary>
    public string Faction { get; init; } = string.Empty;

    /// <summary>Guild affiliation, or empty when none.</summary>
    public string Guild { get; init; } = string.Empty;

    /// <summary>Sect affiliation, or empty when none.</summary>
    public string Sect { get; init; } = string.Empty;

    public WhoRecord()
    {
    }
}
