using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Portal.State;

/// <summary>
/// Bindable current-target stats view-model that mirrors Keystone's target data.
/// Provides change notification for all displayed Current Target panel values.
/// </summary>
public sealed class TargetStats : INotifyPropertyChanged
{
    private string _targetId = string.Empty;
    private string _name = string.Empty;
    private int _level;
    private int _hp;
    private int _maxHp = 1;
    private string _identity = string.Empty;
    private bool _isMob;
    private bool _isDead;

    public string TargetId
    {
        get => _targetId;
        set
        {
            if (_targetId != value)
            {
                _targetId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTarget));
                OnPropertyChanged(nameof(HasNoTarget));
                OnPropertyChanged(nameof(NameDisplay));
                OnPropertyChanged(nameof(LevelDisplay));
            }
        }
    }

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(NameDisplay)); } }
    }

    public int Level
    {
        get => _level;
        set { if (_level != value) { _level = value; OnPropertyChanged(); OnPropertyChanged(nameof(LevelDisplay)); } }
    }

    public int Hp
    {
        get => _hp;
        set
        {
            if (_hp != value)
            {
                _hp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HpDisplay));
                OnPropertyChanged(nameof(HpRatio));
            }
        }
    }

    public int MaxHp
    {
        get => _maxHp;
        set
        {
            if (_maxHp != value)
            {
                _maxHp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HpDisplay));
                OnPropertyChanged(nameof(HpRatio));
            }
        }
    }

    public string Identity
    {
        get => _identity;
        set
        {
            if (_identity != value)
            {
                _identity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PortraitImagePath));
            }
        }
    }

    public bool IsMob
    {
        get => _isMob;
        set
        {
            if (_isMob != value)
            {
                _isMob = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PortraitImagePath));
            }
        }
    }

    public bool IsDead
    {
        get => _isDead;
        set { if (_isDead != value) { _isDead = value; OnPropertyChanged(); } }
    }

    // ─── Derived display state ──────────────────────────────────────────

    /// <summary>
    /// True when a target is currently selected (TargetId is non-empty).
    /// </summary>
    public bool HasTarget => !string.IsNullOrEmpty(TargetId);

    /// <summary>
    /// Inverse of <see cref="HasTarget"/> for convenient one-way bindings.
    /// </summary>
    public bool HasNoTarget => !HasTarget;

    /// <summary>
    /// Target name for display. Returns "None" when no target is selected,
    /// otherwise returns <see cref="Name"/>.
    /// </summary>
    public string NameDisplay => HasTarget ? Name : "None";

    /// <summary>
    /// Target level for display. Returns "--" when no target is selected,
    /// otherwise returns <see cref="Level"/> as a string.
    /// </summary>
    public string LevelDisplay => HasTarget ? Level.ToString() : "--";

    /// <summary>
    /// Safe 0.0–1.0 ratio of current HP to max HP.
    /// Never divides by zero; defaults to 0 when MaxHp is invalid.
    /// </summary>
    public double HpRatio => MaxHp > 0 ? (double)Hp / MaxHp : 0;

    /// <summary>
    /// Human-readable "current / max" HP display string.
    /// </summary>
    public string HpDisplay => $"{Hp} / {MaxHp}";

    // ─── Current Target portrait ────────────────────────────────────────

    /// <summary>
    /// Application resource folder holding the target portrait images.
    /// Mirrors the <c>Resource</c> link in Portal.UI.csproj
    /// (<c>..\monsterimages\*.png</c> → <c>Assets\monsterimages\</c>).
    /// </summary>
    private const string MonsterImageFolder = "/Assets/monsterimages/";

    /// <summary>
    /// Fallback portrait path used for player-character targets and for any
    /// mob whose authoritative type has no matching image file.
    /// </summary>
    private const string DefaultMonsterImagePath = MonsterImageFolder + "defaultmonster.png";

    /// <summary>
    /// The authoritative Keystone monster-type categories that have a real,
    /// existing image file in <c>C:\Users\mcdor\portal\monsterimages</c>.
    ///
    /// <para>
    /// Each entry is the category part of an existing
    /// <c>&lt;category&gt;-types.png</c> file found in that folder. These
    /// filenames are the only permitted monster categories: an entry must
    /// never be added or renamed unless the matching file physically exists.
    /// </para>
    /// </summary>
    private static readonly string[] MonsterImageCategories =
    {
        "demon",        // demon-types.png
        "dragon",       // dragon-types.png
        "evil-female",  // evil-female-types.png
        "female-witch", // female-witch-types.png
        "ghoul",        // ghoul-types.png
        "goblin",       // goblin-types.png
        "imp",          // imp-types.png
        "lizard",       // lizard-types.png
        "minotaur",     // minotaur-types.png
        "ogre",         // ogre-types.png
        "orc",          // orc-types.png
        "rat",          // rat-types.png
        "skeleton",     // skeleton-types.png
        "slime",        // slime-types.png
        "spider",       // spider-types.png
        "spirit",       // spirit-types.png
        "stone-giant",  // stone-giant-types.png
        "troll",        // troll-types.png
        "undead",       // undead-types.png
        "werewolf",     // werewolf-types.png
        "wolf",         // wolf-types.png
    };

    /// <summary>
    /// Portrait image path for the Current Target panel.
    ///
    /// <para>
    /// Mob / NPC targets are resolved from the authoritative Keystone monster
    /// type carried by <see cref="Identity"/> (Keystone's
    /// <c>CharacterData.profession_id</c>, i.e. the canonical <c>mob_id</c>,
    /// which is what <c>target.changed</c> already publishes for mobs).
    /// Player-character targets keep the pre-existing behaviour and use the
    /// default portrait.
    /// </para>
    ///
    /// <para>
    /// Falls back to <c>defaultmonster.png</c> whenever no specific image
    /// exists for the authoritative type.
    /// </para>
    /// </summary>
    public string PortraitImagePath
    {
        get
        {
            // Player characters: preserve existing behaviour (no authoritative
            // player portrait system exists), so use the default portrait.
            if (!IsMob)
                return DefaultMonsterImagePath;

            var category = ResolveMonsterImageCategory(Identity);
            return category is null
                ? DefaultMonsterImagePath
                : MonsterImageFolder + category + "-types.png";
        }
    }

    /// <summary>
    /// Resolves an authoritative monster type to one of the existing
    /// <c>&lt;category&gt;-types.png</c> categories using case-insensitive,
    /// deterministic matching. Returns null when nothing matches.
    ///
    /// <para>Matching rule:</para>
    /// <list type="number">
    /// <item>The type is normalised: lower-cased, with every run of
    /// non-alphanumeric characters collapsed to a single <c>-</c> and leading
    /// or trailing separators removed (so <c>"Giant Rat"</c>,
    /// <c>"giant_rat"</c> and <c>"Giant-Rat"</c> normalise identically).</item>
    /// <item>Exact category match wins (e.g. <c>stone_giant</c> →
    /// <c>stone-giant</c>).</item>
    /// <item>Otherwise the longest category whose hyphen-separated tokens
    /// occur as a contiguous whole-token run of the normalised type wins
    /// (e.g. <c>giant_rat</c> → <c>rat</c>).</item>
    /// <item>Ties are broken deterministically by the right-most matching
    /// token run, then by category name (ordinal).</item>
    /// </list>
    /// </summary>
    private static string? ResolveMonsterImageCategory(string? identity)
    {
        var normalized = NormalizeMonsterType(identity);
        if (normalized.Length == 0)
            return null;

        // ─── 1. Exact category match ────────────────────────────────
        foreach (var category in MonsterImageCategories)
        {
            if (string.Equals(category, normalized, StringComparison.Ordinal))
                return category;
        }

        // ─── 2. Longest contiguous whole-token run match ────────────
        var tokens = normalized.Split('-');
        string? best = null;
        int bestSpan = 0;
        int bestStart = -1;

        foreach (var category in MonsterImageCategories)
        {
            var categoryTokens = category.Split('-');
            int span = categoryTokens.Length;
            if (span > tokens.Length)
                continue;

            for (int start = 0; start <= tokens.Length - span; start++)
            {
                if (!TokensMatch(tokens, start, categoryTokens))
                    continue;

                bool better = span > bestSpan;

                if (!better && span == bestSpan)
                {
                    if (start > bestStart)
                        better = true;
                    else if (start == bestStart && best is not null &&
                             string.CompareOrdinal(category, best) < 0)
                        better = true;
                }

                if (better)
                {
                    best = category;
                    bestSpan = span;
                    bestStart = start;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Lower-cases an authoritative monster type and normalises every run of
    /// non-alphanumeric characters to a single <c>-</c> separator.
    /// </summary>
    private static string NormalizeMonsterType(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return string.Empty;

        var builder = new StringBuilder(identity.Length);
        bool lastWasSeparator = true; // suppresses leading separators

        foreach (char ch in identity)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        // Strip a trailing separator (if any).
        int length = builder.Length;
        if (length > 0 && builder[length - 1] == '-')
            builder.Length = length - 1;

        return builder.ToString();
    }

    /// <summary>
    /// True when <paramref name="categoryTokens"/> occur in
    /// <paramref name="tokens"/> starting at <paramref name="start"/>.
    /// </summary>
    private static bool TokensMatch(string[] tokens, int start, string[] categoryTokens)
    {
        for (int i = 0; i < categoryTokens.Length; i++)
        {
            if (!string.Equals(tokens[start + i], categoryTokens[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // ─── Reset ──────────────────────────────────────────────────────────

    /// <summary>
    /// Clears all target state back to the no-target defaults and raises
    /// the required PropertyChanged notifications through normal setters.
    /// </summary>
    public void Clear()
    {
        TargetId = string.Empty;
        Name = string.Empty;
        Level = 0;
        Hp = 0;
        MaxHp = 1;
        Identity = string.Empty;
        IsMob = false;
        IsDead = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}