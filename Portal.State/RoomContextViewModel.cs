using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Bindable view-model for the character's CURRENT room and the services and
/// doors it offers.
/// </summary>
/// <remarks>
/// <para>
/// Every value here is a straight copy of an authoritative server payload.
/// The view-model never derives a room name from map data, never guesses
/// service availability from map markers, and never computes a price — it
/// only ever renders what <c>room.state</c>, <c>shop.snapshot</c> and
/// <c>bank.snapshot</c> actually said.
/// </para>
/// <para>
/// Applying a new room state REPLACES the previous one wholesale, so a
/// service or door that is no longer present disappears from the UI
/// immediately rather than lingering as a stale control.
/// </para>
/// </remarks>
public sealed class RoomContextViewModel : INotifyPropertyChanged
{
    private string _roomName = string.Empty;
    private string _roomDescription = string.Empty;
    private string _areaId = string.Empty;
    private RoomExitRecord? _selectedDoor;
    private string _doorFeedback = string.Empty;
    private bool _doorFeedbackIsSuccess;

    /// <summary>Every exit leaving the current room, in server order.</summary>
    public ObservableCollection<RoomExitRecord> Exits { get; } = new();

    /// <summary>
    /// Only the exits that are genuine doors — the ones the door controls
    /// act on. Non-door exits are excluded entirely rather than shown with
    /// dead buttons.
    /// </summary>
    public ObservableCollection<RoomExitRecord> Doors { get; } = new();

    /// <summary>Shop ids linked to this room (empty when there are none).</summary>
    public ObservableCollection<string> ShopIds { get; } = new();

    /// <summary>Whether this room permits banking.</summary>
    public bool HasBank { get; private set; }

    /// <summary>
    /// Whether this is a designated healing/start room.
    /// </summary>
    /// <remarks>
    /// Informational only. Keystone's healing room is a passive regeneration
    /// multiplier with no command, price or instant effect, so this flag
    /// deliberately drives a status label and nothing else — there is no
    /// heal button anywhere in Portal.
    /// </remarks>
    public bool IsHealingRoom { get; private set; }

    /// <summary>Authoritative room name from the server.</summary>
    public string RoomName
    {
        get => _roomName;
        private set { if (_roomName != value) { _roomName = value; OnPropertyChanged(); } }
    }

    /// <summary>Authoritative room description from the server.</summary>
    public string RoomDescription
    {
        get => _roomDescription;
        private set { if (_roomDescription != value) { _roomDescription = value; OnPropertyChanged(); } }
    }

    /// <summary>Canonical world room id, when the room belongs to the realm.</summary>
    public string AreaId
    {
        get => _areaId;
        private set { if (_areaId != value) { _areaId = value; OnPropertyChanged(); } }
    }

    /// <summary>True when the room offers at least one shop.</summary>
    public bool HasShops => ShopIds.Count > 0;

    /// <summary>True when the room has at least one door to operate.</summary>
    public bool HasDoors => Doors.Count > 0;

    /// <summary>True when the room name or description is worth displaying.</summary>
    public bool HasRoomText =>
        !string.IsNullOrWhiteSpace(RoomName) || !string.IsNullOrWhiteSpace(RoomDescription);

    /// <summary>
    /// The door the door controls most recently acted on, or null.
    /// </summary>
    /// <remarks>
    /// Retained purely so the panel can report which door a result refers
    /// to. Each door row carries its own buttons and needs no selection, so
    /// this is never used to decide what a click does.
    /// </remarks>
    public RoomExitRecord? SelectedDoor
    {
        get => _selectedDoor;
        set
        {
            if (!ReferenceEquals(_selectedDoor, value))
            {
                _selectedDoor = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Latest door action outcome text, verbatim from the server.</summary>
    public string DoorFeedback
    {
        get => _doorFeedback;
        private set { if (_doorFeedback != value) { _doorFeedback = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Whether <see cref="DoorFeedback"/> reports a success. This is a
    /// non-colour cue: the UI renders a leading "OK:" / "Failed:" label as
    /// well as a colour, so the outcome is never conveyed by colour alone.
    /// </summary>
    public bool DoorFeedbackIsSuccess
    {
        get => _doorFeedbackIsSuccess;
        private set { if (_doorFeedbackIsSuccess != value) { _doorFeedbackIsSuccess = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The door feedback rendered with an explicit text marker, so success
    /// and failure stay distinguishable without relying on colour.
    /// </summary>
    public string DoorFeedbackDisplay =>
        string.IsNullOrEmpty(DoorFeedback)
            ? string.Empty
            : (DoorFeedbackIsSuccess ? "OK: " : "Failed: ") + DoorFeedback;

    /// <summary>True when a door result is waiting to be shown.</summary>
    public bool HasDoorFeedback => !string.IsNullOrEmpty(DoorFeedback);

    /// <summary>
    /// Replaces the whole current-room view-model with an authoritative
    /// <c>room.state</c> payload.
    /// </summary>
    /// <remarks>
    /// Full-replacement semantics, matching how the map applies a snapshot:
    /// anything absent from the payload is genuinely unavailable here, so
    /// the UI must not keep showing it.
    /// </remarks>
    public void ApplyRoomState(RoomStatePayload payload)
    {
        RoomName = payload.Name ?? string.Empty;
        RoomDescription = payload.Description ?? string.Empty;
        AreaId = payload.WorldRoomId ?? string.Empty;

        Exits.Clear();
        Doors.Clear();
        foreach (var exit in payload.Exits ?? Array.Empty<RoomExitRecord>())
        {
            Exits.Add(exit);
            if (exit.IsDoor)
                Doors.Add(exit);
        }

        ShopIds.Clear();
        foreach (var shopId in payload.Services?.ShopIds ?? Array.Empty<string>())
            ShopIds.Add(shopId);

        HasBank = payload.Services?.Bank ?? false;
        IsHealingRoom = payload.Services?.HealingRoom ?? false;

        // Keep a door selected across a refresh when the same door is still
        // here, so a re-pushed room.state (after a door action, or a
        // reconnect) never silently discards the panel's context.
        var previous = SelectedDoor;
        SelectedDoor = previous is null
            ? null
            : Doors.FirstOrDefault(d => d.Direction == previous.Direction);

        OnPropertyChanged(nameof(HasShops));
        OnPropertyChanged(nameof(HasDoors));
        OnPropertyChanged(nameof(HasRoomText));
    }

    /// <summary>Records the authoritative outcome of a door action.</summary>
    public void ApplyDoorResult(DoorResultPayload payload)
    {
        DoorFeedbackIsSuccess = payload.Success;
        DoorFeedback = payload.Message ?? string.Empty;
        OnPropertyChanged(nameof(DoorFeedbackDisplay));
        OnPropertyChanged(nameof(HasDoorFeedback));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
