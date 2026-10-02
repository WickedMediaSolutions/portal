using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// Bindable view-model for the shop panel: which shops the current room
/// offers, the wares of the selected shop, and the latest transaction result.
/// </summary>
/// <remarks>
/// Portal never calculates a price here. Every price, stock level and
/// outcome is rendered exactly as the server reported it, so the graphical
/// client and the text client always agree about what a trade costs.
/// </remarks>
public sealed class ShopViewModel : INotifyPropertyChanged
{
    private ShopSummaryRecord? _selectedShop;
    private string _resultMessage = string.Empty;
    private bool _resultIsSuccess;
    private int _currency;
    private bool _isOpen;

    /// <summary>Shops linked to the character's current room.</summary>
    public ObservableCollection<ShopSummaryRecord> Shops { get; } = new();

    /// <summary>Wares offered by the selected shop.</summary>
    public ObservableCollection<ShopWareRecord> Wares { get; } = new();

    /// <summary>Whether the shop popup is currently open.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// The shop whose wares are shown, or null before one is chosen.
    /// </summary>
    public ShopSummaryRecord? SelectedShop
    {
        get => _selectedShop;
        set
        {
            if (!ReferenceEquals(_selectedShop, value))
            {
                _selectedShop = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedShopName));
                OnPropertyChanged(nameof(HasSelectedShop));
            }
        }
    }

    /// <summary>Display name of the selected shop, for the panel header.</summary>
    public string SelectedShopName => SelectedShop?.Name ?? string.Empty;

    /// <summary>True once a shop has been chosen and its wares loaded.</summary>
    public bool HasSelectedShop => SelectedShop is not null;

    /// <summary>True when the selected shop has at least one line of wares.</summary>
    public bool HasWares => Wares.Count > 0;

    /// <summary>
    /// The player's carried currency in copper, mirrored from the
    /// authoritative inventory snapshot so the panel can show what the
    /// player holds. Display only — the server decides affordability.
    /// </summary>
    public int Currency
    {
        get => _currency;
        set { if (_currency != value) { _currency = value; OnPropertyChanged(); } }
    }

    /// <summary>True when a transaction result is waiting to be shown.</summary>
    public bool HasResult => !string.IsNullOrEmpty(ResultMessage);

    /// <summary>Latest transaction outcome text, verbatim from the server.</summary>
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

    /// <summary>Whether the latest transaction succeeded.</summary>
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

    /// <summary>
    /// Applies an authoritative <c>shop.snapshot</c>.
    /// </summary>
    /// <remarks>
    /// When the payload advertises a specific shop, its wares replace the
    /// previous shop's. When it only carries the room's shop list, the wares
    /// are cleared rather than left stale, so the panel can never show one
    /// shop's goods under another shop's name.
    /// </remarks>
    public void ApplyShopSnapshot(ShopSnapshotPayload payload)
    {
        Shops.Clear();
        foreach (var shop in payload.Shops ?? Array.Empty<ShopSummaryRecord>())
            Shops.Add(shop);

        var detail = payload.Shop;

        Wares.Clear();
        if (detail is null)
        {
            // Only the shop list arrived: no shop is open, so drop the wares.
            SelectedShop = null;
        }
        else
        {
            foreach (var ware in detail.Wares ?? Array.Empty<ShopWareRecord>())
                Wares.Add(ware);

            // Reconcile the selection with the server's own naming, so the
            // header can never disagree with the wares on screen.
            SelectedShop = Shops.FirstOrDefault(s => s.ShopId == detail.ShopId)
                ?? new ShopSummaryRecord(detail.ShopId, detail.Name);
        }

        OnPropertyChanged(nameof(HasWares));
    }

    /// <summary>Applies an authoritative <c>shop.result</c>.</summary>
    public void ApplyShopResult(ShopResultPayload payload)
    {
        ResultIsSuccess = payload.Success;
        ResultMessage = payload.Message ?? string.Empty;
    }

    /// <summary>
    /// Clears the panel when the character leaves a shop room, so no stale
    /// shop or wares linger after moving away.
    /// </summary>
    public void ClearForRoomChange()
    {
        Shops.Clear();
        Wares.Clear();
        SelectedShop = null;
        ResultMessage = string.Empty;
        ResultIsSuccess = false;
        OnPropertyChanged(nameof(HasWares));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
