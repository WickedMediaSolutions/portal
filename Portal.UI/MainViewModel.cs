using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Portal.Networking;
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
    private string _connectionTarget = PortalEndpoints.ProductionDisplayName;
    private ObservableCollection<RoomEntityRecord> _roomEntities = new();
    private int _roomEntityCount;
    private RoomEntityRecord? _selectedRoomEntity;
    private ObservableCollection<SkillRecord> _skills = new();
    private string _movementMessage = string.Empty;
    private ObservableCollection<InventoryItemRecord> _inventoryItems = new();
    private ObservableCollection<EquippedItemRecord> _equippedItems = new();
    private int _currency;

    /// <summary>
    /// The realm map panel's view-model: Keystone's filtered, fog-of-war
    /// respecting map plus the local view controls (filters, zoom, selection).
    /// </summary>
    /// <remarks>
    /// Exposed here so the whole window shares a single <see cref="MainViewModel"/>
    /// as its DataContext, exactly like every other panel. It is populated only
    /// from the server's map events; Portal never loads world data locally.
    /// </remarks>
    private readonly MapViewModel _map = new();

    /// <summary>The realm map view-model bound by the map panel.</summary>
    public MapViewModel Map => _map;

    // ─── Room service context view-models ───────────────────────────
    //
    // These three plus Room make up the contextual gameplay surface. Each
    // is populated ONLY from its own authoritative server payload; none of
    // them derives anything from map data or recomputes a server value.

    private readonly RoomContextViewModel _room = new();
    private readonly ShopViewModel _shop = new();
    private readonly BankViewModel _bank = new();
    private readonly WhoViewModel _who = new();
    private readonly QuestState _quests = new();

    /// <summary>
    /// Quest panel state: active, available and completed quests, all derived
    /// solely from server-authoritative payloads.
    /// </summary>
    public QuestState Quests => _quests;

    /// <summary>
    /// The current room: authoritative name and description, its exits, the
    /// dynamic door state of those exits, and which services it offers.
    /// </summary>
    public RoomContextViewModel Room => _room;

    /// <summary>Shop panel state: room shops, selected shop, wares, results.</summary>
    public ShopViewModel Shop => _shop;

    /// <summary>Bank panel state: carried and banked copper, last result.</summary>
    public BankViewModel Bank => _bank;

    /// <summary>WHO panel state: connected characters.</summary>
    public WhoViewModel Who => _who;

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
    /// Concise, port-free label of the currently selected connection target,
    /// shown beside the local-development toggle ("Production" or
    /// "Local Development").
    ///
    /// Deliberately carries no URL and no port number: ordinary players only
    /// need to know which server they are pointed at, and the raw endpoint
    /// remains a developer detail.
    /// </summary>
    public string ConnectionTarget
    {
        get => _connectionTarget;
        set { if (_connectionTarget != value) { _connectionTarget = value; OnPropertyChanged(); } }
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
        set
        {
            if (_equippedItems == value) return;
            _equippedItems = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EquippedItemsWithoutArtwork));
        }
    }

    /// <summary>
    /// The exact set of Keystone <c>EquipmentSlot</c> values that have a
    /// layered character-panel image in this client.
    /// </summary>
    /// <remarks>
    /// These are the raw Keystone slot strings, NOT the asset file names, so
    /// the set can be compared directly against the <c>slot</c> field of an
    /// <see cref="EquippedItemRecord"/>. Keystone's full slot enum is:
    /// head, chest, legs, hands, feet, wrists, left_finger, right_finger,
    /// neck, left_ear, right_ear, waist, back, main_hand, off_hand,
    /// left_hand, right_hand.
    /// </remarks>
    private static readonly HashSet<string> SlotsWithArtwork = new(StringComparer.Ordinal)
    {
        "head", "chest", "wrists", "left_finger", "right_finger",
        "left_ear", "right_ear", "waist", "legs", "feet", "hands",
        "off_hand", "main_hand",
    };

    /// <summary>
    /// Occupied equipment slots that have NO layered panel image.
    /// </summary>
    /// <remarks>
    /// Keystone's <c>back</c> slot (44 cloak/cape catalogue items) has no
    /// artwork in this client. Without this list such an item would be
    /// equipped on the server but completely invisible in the panel, with
    /// no click target to inspect or unequip it — effectively a stuck item.
    /// Listing them keeps every equipped item visible and removable. The
    /// collection is derived purely from the authoritative
    /// <c>equipment.snapshot</c>; it never invents an entry.
    /// </remarks>
    public IEnumerable<EquippedItemRecord> EquippedItemsWithoutArtwork =>
        _equippedItems.Where(e =>
            !string.IsNullOrEmpty(e.Slot) && !SlotsWithArtwork.Contains(e.Slot));

    /// <summary>True when at least one equipped slot has no layered image.</summary>
    public bool HasEquippedItemsWithoutArtwork =>
        EquippedItemsWithoutArtwork.Any();

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

        // Mirror the authoritative carried balance into the shop panel so it
        // can show what the player holds. This is display only: the server
        // still decides affordability on every purchase, and the same
        // inventory.snapshot is re-sent after each successful transaction.
        Shop.Currency = currency;
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

    // ─── Character Point allocation dialog ───────────────────────────────
    //
    // The dialog owns ONLY the player's unsaved choices. It is seeded from
    // the authoritative CharacterStats snapshot and never writes back to it:
    // only a successful Keystone commit changes real state, and that arrives
    // as a fresh character.points.snapshot event.

    /// <summary>
    /// The stat-allocation dialog view-model, or null when the dialog is
    /// closed. Bound to the modal's DataContext.
    /// </summary>
    private StatAllocationViewModel? _statAllocation;
    public StatAllocationViewModel? StatAllocation
    {
        get => _statAllocation;
        set
        {
            if (_statAllocation != value)
            {
                _statAllocation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStatAllocationOpen));
            }
        }
    }

    /// <summary>Whether the allocation modal is currently open.</summary>
    public bool IsStatAllocationOpen => _statAllocation is not null;

    /// <summary>
    /// Opens the allocation dialog, seeded from the current authoritative
    /// Character Point state.  No pending choices are carried over.
    /// </summary>
    public void OpenStatAllocation()
    {
        StatAllocation = new StatAllocationViewModel(
            _character.CharacterPoints,
            _character.AllocatableStats);
    }

    /// <summary>Closes the allocation dialog, discarding pending choices.</summary>
    public void CloseStatAllocation()
    {
        StatAllocation = null;
    }
}

