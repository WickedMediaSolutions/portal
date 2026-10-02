using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Bindable view-model for the WHO utility panel.
/// </summary>
/// <remarks>
/// Every field is one the server genuinely publishes. There is deliberately
/// no title column, because Keystone has no runtime title system to be
/// authoritative about — the client must not invent one.
/// </remarks>
public sealed class WhoViewModel : INotifyPropertyChanged
{
    private int _count;
    private bool _isOpen;
    private string _summary = string.Empty;

    /// <summary>One row per connected character, in server order.</summary>
    public ObservableCollection<WhoRecord> Rows { get; } = new();

    /// <summary>Whether the WHO popup is currently open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } }
    }

    /// <summary>Number of connected characters.</summary>
    public int Count
    {
        get => _count;
        private set { if (_count != value) { _count = value; OnPropertyChanged(); } }
    }

    /// <summary>Human-readable count line, or the empty-state text.</summary>
    public string Summary
    {
        get => _summary;
        private set { if (_summary != value) { _summary = value; OnPropertyChanged(); } }
    }

    /// <summary>True when at least one character is online.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Applies an authoritative <c>who.snapshot</c>.</summary>
    public void ApplyWhoSnapshot(WhoSnapshotPayload payload)
    {
        Rows.Clear();
        foreach (var row in payload.Rows ?? Array.Empty<WhoRecord>())
            Rows.Add(row);

        // Trust the row list over the reported count when they disagree, so
        // the header can never contradict the visible list.
        Count = Rows.Count;
        Summary = Count == 0
            ? "No characters are currently online."
            : $"{Count} character{(Count == 1 ? " is" : "s are")} online.";

        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>Resets the panel when the session ends.</summary>
    public void Clear()
    {
        Rows.Clear();
        Count = 0;
        Summary = string.Empty;
        OnPropertyChanged(nameof(HasRows));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
