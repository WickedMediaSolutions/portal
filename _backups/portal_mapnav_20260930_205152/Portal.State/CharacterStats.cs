using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Portal.State;

/// <summary>
/// Bindable character stats view-model that mirrors Keystone's CharacterData.
/// Provides change notification for all displayed character panel values.
/// </summary>
public sealed class CharacterStats : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _raceName = string.Empty;
    private string _professionName = string.Empty;
    private int _level;
    private int _xp;
    private int _xpForNextLevel = 1;
    private int _hp;
    private int _maxHp = 1;
    private int _mana;
    private int _maxMana = 1;
    private int _stamina;
    private int _maxStamina = 1;
    private bool _hasMana;
    private string _statusState = string.Empty;
    private bool _isAlive = true;
    private bool _isStatusKnown;
    private int _characterPoints;
    private int _pointsPerLevel;
    private IReadOnlyList<AllocatableStatViewModel> _allocatableStats =
        Array.Empty<AllocatableStatViewModel>();
    private string? _characterPointsError;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string RaceName
    {
        get => _raceName;
        set { if (_raceName != value) { _raceName = value; OnPropertyChanged(); } }
    }

    public string ProfessionName
    {
        get => _professionName;
        set { if (_professionName != value) { _professionName = value; OnPropertyChanged(); } }
    }

    public int Level
    {
        get => _level;
        set { if (_level != value) { _level = value; OnPropertyChanged(); } }
    }

    public int Xp
    {
        get => _xp;
        set { if (_xp != value) { _xp = value; OnPropertyChanged(); OnPropertyChanged(nameof(XpDisplay)); } }
    }

    public int XpForNextLevel
    {
        get => _xpForNextLevel;
        set { if (_xpForNextLevel != value) { _xpForNextLevel = value; OnPropertyChanged(); OnPropertyChanged(nameof(XpDisplay)); } }
    }

    public string XpDisplay => XpForNextLevel > 0
        ? $"{Xp:N0} / {XpForNextLevel:N0}"
        : $"{Xp:N0}";

    public int Hp
    {
        get => _hp;
        set { if (_hp != value) { _hp = value; OnPropertyChanged(); OnPropertyChanged(nameof(HpDisplay)); OnPropertyChanged(nameof(HpRatio)); } }
    }

    public int MaxHp
    {
        get => _maxHp;
        set { if (_maxHp != value) { _maxHp = value; OnPropertyChanged(); OnPropertyChanged(nameof(HpDisplay)); OnPropertyChanged(nameof(HpRatio)); } }
    }

    public string HpDisplay => $"{Hp} / {MaxHp}";
    public double HpRatio => MaxHp > 0 ? (double)Hp / MaxHp : 0;

    public int Mana
    {
        get => _mana;
        set { if (_mana != value) { _mana = value; OnPropertyChanged(); OnPropertyChanged(nameof(ManaDisplay)); OnPropertyChanged(nameof(ManaRatio)); } }
    }

    public int MaxMana
    {
        get => _maxMana;
        set { if (_maxMana != value) { _maxMana = value; OnPropertyChanged(); OnPropertyChanged(nameof(ManaDisplay)); OnPropertyChanged(nameof(ManaRatio)); } }
    }

    public string ManaDisplay => $"{Mana} / {MaxMana}";
    public double ManaRatio => MaxMana > 0 ? (double)Mana / MaxMana : 0;

    /// <summary>
    /// Whether this character's profession supports mana.
    /// When false, the MA bar should be hidden in the UI.
    /// </summary>
    public bool HasMana
    {
        get => _hasMana;
        set { if (_hasMana != value) { _hasMana = value; OnPropertyChanged(); } }
    }

    public int Stamina
    {
        get => _stamina;
        set { if (_stamina != value) { _stamina = value; OnPropertyChanged(); OnPropertyChanged(nameof(StaminaDisplay)); OnPropertyChanged(nameof(StaminaRatio)); } }
    }

    public int MaxStamina
    {
        get => _maxStamina;
        set { if (_maxStamina != value) { _maxStamina = value; OnPropertyChanged(); OnPropertyChanged(nameof(StaminaDisplay)); OnPropertyChanged(nameof(StaminaRatio)); } }
    }

    public string StaminaDisplay => $"{Stamina} / {MaxStamina}";
    public double StaminaRatio => MaxStamina > 0 ? (double)Stamina / MaxStamina : 0;

    // ─── Authoritative character status (character.status.snapshot) ────────────
    // Keystone owns no buff / debuff / condition system. The complete
    // authoritative status is the CharacterState value plus is_alive().

    /// <summary>
    /// Latest authoritative Keystone CharacterState value received via
    /// character.status.snapshot ("standing", "resting", "meditating",
    /// "combating", "dead"). Empty until the first snapshot arrives.
    /// </summary>
    public string StatusState
    {
        get => _statusState;
        set
        {
            if (_statusState == value) return;
            _statusState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusStateDisplay));
            OnPropertyChanged(nameof(IsStanding));
            OnPropertyChanged(nameof(IsResting));
            OnPropertyChanged(nameof(IsMeditating));
            OnPropertyChanged(nameof(IsInCombat));
            OnPropertyChanged(nameof(IsDead));
        }
    }

    /// <summary>
    /// Authoritative CharacterData.is_alive() flag from the latest
    /// character.status.snapshot.
    /// </summary>
    public bool IsAlive
    {
        get => _isAlive;
        set { if (_isAlive != value) { _isAlive = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Whether an authoritative character.status.snapshot has been received.
    /// While false, all status LEDs stay dim/off.
    /// </summary>
    public bool IsStatusKnown
    {
        get => _isStatusKnown;
        set { if (_isStatusKnown != value) { _isStatusKnown = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusStateDisplay)); } }
    }

    // ─── Derived LED flags — computed strictly from the authoritative state ────
    public bool IsStanding => StatusState == "standing";
    public bool IsResting => StatusState == "resting";
    public bool IsMeditating => StatusState == "meditating";
    public bool IsInCombat => StatusState == "combating";
    public bool IsDead => StatusState == "dead";

    /// <summary>Human-readable authoritative state for display.</summary>
    public string StatusStateDisplay => !IsStatusKnown
        ? "--"
        : StatusState switch
        {
            "standing" => "Standing",
            "resting" => "Resting",
            "meditating" => "Meditating",
            "combating" => "In Combat",
            "dead" => "Dead",
            "" => "--",
            _ => StatusState,
        };

    /// <summary>
    /// Applies a complete authoritative status snapshot received via
    /// character.status.snapshot. Called by GameEventService on the UI
    /// dispatcher thread. Replacement semantics: the payload is the full
    /// authoritative status, never a delta.
    /// </summary>
    public void ApplyStatusSnapshot(string? statusState, bool isAlive)
    {
        var newState = statusState ?? string.Empty;
        var stateChanged = _statusState != newState;

        _statusState = newState;
        _isAlive = isAlive;
        var knownChanged = !_isStatusKnown;
        _isStatusKnown = true;

        if (stateChanged)
        {
            OnPropertyChanged(nameof(StatusState));
            OnPropertyChanged(nameof(StatusStateDisplay));
            OnPropertyChanged(nameof(IsStanding));
            OnPropertyChanged(nameof(IsResting));
            OnPropertyChanged(nameof(IsMeditating));
            OnPropertyChanged(nameof(IsInCombat));
            OnPropertyChanged(nameof(IsDead));
        }

        OnPropertyChanged(nameof(IsAlive));

        if (knownChanged)
        {
            OnPropertyChanged(nameof(IsStatusKnown));
            OnPropertyChanged(nameof(StatusStateDisplay));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ─── Character Points ────────────────────────────────────────────────
    //
    // Mirrors Keystone's authoritative character.points.snapshot. Portal
    // holds no independent point/progression model: the balance, the stat
    // values, and the caps are all read straight from the server snapshot
    // and are only ever replaced wholesale.

    /// <summary>
    /// Unspent Character Points from the latest authoritative snapshot.
    /// Never a client-computed value.
    /// </summary>
    public int CharacterPoints
    {
        get => _characterPoints;
        set
        {
            if (_characterPoints == value) return;
            _characterPoints = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CharacterPointsDisplay));
            OnPropertyChanged(nameof(HasUnspentCharacterPoints));
        }
    }

    /// <summary>Keystone's points-per-level rate, for display only.</summary>
    public int PointsPerLevel
    {
        get => _pointsPerLevel;
        set { if (_pointsPerLevel != value) { _pointsPerLevel = value; OnPropertyChanged(); } }
    }

    /// <summary>Human-readable balance for the character panel.</summary>
    public string CharacterPointsDisplay => CharacterPoints.ToString();

    /// <summary>
    /// Whether the Allocate affordance should be visually emphasised.
    /// The control stays visible when this is false, just subdued.
    /// </summary>
    public bool HasUnspentCharacterPoints => CharacterPoints > 0;

    /// <summary>
    /// The complete authoritative list of allocatable stats. Replacement
    /// semantics — never partially updated.
    /// </summary>
    public IReadOnlyList<AllocatableStatViewModel> AllocatableStats
    {
        get => _allocatableStats;
        private set
        {
            _allocatableStats = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAllocateCharacterPoints));
        }
    }

    /// <summary>
    /// The most recent Keystone rejection message, or null. Cleared as soon
    /// as a new clean snapshot arrives.
    /// </summary>
    public string? CharacterPointsError
    {
        get => _characterPointsError;
        private set
        {
            if (_characterPointsError == value) return;
            _characterPointsError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCharacterPointsError));
        }
    }

    public bool HasCharacterPointsError => !string.IsNullOrEmpty(_characterPointsError);

    /// <summary>Whether the Allocate button can be opened at all.</summary>
    public bool CanAllocateCharacterPoints => CharacterPoints > 0;

    /// <summary>
    /// Applies a complete authoritative character.points.snapshot.
    /// Called by GameEventService on the UI dispatcher thread.
    /// Replacement semantics: the payload is always the full authoritative
    /// state, never a delta.
    /// </summary>
    public void ApplyCharacterPointsSnapshot(
        int availablePoints,
        int pointsPerLevel,
        IReadOnlyList<(string StatId, string Name, int Value, int Cap)> stats,
        string? error)
    {
        _characterPoints = availablePoints;
        _pointsPerLevel = pointsPerLevel;
        _characterPointsError = error;

        _allocatableStats = stats
            .Select(s => new AllocatableStatViewModel(s.StatId, s.Name, s.Value, s.Cap))
            .ToArray();

        OnPropertyChanged(nameof(CharacterPoints));
        OnPropertyChanged(nameof(CharacterPointsDisplay));
        OnPropertyChanged(nameof(HasUnspentCharacterPoints));
        OnPropertyChanged(nameof(PointsPerLevel));
        OnPropertyChanged(nameof(AllocatableStats));
        OnPropertyChanged(nameof(CanAllocateCharacterPoints));
        OnPropertyChanged(nameof(CharacterPointsError));
        OnPropertyChanged(nameof(HasCharacterPointsError));
    }
}

/// <summary>
/// One allocatable permanent stat in the Portal character panel / allocation
/// dialog. Purely a mirror of Keystone's authoritative stat; Portal never
/// decides a value or a cap itself.
/// </summary>
public sealed class AllocatableStatViewModel : INotifyPropertyChanged
{
    public AllocatableStatViewModel(string statId, string name, int value, int cap)
    {
        StatId = statId;
        Name = name;
        Value = value;
        Cap = cap;
    }

    /// <summary>Canonical Keystone stat id (e.g. "str").</summary>
    public string StatId { get; }

    /// <summary>Player-facing stat name (e.g. "Strength").</summary>
    public string Name { get; }

    /// <summary>Current authoritative permanent value.</summary>
    public int Value { get; }

    /// <summary>Authoritative maximum this stat may reach.</summary>
    public int Cap { get; }

    /// <summary>Whether this stat has reached its ceiling.</summary>
    public bool IsCapped => Value >= Cap;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}