using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Portal.Networking;
using Portal.Protocol;
using Portal.Services;

namespace Portal.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Amount of the title bar that must remain inside the current virtual
    // desktop for a saved window position to be considered usable.
    private const double TitleBarProbeHeight = 30;
    private const double MinimumTitleBarVisibleWidth = 120;

    private readonly MainViewModel _viewModel = new();

    // Debounces autosave of the username convenience field so typing does not
    // write the settings file on every keystroke.
    private readonly DispatcherTimer _settingsSaveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    // ─── Networking / Service dependencies ────────────────────────────
    private readonly IWebSocketConnection _connection;
    private readonly AuthenticationService _authService;
    private readonly GameEventService _eventService;
    private readonly CombatService _combatService;
    private readonly MovementService _movementService;
    private readonly EquipmentService _equipmentService;
    private readonly CharacterPointsService _characterPointsService;
    private CancellationTokenSource? _sessionCts;

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();

        // Size the initial window to the usable desktop work area (which
        // excludes the taskbar) so the title bar and window controls are never
        // positioned off-screen on smaller displays. The window uses native
        // chrome, so Minimize / Maximize-Restore / Close and resizing are
        // provided by Windows and already respect the work area when maximized.
        var workArea = SystemParameters.WorkArea;

        // Never allow the minimum dimensions to exceed the work area, which
        // would otherwise force the window (and its title bar) off-screen.
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);

        // Clamp the initial size to fit within the work area.
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);

        // ─── Restore locally autosaved client preferences ─────────────────
        // Window placement/state and the login bar's username convenience
        // value are the only locally owned values Portal persists; they are
        // restored after the work-area clamping above so a stale saved size
        // can never reintroduce an off-screen window.
        RestoreLocalSettings(workArea);

        // ─── Wire networking services ────────────────────────────────────
        var connectionOptions = new WebSocketConnectionOptions();
        _connection = new WebSocketConnection(connectionOptions);

        var authOptions = new AuthenticationServiceOptions(
            new Uri("ws://localhost:4013"),
            "Portal",
            "1.0.0",
            new CapabilityInfo[]
            {
                new CapabilityInfo("combat.skill", "1.0"),
                new("combat.attack", "1.0"),
                new("target.select", "1.0"),
                new("character.skills.snapshot", "1.0"),
                new("movement.direction", "1.0"),
                new("movement.failed", "1.0"),
            });

        _authService = new AuthenticationService(
            _connection,
            authOptions);

        _eventService = new GameEventService(
            _connection,
            _viewModel.Character,
            _viewModel.Target,
            action =>
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Normal,
                    action),
            entities =>
                _viewModel.ReplaceRoomEntities(entities),
            skills =>
                _viewModel.ReplaceSkills(skills),
            message =>
                _viewModel.MovementMessage = message,
            (items, currency) =>
                _viewModel.ReplaceInventory(items, currency),
            equipped =>
                _viewModel.ReplaceEquipment(equipped));

        _combatService = new CombatService(_connection);
        _movementService = new MovementService(_connection);
        _equipmentService = new EquipmentService(_connection);
        _characterPointsService = new CharacterPointsService(_connection);

        _authService.StateChanged += OnAuthenticationStateChanged;

        // ─── Local settings autosave wiring ──────────────────────────────
        // There is no manual Save action anywhere in Portal: the username
        // convenience field is autosaved shortly after it changes, and the
        // window-related values are persisted when the window closes.
        UsernameEntry.TextChanged += UsernameEntry_TextChanged;
        _settingsSaveTimer.Tick += OnSettingsSaveTimerTick;
        Closing += OnMainWindowClosing;
        Closed += OnMainWindowClosed;
    }

    // ─── Authentication trigger ───────────────────────────────────────

    /// <summary>
    /// Handles the login bar's Login button click.
    ///
    /// This is the single entry point into the Portal connection and
    /// authentication flow: no WebSocket connection is opened and no
    /// authentication request is sent until the user has entered credentials
    /// and submitted them here.
    ///
    /// Portal never creates an account and never creates or selects a
    /// character.  Keystone authenticates the account, resolves that
    /// account's existing website-created character, binds it to the Portal
    /// session, and pushes the initial gameplay snapshots.
    /// </summary>
    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_authService.State == AuthenticationState.SessionActive)
        {
            _viewModel.ConnectionStatus = "Already connected";
            return;
        }

        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Password ?? string.Empty;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            _viewModel.ConnectionStatus = "Username and password are required";
            return;
        }

        LoginButton.IsEnabled = false;
        _viewModel.ConnectionStatus = "Connecting...";

        try
        {
            await ConnectAndAuthenticateAsync(username, password);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Initiates the full Portal authentication flow and, on success,
    /// automatically starts the background event receive loop.
    ///
    /// On failure the connection is left non-authenticated (no SessionActive
    /// state) so the event loop is never started, and the short authoritative
    /// failure message is surfaced in the login status text.
    /// </summary>
    public async Task ConnectAndAuthenticateAsync(
        string username, string password)
    {
        var result = await _authService.AuthenticateAsync(username, password);

        if (result.IsFailure)
        {
            // AuthenticationService has already returned to a non-SessionActive
            // state — no session, no gameplay snapshots, no event loop.
            _viewModel.ConnectionStatus = result.Errors.Count > 0
                ? result.Errors[0].Message
                : "Login failed";
            return;
        }

        _viewModel.SessionId = result.Value.SessionId;
        _viewModel.ConnectionStatus = "Connected";
        PasswordEntry.Clear();

        // A real ROP session now exists, so logout becomes available.
        LogoutButton.IsEnabled = true;
    }

    // ─── Auth state change → start event loop ─────────────────────────

    private void OnAuthenticationStateChanged(
        object? sender, AuthenticationStateChangedEventArgs e)
    {
        // Keep the bottom status bar's session text in sync with the real
        // authentication state, independent of the transient login-bar
        // messages carried by ConnectionStatus.
        _viewModel.SessionStatus = GetSessionStatusText(e.NewState);

        if (e.NewState == AuthenticationState.SessionActive)
        {
            // Authentication has fully completed and consumed its responses.
            // Now start the single background event receive loop.
            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();

            _eventService.Start(_sessionCts.Token);
        }
        else if (e.OldState == AuthenticationState.SessionActive
                 && e.NewState != AuthenticationState.SessionActive)
        {
            // Session is no longer active — stop event loop.
            _ = StopEventLoopAsync();
        }
    }

    // ─── Logout ───────────────────────────────────────────────────────

    /// <summary>
    /// Handles the login bar's Logout button click.
    ///
    /// Delegates to the single existing <see cref="LogoutAsync"/> path and
    /// restores the button's enabled state from the real authentication
    /// state afterwards.
    /// </summary>
    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        // Defensive guard: there is nothing to log out of when no ROP
        // session is active.
        if (_authService.State != AuthenticationState.SessionActive)
            return;

        LogoutButton.IsEnabled = false;

        try
        {
            await LogoutAsync();
        }
        finally
        {
            // Only a session that is still active (i.e. a failed logout)
            // keeps the button available.
            LogoutButton.IsEnabled = _authService.State == AuthenticationState.SessionActive;
        }
    }

    /// <summary>
    /// Logs out of the current ROP session, stops the event loop, and
    /// disconnects the WebSocket.
    ///
    /// Uses existing mechanisms only: the session cancellation token plus
    /// <see cref="GameEventService.StopAsync"/> stop the single receive
    /// loop, and <see cref="AuthenticationService.LogoutAsync"/> sends the
    /// logout request, closes the connection, and returns the authentication
    /// state to Unauthenticated.  No additional connection is created, so a
    /// later login reuses the same services.
    /// </summary>
    public async Task LogoutAsync()
    {
        await StopEventLoopAsync();

        var logoutResult = await _authService.LogoutAsync();

        // The password is never retained across a logout attempt, so a new
        // login always starts from a cleared field.
        PasswordEntry.Clear();

        if (logoutResult.IsSuccess)
        {
            // Session is gone: clear the session id (bottom bar shows
            // "Session: --") and return the login/status area to Disconnected.
            _viewModel.SessionId = "--";
            _viewModel.ConnectionStatus = "Disconnected";
        }
        else
        {
            // The session is still active — report the reason in the login
            // bar and leave the connection state untouched.
            _viewModel.ConnectionStatus = logoutResult.Errors.Count > 0
                ? logoutResult.Errors[0].Message
                : "Logout failed";
        }
    }

    /// <summary>
    /// Maps the authoritative authentication state onto the short session
    /// text shown by the bottom status bar.  Any state that does not
    /// represent an established session reads as Disconnected.
    /// </summary>
    private static string GetSessionStatusText(AuthenticationState state)
    {
        return state switch
        {
            AuthenticationState.Authenticating => "Connecting...",
            AuthenticationState.Authenticated => "Authenticating...",
            AuthenticationState.EstablishingSession => "Authenticating...",
            AuthenticationState.SessionActive => "Connected",
            AuthenticationState.Reconnecting => "Reconnecting...",
            _ => "Disconnected",
        };
    }

    private async Task StopEventLoopAsync()
    {
        _sessionCts?.Cancel();
        await _eventService.StopAsync();
    }

    /// ─── Attack button ───────────────────────────────────────────

    /// <summary>
    /// Sends a combat.attack.request to Keystone when a valid target is
    /// selected.  If no target is selected the button click is silently
    /// ignored — no invalid request is sent.
    /// </summary>
    private async void AttackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Target.HasTarget || string.IsNullOrEmpty(_viewModel.Target.TargetId))
            return;

        var result = await _combatService.SendAttackAsync(_viewModel.Target.TargetId);
        // Fire-and-forget.  Resulting combat state arrives through the
        // existing GameEventService event stream.
    }

    /// <summary>
    /// Sends a combat.skill.request to Keystone when the player clicks an
    /// active unlocked skill button in the Spells and Abilities panel.
    /// Guards passive skills, empty SkillId, and missing required target.
    /// </summary>
    private async void SkillButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.Tag is not SkillRecord skill)
            return;

        if (skill.IsPassive || string.IsNullOrEmpty(skill.SkillId))
            return;

        if (skill.TargetRequired)
        {
            if (!_viewModel.Target.HasTarget || string.IsNullOrEmpty(_viewModel.Target.TargetId))
                return;
        }

        var targetId = skill.TargetRequired ? _viewModel.Target.TargetId : null;
        _ = await _combatService.SendSkillAsync(skill.SkillId, targetId);
        // Fire-and-forget.  Resulting combat state arrives through the
        // existing GameEventService event stream.
    }

    // ─── Room Entities ComboBox selection ─────────────────────────

    /// <summary>
    /// Sends a target.select.request to Keystone when the player selects
    /// a live RoomEntity from the ComboBox.
    ///
    /// Does NOT update TargetStats locally — the authoritative target state
    /// arrives via the existing target.changed event stream.
    /// </summary>
    private async void RoomEntitiesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = _viewModel.SelectedRoomEntity;

        // Defensive guard: no entity selected or dead entity
        if (selected is null || selected.IsDead)
            return;

        // Fire-and-forget send.  Resulting target state arrives through the
        // existing GameEventService event stream as a target.changed event.
        _ = await _combatService.SendTargetSelectAsync(selected.TargetId);
    }

    // --- Movement direction controls -------------------------------------

    /// <summary>
    /// Sends a movement.direction.request to Keystone when the player clicks
    /// a directional control in the Navigation panel.
    ///
    /// The canonical direction is carried on the button's Tag; no movement
    /// logic lives here.  Keystone resolves the real exit and authoritative
    /// traversal; resulting state returns via the existing event stream.
    /// </summary>
    private async void DirectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.Tag is not string direction || string.IsNullOrWhiteSpace(direction))
            return;

        // Clear any previous movement failure message before sending the
        // new request. If the move succeeds the message stays cleared;
        // if the move fails, movement.failed will populate it again.
        _viewModel.MovementMessage = string.Empty;

        _ = await _movementService.SendDirectionAsync(direction);
        // Fire-and-forget.  Resulting room/target state arrives through the
        // existing GameEventService event stream.
    }

    // ─── Local settings: restore / autosave ───────────────────────────

    /// <summary>
    /// Applies the locally saved client preferences at startup: the window's
    /// normal size/position, its maximized state, and the login bar's username
    /// convenience value.
    ///
    /// Only values Portal itself owns are restored.  Every piece of
    /// authoritative gameplay state (HP, XP, level, inventory, equipment,
    /// current target, skills, currency, room state) keeps coming from
    /// Keystone, and the password box is deliberately left empty because no
    /// password is ever stored locally.
    /// </summary>
    private void RestoreLocalSettings(Rect workArea)
    {
        var settings = PortalSettings.Current;

        // ─── Size ───
        // A saved size below the minimum is ignored rather than clamped, so a
        // damaged settings file cannot produce an unusable window.
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = Math.Min(settings.WindowWidth, workArea.Width);
            Height = Math.Min(settings.WindowHeight, workArea.Height);
        }

        // ─── Position ───
        // Only restore a position that still leaves part of the title bar on
        // the current virtual desktop, because monitor layouts can change
        // between sessions.  Otherwise the XAML default (CenterScreen) stays.
        if (settings.WindowLeft is double savedLeft &&
            settings.WindowTop is double savedTop)
        {
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            var visibleTitleBarWidth = Math.Min(savedLeft + Width, virtualScreen.Right)
                                       - Math.Max(savedLeft, virtualScreen.Left);
            var visibleTitleBarHeight = Math.Min(savedTop + TitleBarProbeHeight, virtualScreen.Bottom)
                                        - Math.Max(savedTop, virtualScreen.Top);

            if (visibleTitleBarWidth >= MinimumTitleBarVisibleWidth &&
                visibleTitleBarHeight >= TitleBarProbeHeight)
            {
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                Left = savedLeft;
                Top = savedTop;
            }
        }

        // ─── Maximized state ───
        if (settings.WindowMaximized)
        {
            WindowState = System.Windows.WindowState.Maximized;
        }

        // ─── Username convenience value ───
        // Set after InitializeComponent so the login bar starts with the last
        // used account name.  The password field is never populated.
        UsernameEntry.Text = settings.Username;
    }

    /// <summary>
    /// Persists the locally owned window preferences and the username
    /// convenience value.  Called automatically when the window closes; no
    /// gameplay state is written, so nothing here can conflict with Keystone.
    /// </summary>
    private void PersistLocalSettings()
    {
        var settings = PortalSettings.Current;

        // RestoreBounds holds the normal-state rectangle, so a maximized
        // window still remembers where and at what size it was un-maximized.
        var normalBounds = WindowState == System.Windows.WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        if (!normalBounds.IsEmpty &&
            normalBounds.Width >= MinWidth &&
            normalBounds.Height >= MinHeight)
        {
            settings.WindowLeft = normalBounds.Left;
            settings.WindowTop = normalBounds.Top;
            settings.WindowWidth = normalBounds.Width;
            settings.WindowHeight = normalBounds.Height;
        }

        settings.WindowMaximized = WindowState == System.Windows.WindowState.Maximized;

        // Username only — the password is never stored in any form.
        settings.Username = UsernameEntry.Text?.Trim() ?? string.Empty;

        settings.Save();
    }

    /// <summary>
    /// Marks the username convenience field as changed and (re)starts the
    /// short debounce that autosaves it.  There is no manual save action.
    /// </summary>
    private void UsernameEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    /// <summary>
    /// Autosaves the username convenience field once typing has stopped for
    /// the debounce interval.  Writes only when the stored value differs.
    /// </summary>
    private void OnSettingsSaveTimerTick(object? sender, EventArgs e)
    {
        _settingsSaveTimer.Stop();

        var settings = PortalSettings.Current;
        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        if (settings.Username == username)
        {
            return;
        }

        settings.Username = username;
        settings.Save();
    }

    // ─── Window close cleanup ─────────────────────────────────────────

    /// <summary>
    /// Persists the locally owned settings one final time, while the window
    /// still reports a valid state and size.
    /// </summary>
    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        Closing -= OnMainWindowClosing;
        _settingsSaveTimer.Stop();

        PersistLocalSettings();
    }

    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        Closed -= OnMainWindowClosed;
        _authService.StateChanged -= OnAuthenticationStateChanged;

        await StopEventLoopAsync();

        try { _sessionCts?.Dispose(); } catch { /* Best-effort */ }
        _sessionCts = null;

        await _connection.DisposeAsync();
    }

    // ─── Equipment / Inventory item inspection ──────────────────────

    /// <summary>
    /// Handles clicks on occupied equipment silhouette layer images.
    /// Resolves the clicked slot to an EquippedItemRecord, opens the item
    /// information card, and sends a real equipment.unequip.request for
    /// that slot to Keystone (fire-and-forget).
    ///
    /// No local optimistic state is applied — Keystone resolves the slot
    /// authoritatively and the refreshed inventory.snapshot /
    /// equipment.snapshot events update the UI.
    /// </summary>
    private async void EquipmentImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Image image)
            return;

        if (image.Tag is not string slot || string.IsNullOrEmpty(slot))
            return;

        var equipped = _viewModel.EquippedItems
            .FirstOrDefault(eq => eq.Slot == slot);

        // Only an occupied slot can be unequipped.
        if (equipped is null)
            return;

        ShowItemCard(equipped);

        _ = await _equipmentService.SendUnequipAsync(slot);
        // Fire-and-forget.  Refreshed inventory/equipment state arrives
        // through the existing GameEventService event stream.
    }

    /// <summary>
    /// Handles clicks on inventory item entries.
    /// Opens the item information card for the clicked item, and for a
    /// carried item that declares an equipment slot, sends a real
    /// equipment.equip.request to Keystone (fire-and-forget).
    ///
    /// No local optimistic state is applied — Keystone resolves the slot
    /// authoritatively from the item definition and the refreshed
    /// inventory.snapshot / equipment.snapshot events update the UI.
    /// </summary>
    private async void InventoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border border)
            return;

        if (border.Tag is not InventoryItemRecord item)
            return;

        ShowItemCard(item);

        // Only items that declare an equipment slot are equippable.
        // Keystone remains fully authoritative: it re-resolves the slot
        // from the item definition and runs its existing equip validation.
        if (string.IsNullOrWhiteSpace(item.Slot))
            return;

        _ = await _equipmentService.SendEquipAsync(item.ItemId);
        // Fire-and-forget.  Refreshed inventory/equipment state arrives
        // through the existing GameEventService event stream.
    }

    /// <summary>
    /// Opens the item information card popup with the metadata from an
    /// inventory or equipped item record.
    /// </summary>
    private void ShowItemCard(InventoryItemRecord item)
    {
        _viewModel.SelectedCardItem = new EquipmentCardItem
        {
            Name = item.Name,
            Slot = item.Slot,
            Category = item.Category,
            Description = item.Description,
            DamageType = item.DamageType,
            BaseDamage = item.BaseDamage,
            DamageMin = item.DamageMin,
            DamageMax = item.DamageMax,
            ArmorClass = item.ArmorClass,
            Level = item.Level,
            Strength = item.Strength,
            Speed = item.Speed,
            Encumbrance = item.Encumbrance,
        };

        ItemCardPopup.IsOpen = true;
    }

    /// <summary>
    /// Opens the item information card popup with the metadata from an
    /// equipped item record.
    /// </summary>
    private void ShowItemCard(EquippedItemRecord item)
    {
        _viewModel.SelectedCardItem = new EquipmentCardItem
        {
            Name = item.Name,
            Slot = item.Slot,
            Category = item.Category,
            Description = item.Description,
            DamageType = item.DamageType,
            BaseDamage = item.BaseDamage,
            DamageMin = item.DamageMin,
            DamageMax = item.DamageMax,
            ArmorClass = item.ArmorClass,
            Level = item.Level,
            Strength = item.Strength,
            Speed = item.Speed,
            Encumbrance = item.Encumbrance,
        };

        ItemCardPopup.IsOpen = true;
    }

    /// <summary>
    /// Cleans up SelectedCardItem state when the popup is dismissed (e.g.
    /// by clicking outside with StaysOpen=False).
    /// </summary>
    private void ItemCardPopup_Closed(object? sender, EventArgs e)
    {
        _viewModel.SelectedCardItem = null;
    }

    /// <summary>
    /// Dismisses the item information card when the card background is clicked.
    /// </summary>
    private void ItemCardBackground_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ItemCardPopup.IsOpen = false;
        _viewModel.SelectedCardItem = null;
    }

    // ── Character Point allocation dialog ──────────────────────────────────
    //
    // The dialog edits only UNSAVED choices. A committed allocation happens
    // solely through Apply -> Keystone -> character.points.snapshot, so the
    // character panel, resource displays, and remaining-point count all
    // refresh from authoritative server state rather than local optimism.

    /// <summary>
    /// Opens the allocation dialog, seeded from the current authoritative
    /// Character Point state.
    /// </summary>
    private void AllocateButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenStatAllocation();
        AllocationOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Stages one more point for a stat.  Stays entirely inside the dialog —
    /// nothing is sent to the server until Apply.
    /// </summary>
    private void AllocationPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StatAllocationRowViewModel row })
            _viewModel.StatAllocation?.Plus(row);
    }

    /// <summary>
    /// Removes one staged point.  Only the current unsaved allocation can be
    /// reduced — an already-earned permanent stat is never touched.
    /// </summary>
    private void AllocationMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StatAllocationRowViewModel row })
            _viewModel.StatAllocation?.Minus(row);
    }

    /// <summary>
    /// Clears the dialog's unsaved choices.  Refunds nothing that was already
    /// committed to Keystone.
    /// </summary>
    private void AllocationReset_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.StatAllocation?.Reset();
    }

    /// <summary>Closes the dialog, discarding any unsaved choices.</summary>
    private void AllocationCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseAllocationDialog();
    }

    /// <summary>
    /// Sends the COMPLETE proposed allocation to Keystone as one request.
    ///
    /// Keystone validates it atomically. The dialog is closed optimistically
    /// and the authoritative outcome arrives as a character.points.snapshot:
    /// on success that snapshot carries the new balance and stat values; on
    /// failure it carries Keystone's error message plus the unchanged state,
    /// which is what the character panel then displays. Nothing is partially
    /// applied locally either way.
    /// </summary>
    private async void AllocationApply_Click(object sender, RoutedEventArgs e)
    {
        var allocation = _viewModel.StatAllocation;
        if (allocation is null) return;

        var entries = allocation.BuildAllocation();
        if (entries.Count == 0)
        {
            CloseAllocationDialog();
            return;
        }

        CloseAllocationDialog();

        // Fire-and-forget: the result returns through the existing event
        // stream as a character.points.snapshot.
        _ = await _characterPointsService.SendAllocationAsync(entries);
    }

    private void CloseAllocationDialog()
    {
        AllocationOverlay.Visibility = Visibility.Collapsed;
        _viewModel.CloseStatAllocation();
    }
}