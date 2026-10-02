using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Receives the four server-pushed map events and applies them to the client's
/// map state.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the UI layer's map host. <see cref="GameEventService"/> owns
/// deserialization and UI-thread dispatching exactly as it does for every other
/// event; this interface is only the hand-off point, so map handling follows the
/// same dispatcher conventions as inventory, equipment and target state.
/// </para>
/// <para>
/// Implementations are called on the WPF Dispatcher thread and are the ONLY
/// place a map payload is allowed to mutate visible state.
/// </para>
/// </remarks>
public interface IMapEventSink
{
    /// <summary>Applies a complete filtered <c>map.snapshot</c> (full replacement).</summary>
    void ApplyMapSnapshot(MapSnapshotPayload payload);

    /// <summary>Applies one newly discovered room.</summary>
    void ApplyMapRoomDiscovered(MapRoomDiscoveredPayload payload);

    /// <summary>Applies the character's current room/area/position/floor.</summary>
    void ApplyMapPosition(MapPositionPayload payload);

    /// <summary>Applies discovery counters.</summary>
    void ApplyMapState(MapStatePayload payload);
}
