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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}