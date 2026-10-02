using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Portal.Protocol;
using Portal.State;

namespace Portal.UI;

/// <summary>
/// Top-level view-model for the MainWindow, exposing bindable state
/// for all panels.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private CharacterStats _character = new();
    private TargetStats _target = new();
    private string _connectionStatus = "Disconnected";
    private string _sessionStatus = "Disconnected";
    private string _latency = "-- ms";
    private string _sessionId = "--";
    private ObservableCollection<RoomEntityRecord> _roomEntities = new();
    private int _roomEntityCount;
    private RoomEntityRecord? _selectedRoomEntity;
    private ObservableCollection<SkillRecord> _skills = new();
    private string _movementMessage = string.Empty;
    private ObservableCollection<InventoryItemRecord> _inventoryItems = new();
    private ObservableCollection<EquippedItemRecord> _equippedItems = new();
    private int _currency;

    public CharacterStats Character
    {
        get => _character;
        set { if (_character != value) { _character = value; OnPropertyChanged(); } }
    }

    public TargetStats Target
    {
        get => _target;
        set { if (_target != value) { _target = value; OnPropertyChanged(); } }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { if (_connectionStatus != value) { _connectionStatus = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Authoritative connection/session state shown by the bottom status bar.
    /// Updated by MainWindow from the real authentication state
    /// (Disconnected / Connecting... / Authenticating... / Connected).
    ///
    /// Kept separate from <see cref="ConnectionStatus"/>, which also carries
    /// transient login-bar messages (validation and failure text) and is
    /// therefore not a reliable connection state for the status bar.
    /// </summary>
    public string SessionStatus
    {
        get => _sessionStatus;
        set { if (_sessionStatus != value) { _sessionStatus = value; OnPropertyChanged(); } }
    }

    public string Latency
    {
        get => _latency;
        set { if (_latency != value) { _latency = value; OnPropertyChanged(); } }
    }

    public string SessionId
    {
        get => _sessionId;
        set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Observable collection of entities currently in the character's room.
    /// Updated via full replacement when the server sends a
    /// <c>room.entity.snapshot</c> event.
    /// </summary>
    public ObservableCollection<RoomEntityRecord> RoomEntities
    {
        get => _roomEntities;
        set { if (_roomEntities != value) { _roomEntities = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Count of room entities for simple bindings.
    /// </summary>
    public int RoomEntityCount
    {
        get => _roomEntityCount;
        private set { if (_roomEntityCount != value) { _roomEntityCount = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Observable collection of skills unlocked by the authenticated character.
    /// Updated via full replacement when the server sends a
    /// <c>character.skills.snapshot</c> event.
    /// </summary>
    public ObservableCollection<SkillRecord> Skills
    {
        get => _skills;
        set { if (_skills != value) { _skills = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveSkills)); } }
    }

    /// <summary>
    /// Filtered enumerable of unlocked ACTIVE (non-passive) skills only.
    /// Used by the Spells and Abilities panel for activation buttons.
    /// </summary>
    public IEnumerable<SkillRecord> ActiveSkills =>
        _skills.Where(s => !s.IsPassive);

    /// <summary>
    /// Replaces the entire skills list with a new authoritative snapshot
    /// from the server.  Called by <see cref="Services.GameEventService"/>
    /// on the UI dispatcher thread.
    /// </summary>
    public void ReplaceSkills(IReadOnlyList<SkillRecord> skills)
    {
        _skills.Clear();
        foreach (var skill in skills)
        {
            _skills.Add(skill);
        }
        OnPropertyChanged(nameof(ActiveSkills));
    }

    /// <summary>
    /// Latest authoritative movement failure message, or empty when no
    /// failure is currently displayed.  Set by <see cref="Services.GameEventService"/>
    /// when receiving a <c>movement.failed</c> event; cleared locally
    /// before each new directional movement attempt.
    /// </summary>
    public string MovementMessage
    {
        get => _movementMessage;
        set { if (_movementMessage != value) { _movementMessage = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Observable collection of items currently in the character's inventory.
    /// Updated via full replacement when the server sends an
    /// <c>inventory.snapshot</c> event.
    /// </summary>
    public ObservableCollection<InventoryItemRecord> InventoryItems
    {
        get => _inventoryItems;
        set { if (_inventoryItems != value) { _inventoryItems = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Observable collection of items currently equipped by the character.
    /// Updated via full replacement when the server sends an
    /// <c>equipment.snapshot</c> event.  Contains only occupied slots;
    /// empty slots are omitted.
    /// </summary>
    public ObservableCollection<EquippedItemRecord> EquippedItems
    {
        get => _equippedItems;
        set { if (_equippedItems != value) { _equippedItems = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Current currency balance in copper pieces.
    /// </summary>
    public int Currency
    {
        get => _currency;
        set { if (_currency != value) { _currency = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrencyDisplay)); } }
    }

    /// <summary>
    /// Human-readable currency display string (e.g. "1g 2s 34c").
    /// Computed from <see cref="Currency"/>.
    /// </summary>
    public string CurrencyDisplay
    {
        get
        {
            if (_currency == 0)
                return "0c";

            int copper = _currency;
            int platinum = copper / 1_000_000;
            copper %= 1_000_000;
            int gold = copper / 10_000;
            copper %= 10_000;
            int silver = copper / 100;
            copper %= 100;

            var parts = new System.Collections.Generic.List<string>();
            if (platinum > 0) parts.Add($"{platinum}p");
            if (gold > 0) parts.Add($"{gold}g");
            if (silver > 0) parts.Add($"{silver}s");
            if (copper > 0 || parts.Count == 0) parts.Add($"{copper}c");

            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Replaces the entire inventory list with a new authoritative snapshot
    /// from the server.  Called by <see cref=\"Services.GameEventService\"/>
    /// on the UI dispatcher thread.
    /// </summary>
    public void ReplaceInventory(IReadOnlyList<InventoryItemRecord> items, int currency)
    {
        var newCollection = new ObservableCollection<InventoryItemRecord>();
        foreach (var item in items)
        {
            newCollection.Add(item);
        }
        InventoryItems = newCollection;
        Currency = currency;
    }

    /// <summary>
    /// Replaces the entire equipped items list with a new authoritative snapshot
    /// from the server.  Called by <see cref="Services.GameEventService"/>
    /// on the UI dispatcher thread.
    /// </summary>
    public void ReplaceEquipment(IReadOnlyList<EquippedItemRecord> equipped)
    {
        var newCollection = new ObservableCollection<EquippedItemRecord>();
        foreach (var item in equipped)
        {
            newCollection.Add(item);
        }
        EquippedItems = newCollection;
    }

    /// <summary>
    /// The item currently displayed in the item information card, or null
    /// when no card is shown.  The card can display either an equipped item
    /// or an inventory item — both conform to the same metadata fields.
    /// Set to null to dismiss the card.
    /// </summary>
    private EquipmentCardItem? _selectedCardItem;
    public EquipmentCardItem? SelectedCardItem
    {
        get => _selectedCardItem;
        set { if (_selectedCardItem != value) { _selectedCardItem = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The player's local selection from the current room entity snapshot.
    /// Remains null when no entity is selected or when the room is empty.
    /// This is NOT the authoritative current target — it is purely a local
    /// UI choice pending a future target.select.request.
    /// </summary>
    public RoomEntityRecord? SelectedRoomEntity
    {
        get => _selectedRoomEntity;
        set { if (_selectedRoomEntity != value) { _selectedRoomEntity = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Replaces the entire room entity list with a new authoritative snapshot
    /// from the server.  Called by <see cref="Services.GameEventService"/>
    /// on the UI dispatcher thread.
    /// </summary>
    public void ReplaceRoomEntities(IReadOnlyList<RoomEntityRecord> entities)
    {
        var previousSelectedId = _selectedRoomEntity?.TargetId;

        _roomEntities.Clear();
        foreach (var entity in entities)
        {
            _roomEntities.Add(entity);
        }
        RoomEntityCount = _roomEntities.Count;

        // Refresh local selection: keep the same entity if it still exists
        // in the new snapshot, otherwise clear.
        SelectedRoomEntity = previousSelectedId is not null
            ? _roomEntities.FirstOrDefault(e => e.TargetId == previousSelectedId)
            : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Lightweight display record for the item information card.
/// Carries a copy of the item's player-visible metadata so the card can
/// display it regardless of whether the source was an equipment slot or
/// an inventory slot.
/// </summary>
public sealed class EquipmentCardItem
{
    public string Name { get; init; } = string.Empty;
    public string? Slot { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
    public string? DamageType { get; init; }
    public int? BaseDamage { get; init; }
    public int? DamageMin { get; init; }
    public int? DamageMax { get; init; }
    public int? ArmorClass { get; init; }
    public int? Level { get; init; }
    public int? Strength { get; init; }
    public int? Speed { get; init; }
    public int? Encumbrance { get; init; }

    public bool HasStats =>
        DamageType is not null ||
        BaseDamage.HasValue ||
        DamageMin.HasValue ||
        DamageMax.HasValue ||
        ArmorClass.HasValue ||
        Level.HasValue ||
        Strength.HasValue ||
        Speed.HasValue ||
        Encumbrance.HasValue;
}