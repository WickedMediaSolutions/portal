using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// One quest objective and its authoritative progress.
/// </summary>
/// <remarks>
/// The server supplies <see cref="TargetName"/>, so the UI shows readable text
/// such as "Skeleton Warrior" and never has to know a mob or item id.
/// <see cref="ProgressText"/> renders the count as text ("2 / 3") so progress
/// is never communicated by colour alone.
/// </remarks>
public sealed class QuestObjectiveViewModel : INotifyPropertyChanged
{
    private int _current;
    private int _required;
    private bool _met;

    private QuestObjectiveViewModel(QuestObjective objective)
    {
        Index = objective.Index;
        Type = objective.Type ?? string.Empty;
        TargetId = objective.TargetId ?? string.Empty;
        TargetName = string.IsNullOrWhiteSpace(objective.TargetName)
            ? TargetId
            : objective.TargetName;
        _current = objective.Current;
        _required = objective.Required;
        _met = objective.Met;
    }

    /// <summary>Builds an objective row from a server-authored record.</summary>
    public static QuestObjectiveViewModel FromObjective(QuestObjective objective) =>
        new(objective);

    /// <summary>Objective slot index within the quest.</summary>
    public int Index { get; }

    /// <summary>"kill" or "collect", as reported by the server.</summary>
    public string Type { get; }

    /// <summary>Stable target id, retained for identity.</summary>
    public string TargetId { get; }

    /// <summary>Canonical display name resolved by the server.</summary>
    public string TargetName { get; }

    /// <summary>Progress so far, exactly as the server counts it.</summary>
    public int Current
    {
        get => _current;
        private set
        {
            if (_current == value)
                return;
            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Count required to satisfy this objective.</summary>
    public int Required
    {
        get => _required;
        private set
        {
            if (_required == value)
                return;
            _required = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    /// <summary>Whether the server reports this objective satisfied.</summary>
    public bool Met
    {
        get => _met;
        private set
        {
            if (_met == value)
                return;
            _met = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Text form of progress, e.g. "2 / 3".</summary>
    public string ProgressText => $"{Current} / {Required}";

    /// <summary>A short verb label for the objective kind.</summary>
    public string TypeLabel =>
        string.Equals(Type, "kill", StringComparison.OrdinalIgnoreCase)
            ? "Slay"
            : string.Equals(Type, "collect", StringComparison.OrdinalIgnoreCase)
                ? "Gather"
                : Type;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// The rewards a quest grants, rendered from the server's own summary.
/// </summary>
/// <remarks>
/// Only real, server-declared rewards appear. A loot-table reward is shown as
/// a possibility, never as a guaranteed drop, because the roll happens
/// server-side at completion.
/// </remarks>
public sealed class QuestRewardsViewModel
{
    private QuestRewardsViewModel()
    {
    }

    /// <summary>An empty, reward-free view model used as a safe default.</summary>
    public static QuestRewardsViewModel Empty { get; } = new();

    /// <summary>Experience awarded on completion.</summary>
    public int Xp { get; private init; }

    /// <summary>Copper awarded on completion.</summary>
    public int Currency { get; private init; }

    /// <summary>Items awarded on completion.</summary>
    public IReadOnlyList<QuestRewardItem> Items { get; private init; } =
        Array.Empty<QuestRewardItem>();

    /// <summary>Optional bonus drop, described as a possibility.</summary>
    public QuestRewardLoot? Loot { get; private init; }

    /// <summary>Whether the quest grants nothing at all.</summary>
    public bool IsEmpty =>
        Xp <= 0 && Currency <= 0 && Items.Count == 0 && Loot is null;

    /// <summary>Builds a rewards view model from a server payload.</summary>
    public static QuestRewardsViewModel FromRewards(QuestRewards? rewards)
    {
        if (rewards is null)
            return new QuestRewardsViewModel();

        return new QuestRewardsViewModel
        {
            Xp = rewards.Xp,
            Currency = rewards.Currency,
            Items = rewards.Items ?? Array.Empty<QuestRewardItem>(),
            Loot = rewards.Loot,
        };
    }

    /// <summary>Readable one-line rewards summary.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (Xp > 0)
                parts.Add($"{Xp} XP");
            if (Currency > 0)
                parts.Add($"{Currency}c");
            foreach (var item in Items)
                parts.Add($"{item.Quantity}x {item.Name}");
            if (Loot is not null)
                parts.Add(Loot.Description);
            return parts.Count == 0 ? "No rewards" : string.Join("  •  ", parts);
        }
    }
}