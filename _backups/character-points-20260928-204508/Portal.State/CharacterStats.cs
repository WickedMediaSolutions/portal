using System.ComponentModel;
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
}