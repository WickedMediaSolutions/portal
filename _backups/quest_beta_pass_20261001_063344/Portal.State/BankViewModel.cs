using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Bindable view-model for the bank panel.
/// </summary>
/// <remarks>
/// Keystone's bank is currency only — deposit and withdraw copper, with no
/// fees, interest, overdraft, transfers or item storage — so this panel
/// shows exactly two balances and two actions. There is deliberately no item
/// vault anywhere in the UI, because there is no server-side contract to
/// back one.
/// </remarks>
public sealed class BankViewModel : INotifyPropertyChanged
{
    private int _carriedCurrency;
    private int _bankCurrency;
    private string _amountText = string.Empty;
    private string _resultMessage = string.Empty;
    private bool _resultIsSuccess;
    private bool _isOpen;
    private bool _available;

    /// <summary>Whether the bank popup is currently open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Whether banking is permitted where the character stands. When false
    /// the server's refusal text is shown instead of the controls.
    /// </summary>
    public bool Available
    {
        get => _available;
        private set { if (_available != value) { _available = value; OnPropertyChanged(); } }
    }

    /// <summary>Authoritative carried copper.</summary>
    public int CarriedCurrency
    {
        get => _carriedCurrency;
        private set { if (_carriedCurrency != value) { _carriedCurrency = value; OnPropertyChanged(); } }
    }

    /// <summary>Authoritative banked copper.</summary>
    public int BankCurrency
    {
        get => _bankCurrency;
        private set { if (_bankCurrency != value) { _bankCurrency = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The amount the player typed. Parsing happens in the UI layer; the
    /// server validates the real amount regardless of what is typed here.
    /// </summary>
    public string AmountText
    {
        get => _amountText;
        set { if (_amountText != value) { _amountText = value; OnPropertyChanged(); } }
    }

    /// <summary>True when a bank result is waiting to be shown.</summary>
    public bool HasResult => !string.IsNullOrEmpty(ResultMessage);

    /// <summary>Latest bank outcome text, verbatim from the server.</summary>
    public string ResultMessage
    {
        get => _resultMessage;
        private set
        {
            if (_resultMessage != value)
            {
                _resultMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    /// <summary>Whether the latest operation succeeded.</summary>
    public bool ResultIsSuccess
    {
        get => _resultIsSuccess;
        private set { if (_resultIsSuccess != value) { _resultIsSuccess = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The result text prefixed with an explicit "OK:" / "Failed:" marker so
    /// the outcome is legible without relying on colour.
    /// </summary>
    public string ResultDisplay =>
        string.IsNullOrEmpty(ResultMessage)
            ? string.Empty
            : (ResultIsSuccess ? "OK: " : "Failed: ") + ResultMessage;

    /// <summary>Applies an authoritative <c>bank.snapshot</c>.</summary>
    public void ApplyBankSnapshot(BankSnapshotPayload payload)
    {
        Available = payload.Available;
        CarriedCurrency = payload.CarriedCurrency;
        BankCurrency = payload.BankCurrency;
    }

    /// <summary>Applies an authoritative <c>bank.result</c>.</summary>
    public void ApplyBankResult(BankResultPayload payload)
    {
        ResultIsSuccess = payload.Success;
        ResultMessage = payload.Message ?? string.Empty;

        // A successful operation reports the resulting bank balance
        // directly, so the panel is correct even before the follow-up
        // snapshot lands.
        if (payload.Success && payload.NewBankCurrency.HasValue)
            BankCurrency = payload.NewBankCurrency.Value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