/// <summary>
/// One row in the allocation dialog: an authoritative stat plus the player's
/// unsaved pending change for it.
///
/// <see cref="CanDecrement"/> is only true while <see cref="Pending"/> is
/// greater than zero, which guarantees the dialog can never reduce an
/// already-earned permanent stat — <see cref="Value"/> itself is immutable
/// here and only the pending delta is editable.
/// </summary>
public sealed class StatAllocationRowViewModel : INotifyPropertyChanged
{
    private int _pending;

    internal StatAllocationRowViewModel(AllocatableStatViewModel stat)
    {
        Stat = stat;
    }

    /// <summary>The authoritative stat this row edits.</summary>
    public AllocatableStatViewModel Stat { get; }

    public string StatId => Stat.StatId;
    public string Name => Stat.Name;
    public int Value => Stat.Value;
    public int Cap => Stat.Cap;
    public bool IsCapped => Stat.IsCapped;

    /// <summary>Unsaved points staged for this stat (never negative).</summary>
    public int Pending
    {
        get => _pending;
        internal set
        {
            if (_pending == value) return;
            _pending = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(Resulting));
            OnPropertyChanged(nameof(CanDecrement));
        }
    }

    /// <summary>Whether this stat has a staged change.</summary>
    public bool HasPending => _pending > 0;

    /// <summary>The value this stat would become if applied.</summary>
    public int Resulting => Stat.Value + _pending;

    /// <summary>Whether Minus is available (only with a staged change).</summary>
    public bool CanDecrement => _pending > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// View-model for the Character Point allocation dialog.
