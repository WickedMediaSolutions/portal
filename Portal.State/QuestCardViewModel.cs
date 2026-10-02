using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// One quest, rendered exactly as the server describes it.
/// </summary>
/// <remarks>
/// Every boolean exposed here (<see cref="CanAccept"/>, <see cref="CanAbandon"/>,
/// <see cref="CanComplete"/>) is copied from the server payload rather than
/// computed locally, which is what keeps the buttons honest: a quest the server
/// will refuse offers no enabled action.
/// </remarks>
public sealed class QuestCardViewModel : INotifyPropertyChanged
{
    private string _state;
    private bool _readyToComplete;
    private bool _canAccept;
    private bool _canAbandon;
    private bool _canComplete;
    private string? _acceptBlockedReason;
    private string? _completeBlockedReason;
    private ObservableCollection<QuestObjectiveViewModel> _objectives = new();
    private QuestRewardsViewModel _rewards = QuestRewardsViewModel.Empty;

    private QuestCardViewModel(QuestRecord record)
    {
        QuestId = record.QuestId;
        Name = string.IsNullOrWhiteSpace(record.Name)
            ? record.QuestId
            : record.Name;
        Description = record.Description ?? string.Empty;
        LevelRequired = record.LevelRequired;
        _state = record.State ?? string.Empty;
        _readyToComplete = record.ReadyToComplete;
        _canAccept = record.CanAccept;
        _canAbandon = record.CanAbandon;
        _canComplete = record.CanComplete;
        _acceptBlockedReason = record.AcceptBlockedReason;
        _completeBlockedReason = record.CompleteBlockedReason;
        GiverName = record.GiverName;
        TurnInName = record.TurnInName;
        Prerequisites = record.Prerequisites ?? Array.Empty<QuestPrerequisite>();
        _objectives = BuildObjectives(record.Objectives);
        _rewards = QuestRewardsViewModel.FromRewards(record.Rewards);
    }

    /// <summary>Builds a card from a server-authored quest record.</summary>
    public static QuestCardViewModel FromRecord(QuestRecord record) =>
        new(record);

    /// <summary>Stable server quest id.</summary>
    public string QuestId { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Objective/flavour description.</summary>
    public string Description { get; }

    /// <summary>Minimum level to accept.</summary>
    public int LevelRequired { get; }

    /// <summary>Giver name, when the server legitimately knows one.</summary>
    public string? GiverName { get; }

    /// <summary>Turn-in name, when the server legitimately knows one.</summary>
    public string? TurnInName { get; }

    /// <summary>Prerequisite quests, named so a lock can be explained.</summary>
    public IReadOnlyList<QuestPrerequisite> Prerequisites { get; }

    /// <summary>The server-reported quest state.</summary>
    public string State
    {
        get => _state;
        private set
        {
            if (_state == value)
                return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanComplete));
        }
    }

    /// <summary>Objectives with authoritative counts.</summary>
    public ObservableCollection<QuestObjectiveViewModel> Objectives
    {
        get => _objectives;
        private set
        {
            _objectives = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ObjectiveSummary));
        }
    }

    /// <summary>Rewards the server grants on completion.</summary>
    public QuestRewardsViewModel Rewards
    {
        get => _rewards;
        private set
        {
            _rewards = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RewardsSummary));
        }
    }

    /// <summary>Whether the server reports every objective satisfied.</summary>
    public bool ReadyToComplete
    {
        get => _readyToComplete;
        private set
        {
            if (_readyToComplete == value)
                return;
            _readyToComplete = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// Whether the server would accept this quest right now.
    /// </summary>
    /// <remarks>
    /// Completed quests are never repeatable, so this is forced off for them
    /// even if a payload somehow said otherwise.
    /// </remarks>
    public bool CanAccept
    {
        get => _canAccept && !IsCompleted;
        private set
        {
            if (_canAccept == value)
                return;
            _canAccept = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the server would abandon this quest right now.</summary>
    public bool CanAbandon
    {
        get => _canAbandon;
        private set
        {
            if (_canAbandon == value)
                return;
            _canAbandon = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the server would complete this quest right now.
    /// </summary>
    /// <remarks>
    /// Completed quests are never repeatable, so this is forced off for them
    /// even if a payload somehow said otherwise.
    /// </remarks>
    public bool CanComplete
    {
        get => _canComplete && !IsCompleted;
        private set
        {
            if (_canComplete == value)
                return;
            _canComplete = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Server explanation for a refused acceptance.</summary>
    public string? AcceptBlockedReason
    {
        get => _acceptBlockedReason;
        private set
        {
            if (_acceptBlockedReason == value)
                return;
            _acceptBlockedReason = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Server explanation for a refused completion.</summary>
    public string? CompleteBlockedReason
    {
        get => _completeBlockedReason;
        private set
        {
            if (_completeBlockedReason == value)
                return;
            _completeBlockedReason = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether the server reports this quest as completed.</summary>
    public bool IsCompleted =>
        string.Equals(State, QuestStates.Completed, StringComparison.Ordinal);

    /// <summary>Whether the server reports this quest as in progress.</summary>
    public bool IsActive =>
        string.Equals(State, QuestStates.Active, StringComparison.Ordinal);

    /// <summary>
    /// A short status line. Progress is always expressed as text, never by
    /// colour alone, so it stays readable for colour-blind players and in
    /// high-contrast themes.
    /// </summary>
    public string StatusText => IsCompleted
        ? "Completed"
        : ReadyToComplete
            ? "Ready to Complete"
            : IsActive
                ? "In progress"
                : "Available";

    /// <summary>Concatenated objective progress, e.g. "Skeleton Warriors 2 / 3".</summary>
    public string ObjectiveSummary
    {
        get
        {
            if (Objectives.Count == 0)
                return "No objectives.";
            return string.Join("  |  ",
                Objectives.Select(o => $"{o.TargetName} {o.ProgressText}"));
        }
    }

    /// <summary>Human-readable rewards summary.</summary>
    public string RewardsSummary => Rewards.Summary;

    /// <summary>Prerequisite names, or an empty string when there are none.</summary>
    public string PrerequisiteSummary =>
        Prerequisites.Count == 0
            ? string.Empty
            : "Requires: " + string.Join(", ", Prerequisites.Select(p => p.Name));

    /// <summary>
    /// Applies an incremental progress record from the server.
    /// </summary>
    /// <remarks>
    /// Counts are absolute rather than deltas, so applying the same event twice
    /// is idempotent and can never move a counter backwards.
    /// </remarks>
    public void ApplyProgress(QuestProgressRecord record)
    {
        State = record.State ?? State;
        ReadyToComplete = record.ReadyToComplete;
        CanAbandon = record.CanAbandon;
        CanComplete = record.CanComplete;

        var updated = BuildObjectives(record.Objectives);
        if (updated.Count > 0)
            Objectives = updated;
    }

    private static ObservableCollection<QuestObjectiveViewModel> BuildObjectives(
        IReadOnlyList<QuestObjective>? objectives)
    {
        var list = new ObservableCollection<QuestObjectiveViewModel>();
        if (objectives is null)
            return list;
        foreach (var objective in objectives)
            list.Add(QuestObjectiveViewModel.FromObjective(objective));
        return list;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
