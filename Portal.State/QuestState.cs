using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>The quest states Keystone reports, mirrored as string constants.</summary>
public static class QuestStates
{
    /// <summary>Accepted and in progress.</summary>
    public const string Active = "active";

    /// <summary>Finished; not repeatable.</summary>
    public const string Completed = "completed";

    /// <summary>Not yet accepted but currently offered.</summary>
    public const string Available = "available";

    /// <summary>Known but not currently attainable.</summary>
    public const string Locked = "locked";
}

/// <summary>
/// The client's single source of truth for quest state, built exclusively from
/// server-authoritative payloads.
/// </summary>
/// <remarks>
/// <para>
/// This class deliberately contains NO quest rules. It never decides whether a
/// quest can be accepted, abandoned or completed, never recomputes readiness,
/// and never derives availability from level or prerequisites. Every one of
/// those facts arrives from Keystone and is merely stored and grouped here.
/// </para>
/// <para>
/// That is what guarantees the graphical client and a text client can never
/// disagree: there is exactly one decision-maker, and it is the server.
/// </para>
/// </remarks>
public sealed class QuestState : INotifyPropertyChanged
{
    private QuestCardViewModel? _selectedQuest;
    private string? _lastResultMessage;

    /// <summary>Quests the server reports as active, in server order.</summary>
    public ObservableCollection<QuestCardViewModel> ActiveQuests { get; } = new();

    /// <summary>Quests the server currently reports as available.</summary>
    public ObservableCollection<QuestCardViewModel> AvailableQuests { get; } = new();

    /// <summary>Completed history, newest last, in server order.</summary>
    public ObservableCollection<QuestCardViewModel> CompletedQuests { get; } = new();

    /// <summary>The quest currently expanded in the detail pane.</summary>
    public QuestCardViewModel? SelectedQuest
    {
        get => _selectedQuest;
        set
        {
            if (ReferenceEquals(_selectedQuest, value))
                return;
            _selectedQuest = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The most recent authoritative accept / abandon / complete outcome, or
    /// null when there has not been one.
    /// </summary>
    public string? LastResultMessage
    {
        get => _lastResultMessage;
        private set
        {
            if (_lastResultMessage == value)
                return;
            _lastResultMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResultMessage));
        }
    }

    /// <summary>Whether there is a result message to show.</summary>
    public bool HasResultMessage => !string.IsNullOrEmpty(_lastResultMessage);

    /// <summary>Whether the character has any quests at all.</summary>
    public bool HasAnyQuest =>
        ActiveQuests.Count > 0 || AvailableQuests.Count > 0 ||
        CompletedQuests.Count > 0;

    /// <summary>Whether anything is currently in progress.</summary>
public bool HasActiveQuests => ActiveQuests.Count > 0;

    /// <summary>Whether any quest has been completed.</summary>
    public bool HasCompletedQuests => CompletedQuests.Count > 0;

    /// <summary>Whether a quest is selected in the detail pane.</summary>
    public bool HasSelectedQuest => SelectedQuest is not null;

    /// <summary>
    /// Whether the quest log popup is open. Purely local presentation state —
    /// it never affects what the server is told.
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
                return;
            _isOpen = value;
            OnPropertyChanged();
        }
    }

    private bool _isOpen;

    /// <summary>
    /// Replaces the entire quest view with a server snapshot.
    /// </summary>
    /// <remarks>
    /// A snapshot is the whole truth, so the existing collections are rebuilt
    /// from scratch. This is what makes a reconnect restore everything the
    /// player saw before, with no client-side memory required.
    /// </remarks>
    public void ApplySnapshot(QuestSnapshotPayload payload)
    {
        ActiveQuests.Clear();
        AvailableQuests.Clear();
        CompletedQuests.Clear();

        if (payload?.Quests is null)
        {
            SelectedQuest = null;
            NotifyCounts();
            return;
        }

        foreach (var record in payload.Quests)
        {
            var card = QuestCardViewModel.FromRecord(record);
            switch (card.State)
            {
                case QuestStates.Active:
                    ActiveQuests.Add(card);
                    break;
                case QuestStates.Completed:
                    CompletedQuests.Add(card);
                    break;
                default:
                    // "available", and any future server-defined state that is
                    // not active, are grouped as available: the server has
                    // already decided this quest may be looked at.
                    AvailableQuests.Add(card);
                    break;
            }
        }

        // Keep the detail pane pointing at the same quest across a refresh.
        if (_selectedQuest is not null)
        {
            SelectedQuest = FindById(_selectedQuest.QuestId)
                ?? ActiveQuests.FirstOrDefault();
        }
        else
        {
            SelectedQuest = ActiveQuests.FirstOrDefault();
        }

        NotifyCounts();
    }

    /// <summary>
    /// Applies incremental progress for the quests that advanced.
    /// </summary>
    /// <remarks>
    /// Only quests named in the payload are touched, so an unrelated kill or
    /// pickup changes nothing. Counts are absolute, so replaying the same event
    /// is idempotent and can never make a counter move backwards.
    /// </remarks>
    public void ApplyProgress(QuestProgressPayload payload)
    {
        if (payload?.Quests is null)
            return;

        bool touched = false;
        foreach (var record in payload.Quests)
        {
            var card = FindById(record.QuestId);
            if (card is null)
                continue;

            card.ApplyProgress(record);
            touched = true;

            // A quest can change group when progress completes it, so
            // re-bucket it after applying the new state.
            Rebucket(card);
        }

        if (touched)
            NotifyCounts();
    }

    /// <summary>
    /// Records the authoritative outcome of an accept / abandon / complete
    /// attempt so the UI can report success or refusal verbatim.
    /// </summary>
    public void ApplyResult(QuestResultPayload payload)
    {
        LastResultMessage = payload?.Message;
    }

    /// <summary>Looks a quest up across every group by its server id.</summary>
    public QuestCardViewModel? FindById(string questId) =>
        ActiveQuests.FirstOrDefault(q => q.QuestId == questId)
        ?? AvailableQuests.FirstOrDefault(q => q.QuestId == questId)
        ?? CompletedQuests.FirstOrDefault(q => q.QuestId == questId);

    /// <summary>Clears every quest, used when a session ends.</summary>
    public void Clear()
    {
        ActiveQuests.Clear();
        AvailableQuests.Clear();
        CompletedQuests.Clear();
        SelectedQuest = null;
        LastResultMessage = null;
        NotifyCounts();
    }

    private void Rebucket(QuestCardViewModel card)
    {
        ActiveQuests.Remove(card);
        AvailableQuests.Remove(card);
        CompletedQuests.Remove(card);

        switch (card.State)
        {
            case QuestStates.Active:
                ActiveQuests.Add(card);
                break;
            case QuestStates.Completed:
                CompletedQuests.Add(card);
                break;
            default:
                AvailableQuests.Add(card);
                break;
        }
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(HasAnyQuest));
        OnPropertyChanged(nameof(HasActiveQuests));
        OnPropertyChanged(nameof(HasCompletedQuests));
        OnPropertyChanged(nameof(HasSelectedQuest));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
