using Portal.Core.Results;
using Portal.Networking;
using Portal.Protocol;

namespace Portal.Services;

/// <summary>
/// Sends authoritative game-play commands from Portal to Keystone over
/// the existing <see cref="IWebSocketConnection"/>.
///
/// This service is a fire-and-forget sender. It does NOT consume
/// <c>ReceiveAsync</c>.  All resulting state returns through the existing
/// <see cref="GameEventService"/> event stream.
/// </summary>
public sealed class CombatService
{
    private readonly IWebSocketConnection _connection;

    public CombatService(IWebSocketConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Sends a combat.attack.request command to Keystone for the specified
    /// authoritative TargetId.
    ///
    /// Does NOT read a response.  The resulting combat state (character.update,
    /// target.changed, target.update) arrives through the existing event stream.
    /// </summary>
    /// <param name="targetId">The stable Keystone database primary key of the
    /// target, serialised as a string (str(target.id)).</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendAttackAsync(string targetId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(targetId))
            return Result.Failure(
                "COMBAT_NO_TARGET",
                "Cannot attack: no target selected.");

        var request = new CombatAttackRequest(targetId);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.CombatAttackRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }

    /// <summary>
    /// Sends a combat.skill.request command to Keystone to activate an active
    /// unlocked skill for the authenticated character.
    ///
    /// Does NOT read a response.  The resulting state (character.update,
    /// target.update) arrives through the existing event stream.
    /// </summary>
    /// <param name="skillId">The Keystone skill identifier (e.g. "acid_blast").
    /// Must be non-empty.</param>
    /// <param name="targetId">The stable Keystone database primary key of the
    /// target, serialised as a string, or null when the skill does not require
    /// a target.</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendSkillAsync(string skillId, string? targetId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(skillId))
            return Result.Failure(
                "SKILL_NO_ID",
                "Cannot use skill: no SkillId provided.");

        var request = new CombatSkillRequest(skillId, targetId);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.CombatSkillRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }

    /// <summary>
    /// Sends a target.select.request command to Keystone for the specified
    /// TargetId.  This is a fire-and-forget send — Keystone validates the
    /// selection authoritatively and emits a target.changed event if accepted.
    ///
    /// Does NOT read a response.  The resulting target state arrives through
    /// the existing GameEventService event stream as a target.changed event.
    /// </summary>
    /// <param name="targetId">The stable Keystone database primary key of the
    /// intended target, serialised as a string (str(target.id)).</param>
    /// <param name="ct">Cancellation token for the send operation.</param>
    public async Task<Result> SendTargetSelectAsync(string targetId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(targetId))
            return Result.Failure(
                "TARGET_SELECT_NO_ID",
                "Cannot select target: no TargetId provided.");

        var request = new TargetSelectRequest(targetId);

        var envelope = new MessageEnvelope(
            ProtocolVersion.Current,
            MessageCategory.Request,
            ProtocolMessageTypes.TargetSelectRequest,
            Guid.NewGuid().ToString("N"),
            null,
            ProtocolSerializer.SerializePayload(request));

        return await _connection.SendAsync(envelope, ct);
    }
}