///
/// <para>
/// A purely local "shopping basket" of unsaved choices: the player can add and
/// remove pending points freely before committing. It never mutates the
/// character's real stats — that only happens when Keystone accepts the
/// allocation and pushes a new authoritative snapshot.
/// </para>
///
/// <para>
/// <see cref="Minus"/> can only remove points from the CURRENT pending
/// allocation. It can never reduce a stat the character has already earned.
/// </para>
/// </summary>
public sealed class StatAllocationViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, int> _pending = new();
    private readonly int _pointsAvailable;
    private int _pointsRemaining;

    public StatAllocationViewModel(
        int pointsAvailable,
        IReadOnlyList<AllocatableStatViewModel> stats)
    {
        _pointsAvailable = pointsAvailable;
        _pointsRemaining = pointsAvailable;
        Rows = stats.Select(s => new StatAllocationRowViewModel(s)).ToArray();
    }

    /// <summary>One editable row per canonical allocatable stat.</summary>
    public IReadOnlyList<StatAllocationRowViewModel> Rows { get; }

    /// <summary>The authoritative unspent balance this dialog works from.</summary>
    public int PointsAvailable => _pointsAvailable;

    /// <summary>
    /// Points left after the pending allocation. Never negative — Plus is
    /// disabled before it could go below zero.
    /// </summary>
    public int PointsRemaining
    {
        get => _pointsRemaining;
        private set
        {
            if (_pointsRemaining == value) return;
            _pointsRemaining = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PointsRemainingDisplay));
            RefreshRows();
        }
    }

    /// <summary>Prominent "Points Remaining: X" label text.</summary>
    public string PointsRemainingDisplay => $"Points Remaining: {PointsRemaining}";

    /// <summary>Total pending points across all stats.</summary>
    public int TotalPending => Rows.Sum(r => r.Pending);

    /// <summary>Whether anything is pending — gates the Apply button.</summary>
    public bool HasPending => TotalPending > 0;

    private bool CanIncrementCore(StatAllocationRowViewModel row) =>
        PointsRemaining > 0 && !row.IsCapped && row.Resulting < row.Cap;

    /// <summary>Adds one pending point to a stat, if the rules allow it.</summary>
    public void Plus(StatAllocationRowViewModel row)
    {
        if (row is null || !CanIncrementCore(row)) return;
        row.Pending += 1;
        AfterChange();
    }

    /// <summary>
    /// Removes ONE pending point from a stat. This only ever reduces the
    /// unsaved allocation — the already-earned permanent value is untouched.
    /// </summary>
    public void Minus(StatAllocationRowViewModel row)
    {
        if (row is null || row.Pending <= 0) return;
        row.Pending -= 1;
        AfterChange();
    }

    /// <summary>
    /// Clears every unsaved choice made in this dialog.  Does not refund
    /// anything that was already committed to Keystone.
    /// </summary>
    public void Reset()
    {
        foreach (var row in Rows) row.Pending = 0;
        _pointsRemaining = _pointsAvailable;
        OnPropertyChanged(nameof(PointsRemaining));
        OnPropertyChanged(nameof(PointsRemainingDisplay));
        OnPropertyChanged(nameof(TotalPending));
        OnPropertyChanged(nameof(HasPending));
        RefreshRows();
    }

    /// <summary>
    /// Builds the complete proposed allocation for the single Apply request.
    /// Empty when nothing is pending, which the caller treats as a no-op.
    /// </summary>
    public IReadOnlyList<StatAllocationEntry> BuildAllocation()
    {
        return Rows
            .Where(r => r.Pending > 0)
            .Select(r => new StatAllocationEntry(r.StatId, r.Pending))
            .ToArray();
    }

    private void AfterChange()
    {
        _pointsRemaining = _pointsAvailable - TotalPending;
        OnPropertyChanged(nameof(PointsRemaining));
        OnPropertyChanged(nameof(PointsRemainingDisplay));
        OnPropertyChanged(nameof(TotalPending));
        OnPropertyChanged(nameof(HasPending));
        RefreshRows();
    }

    private void RefreshRows()
    {
        // Rows read PointsRemaining through this notification, so a wildcard
        // change re-evaluates every Plus enablement in one pass.
        OnPropertyChanged(string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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