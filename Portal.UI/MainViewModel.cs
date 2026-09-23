using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.State;

namespace Portal.UI;

/// <summary>
/// Top-level view-model for the MainWindow, exposing bindable state
/// for all panels.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private CharacterStats _character = new();
    private string _connectionStatus = "Disconnected";
    private string _latency = "-- ms";
    private string _sessionId = "--";

    public CharacterStats Character
    {
        get => _character;
        set { if (_character != value) { _character = value; OnPropertyChanged(); } }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { if (_connectionStatus != value) { _connectionStatus = value; OnPropertyChanged(); } }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}