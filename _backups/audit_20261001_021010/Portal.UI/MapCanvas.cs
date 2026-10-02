using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Portal.Protocol;
using Portal.State;

namespace Portal.UI;

/// <summary>
/// A high-performance, immediate-mode WPF renderer for the character's discovered map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a custom control and not a Canvas of shapes.</b> The realm holds 7943
/// rooms and admin/full-reveal testing renders all of them. One WPF element per
/// room would mean thousands of visual trees, layout passes and hit-test
/// entries, which does not stay responsive. This control draws everything in a
/// single <see cref="OnRender"/> pass into ONE element, so cost is proportional
/// to what is actually on screen rather than to the size of the world.
/// </para>
/// <para>
/// <b>What is drawn.</b> Discovered rooms as compact nodes at their real x/y,
/// discovered edges as lines, Up/Down edges as vertical chevrons (never as a
/// misleading 2-D line), doors as a distinct doubled stroke, the player's room
/// as an unmistakable ringed marker, and unexplored exits as a fogged stub
/// carrying a "?" and nothing else.
/// </para>
/// <para>
/// <b>Fog of war.</b> Only rooms present in <see cref="MapState.Rooms"/> are
/// drawn, so an undiscovered room cannot appear. Frontier entries have no
/// destination in the payload, so this control never invents one: it draws a
/// stub pointing away from the source room and stops.
/// </para>
/// </remarks>
public sealed class MapCanvas : FrameworkElement
{
    // â”€â”€ Palette â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Dark-fantasy, high contrast, matching the existing Portal panel colours.
    private static readonly Color BackgroundColor = Color.FromRgb(0x0A, 0x0A, 0x0A);
    private static readonly Color GridColor = Color.FromRgb(0x1C, 0x1C, 0x1C);
    private static readonly Color EdgeColor = Color.FromRgb(0x6E, 0x62, 0x4C);
    private static readonly Color DoorColor = Color.FromRgb(0xC4, 0xA9, 0x6A);
    private static readonly Color RoomFillColor = Color.FromRgb(0x3A, 0x42, 0x38);
    private static readonly Color RoomStrokeColor = Color.FromRgb(0x9A, 0xB0, 0x92);
    private static readonly Color LandmarkerFillColor = Color.FromRgb(0x4A, 0x40, 0x28);
    private static readonly Color LandmarkerStrokeColor = Color.FromRgb(0xC4, 0xA9, 0x6A);
    private static readonly Color CurrentRoomColor = Color.FromRgb(0x7C, 0xE0, 0x5A);
    private static readonly Color SelectedColor = Color.FromRgb(0xFF, 0xD9, 0x66);
    // Frontier is drawn bright enough to clear WCAG AA against the #0A0A0A map
    // background, because an unexplored exit is critical navigation information.
    private static readonly Color FrontierColor = Color.FromRgb(0xB4, 0xB4, 0xB4);
    private static readonly Color VerticalColor = Color.FromRgb(0x7A, 0xB8, 0xD8);

    private static readonly Brush BackgroundBrush = Frozen(BackgroundColor);
    private static readonly Pen GridPen = FrozenPen(GridColor, 1.0);
    private static readonly Pen EdgePen = FrozenPen(EdgeColor, 1.6);
    private static readonly Pen DoorPen = FrozenPen(DoorColor, 2.6);
    private static readonly Pen RoomPen = FrozenPen(RoomStrokeColor, 1.2);
    private static readonly Pen LandmarkerPen = FrozenPen(LandmarkerStrokeColor, 1.6);
    private static readonly Pen CurrentPen = FrozenPen(CurrentRoomColor, 2.4);
    private static readonly Pen SelectedPen = FrozenPen(SelectedColor, 2.6);
    private static readonly Pen FrontierPen = FrozenPen(FrontierColor, 1.4, dashStyle: new DashStyle(
        new double[] { 2.0, 2.0 }, 0));
    private static readonly Pen VerticalPen = FrozenPen(VerticalColor, 1.8);

    private static readonly Brush RoomFillBrush = Frozen(RoomFillColor);
    private static readonly Brush LandmarkerFillBrush = Frozen(LandmarkerFillColor);
    private static readonly Brush CurrentBrush = Frozen(CurrentRoomColor);
    private static readonly Brush SelectedBrush = Frozen(SelectedColor);
    private static readonly Brush FrontierBrush = Frozen(FrontierColor);
    private static readonly Brush VerticalBrush = Frozen(VerticalColor);
    private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xE8, 0xE8, 0xE8));

    // â”€â”€ Dependency properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>The map view-model to render. Assigned once by the host panel.</summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(MapViewModel),
        typeof(MapCanvas),
        new FrameworkPropertyMetadata(null, OnViewModelChanged));

    /// <summary>The map view-model this canvas renders.</summary>
    public MapViewModel? ViewModel
    {
        get => (MapViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Raised when the user clicks a discovered room.</summary>
    public event EventHandler<string>? RoomClicked;

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var canvas = (MapCanvas)d;
        var oldVm = e.OldValue as MapViewModel;
        if (oldVm is not null) oldVm.PropertyChanged -= canvas.OnViewModelPropertyChanged;

        var newVm = e.NewValue as MapViewModel;
        if (newVm is not null) newVm.PropertyChanged += canvas.OnViewModelPropertyChanged;

        canvas.InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The canvas redraws wholesale on any relevant change. Because drawing
        // is immediate-mode and cheap, this is far simpler (and far cheaper) than
        // trying to diff thousands of individual visual elements.
        switch (e.PropertyName)
        {
            case null:
            case "":
            case nameof(MapViewModel.State):
            case nameof(MapViewModel.Viewport):
            case nameof(MapViewModel.SelectedRoomId):
            case nameof(MapViewModel.AvailableAreas):
            case nameof(MapViewModel.AvailableFloors):
            case nameof(MapViewModel.SelectedArea):
            case nameof(MapViewModel.SelectedFloor):
                InvalidateVisual();
                break;
            default:
                break;
        }
    }

    /// <summary>Creates the control.</summary>
    public MapCanvas()
    {
        // The canvas draws everything itself, so WPF's own hit testing is
        // bypassed in favour of the explicit hit test implemented below.
        ClipToBounds = true;
        Focusable = true;
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    private MapState? State => ViewModel?.State;

    // â”€â”€ Input: wheel zoom, drag pan, click select, keyboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private bool _isPanning;
    private Point _panStartPoint;
    private bool _panMoved;

    /// <summary>Mouse wheel zooms toward the cursor.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        var vm = ViewModel;
        if (vm is null) return;

        var notches = Math.Sign(e.Delta);
        if (notches == 0) return;

        var position = e.GetPosition(this);
        // One notch is a gentle step, so the map stays controllable with a
        // standard wheel rather than requiring dozens of turns.
        vm.Viewport.ZoomAt(
            new MapPoint(position.X, position.Y),
            notches > 0 ? 1.15 : 1.0 / 1.15);

        e.Handled = true;
    }

    /// <summary>Left or middle button starts a pan drag.</summary>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);

        var vm = ViewModel;
        if (vm is null) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;

        Focus();
        CaptureMouse();
        _isPanning = true;
        _panMoved = false;
        _panStartPoint = e.GetPosition(this);

        e.Handled = true;
    }

    /// <summary>
    /// Applies drag movement to the viewport, or reports a click when the press
    /// never turned into a drag.
    /// </summary>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var vm = ViewModel;
        if (vm is null || !_isPanning) return;

        if (e.LeftButton != MouseButtonState.Pressed &&
            e.MiddleButton != MouseButtonState.Pressed)
        {
            StopPanning();
            return;
        }

        var current = e.GetPosition(this);
        var dx = current.X - _panStartPoint.X;
        var dy = current.Y - _panStartPoint.Y;

        // A couple of pixels of tolerance stops an accidental nudge from being
        // read as a click.
        if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _panMoved = true;

        // Measured from the drag origin every time, so the map tracks the cursor
        // exactly and cannot drift through accumulated rounding.
        vm.Viewport.PanBy(dx, dy);

        e.Handled = true;
    }

    /// <summary>Release ends the drag; a still press selects a room.</summary>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var vm = ViewModel;
        if (vm is null) return;

        if (!_panMoved && e.ChangedButton == MouseButton.Left)
        {
            if (HitTestRoom(e.GetPosition(this)) is { } roomId)
            {
                vm.SelectedRoomId = roomId;
                RoomClicked?.Invoke(this, roomId);
            }
        }

        StopPanning();
        e.Handled = true;
    }

    private void StopPanning()
    {
        _isPanning = false;
        if (CaptureMouse()) ReleaseMouseCapture();
    }

    /// <summary>Stops panning if the capture is lost unexpectedly.</summary>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isPanning = false;
    }

    /// <summary>
    /// Keyboard support: arrows pan, +/- zoom, Home centres on the player,
    /// F fits the view.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var vm = ViewModel;
        if (vm is null) return;

        const double panStep = 60.0;
        switch (e.Key)
        {
            case Key.Left: vm.Viewport.PanBy(panStep, 0); break;
            case Key.Right: vm.Viewport.PanBy(-panStep, 0); break;
            case Key.Up: vm.Viewport.PanBy(0, panStep); break;
            case Key.Down: vm.Viewport.PanBy(0, -panStep); break;
            case Key.Add:
            case Key.OemPlus: vm.Viewport.ZoomIn(); break;
            case Key.Subtract:
            case Key.OemMinus: vm.Viewport.ZoomOut(); break;
            case Key.Home: vm.CenterOnPlayer(); break;
            case Key.F: vm.FitToVisibleRooms(); break;
            default: return;
        }

        e.Handled = true;
    }

    // â”€â”€ Hit testing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Finds the discovered room under a screen point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hit testing runs in WORLD space against the filtered visible set, so a
    /// click can only ever select a room Keystone disclosed and that is
    /// currently shown. There is no path through this method by which an
    /// undiscovered room could be selected.
    /// </para>
    /// <para>
    /// The pick radius grows as the map zooms out so nodes stay comfortably
    /// clickable even when only a few pixels across, and it stays bounded so
    /// neighbouring rooms never steal the click.
    /// </para>
    /// </remarks>
    private string? HitTestRoom(Point screenPoint)
    {
        var vm = ViewModel;
        var state = State;
        if (vm is null || state is null) return null;

        var (worldX, worldY) = vm.Viewport.Unproject(new MapPoint(screenPoint.X, screenPoint.Y));

        var radius = Math.Clamp(22.0 / Math.Max(vm.Viewport.Scale, 0.001), 0.35, 3.0);
        var radiusSquared = radius * radius;

        string? bestId = null;
        var bestDistance = double.MaxValue;

        foreach (var room in state.VisibleRooms)
        {
            // Rooms on other floors are not drawn here, so they are not pickable.
            if (state.SelectedZ is int floor && room.Z != floor) continue;

            var dx = room.X - worldX;
            var dy = room.Y - worldY;
            var distance = (dx * dx) + (dy * dy);
            if (distance > radiusSquared) continue;

            // Deterministic tie-break by room id, so an exact overlap between two
            // rooms always resolves to the same one.
            if (distance < bestDistance ||
                (Math.Abs(distance - bestDistance) < 1e-9 &&
                 bestId is not null &&
                 string.CompareOrdinal(room.RoomId, bestId) < 0))
            {
                bestDistance = distance;
                bestId = room.RoomId;
            }
        }

        return bestId;
    }

    // â”€â”€ Rendering â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Draws the whole map in a single pass.
    /// </summary>
    /// <remarks>
    /// Draw order is deliberate: grid, then edges beneath the nodes, then
    /// nodes, then vertical chevrons and frontier stubs on top, then the
    /// player's marker last so it is never obscured.
    /// </remarks>
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        // Cache the DPI scale once per pass: glyph metrics need it, and a
        // visual-tree lookup per label would be wasteful.
        try { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
        catch (InvalidOperationException) { _pixelsPerDip = 1.0; }

        var vm = ViewModel;
        var state = State;

        var width = ActualWidth;
        var height = ActualHeight;

        dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));

        if (vm is null || state is null || state.IsEmpty || width <= 0 || height <= 0)
            return;

        vm.Viewport.SetViewportSize(width, height);

        var rooms = state.VisibleRooms;
        if (rooms.Count == 0) return;

        // The map always draws EXACTLY ONE floor. MapState.EffectiveFloor follows the
        // player's own floor when none is pinned, so rooms on other Z levels are
        // never silently overlaid onto this plane, which would misrepresent a
        // multi-storey area as a flat corridor.
        var drawable = state.VisibleRoomsOnFloor;
        if (drawable.Count == 0) return;

        DrawGrid(dc, vm, width, height);
        DrawEdges(dc, state, vm, drawable);
        DrawRooms(dc, state, vm, drawable);
        DrawCurrentRoomMarker(dc, state, vm);

        // Frontier is drawn LAST, on top of everything including the player's
        // marker. An unexplored exit is important information, so it must never
        // be hidden underneath a node or the player reticle.
        DrawFrontier(dc, state, vm, drawable);
    }

    /// <summary>
    /// Draws a faint reference grid so the player can perceive scale and pan
    /// distance. Purely decorative orientation, never data.
    /// </summary>
    private static void DrawGrid(DrawingContext dc, MapViewModel vm, double width, double height)
    {
        // Only show grid lines once cells are far enough apart to be useful,
        // otherwise the map turns into noise at low zoom.
        if (vm.Viewport.Scale < 16) return;

        var startX = vm.Viewport.OriginX;
        var startY = vm.Viewport.OriginY;
        var scale = vm.Viewport.Scale;

        var firstWorldX = (int)Math.Floor(-startX / scale);
        var lastWorldX = (int)Math.Ceiling((width - startX) / scale);
        var firstWorldY = (int)Math.Floor((startY - height) / scale);
        var lastWorldY = (int)Math.Ceiling(startY / scale);

        for (var wx = firstWorldX; wx <= lastWorldX; wx++)
        {
            var x = startX + wx * scale;
            dc.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
        }

        for (var wy = firstWorldY; wy <= lastWorldY; wy++)
        {
            var y = startY - wy * scale;
            dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));
        }
    }

    // â”€â”€ Edges â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Draws connections between two discovered rooms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only edges whose BOTH endpoints are visible rooms are drawn, so a
    /// connection never runs off to a room that is filtered out or unknown.
    /// </para>
    /// <para>
    /// Up/Down edges are deliberately NOT drawn as 2-D lines: a staircase is not
    /// a corridor across the map plane, and a line would be a lie. They become a
    /// vertical chevron badge on the source node instead.
    /// </para>
    /// <para>
    /// A door is shown by a thicker, doubled stroke AND a perpendicular tick at
    /// the midpoint, so it is distinguishable by shape as well as colour. No
    /// open/locked state is implied, because the payload does not carry one.
    /// </para>
    /// </remarks>
    private static void DrawEdges(
        DrawingContext dc, MapState state, MapViewModel vm, IReadOnlyList<MapRoomRecord> visible)
    {
        var visibleIds = new HashSet<string>(visible.Count, StringComparer.Ordinal);
        foreach (var room in visible) visibleIds.Add(room.RoomId);

        foreach (var edge in state.Edges)
        {
            if (!visibleIds.Contains(edge.From) || !visibleIds.Contains(edge.To))
                continue;

            var direction = edge.DirectionKind;

            // Vertical connections are represented by a chevron, not a line.
            if (MapDirections.IsVertical(direction)) continue;

            if (!state.TryGetRoom(edge.From, out var from) ||
                !state.TryGetRoom(edge.To, out var to))
            {
                continue;
            }

            var a = Wpf(vm.Viewport.Project(from));
            var b = Wpf(vm.Viewport.Project(to));

            if (!IsOnScreen(a, vm)) continue;

            if (edge.Door)
            {
                // Doubled stroke: two parallel lines read as a doorway even in
                // greyscale or for a colour-blind viewer.
                var offset = Normalize(b - a) * 2.0;
                dc.DrawLine(DoorPen, a + offset, b + offset);
                dc.DrawLine(DoorPen, a - offset, b - offset);

                // Perpendicular tick at the midpoint: the classic "door" glyph.
                var mid = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                var normal = Normalize(new Vector(-(b.Y - a.Y), b.X - a.X)) * 5.0;
                dc.DrawLine(DoorPen, mid - normal, mid + normal);
            }
            else
            {
                dc.DrawLine(EdgePen, a, b);
            }
        }
    }

    /// <summary>
    /// Draws a small "this room has a staircase / ladder" badge.
    /// </summary>
    /// <remarks>
    /// Shape and a letter both carry the meaning: a chevron for Up and a
    /// down-chevron for Down, each labelled U or D. This stays readable without
    /// relying on colour, and it is drawn only for rooms whose vertical
    /// connection was actually reported by Keystone.
    /// </remarks>
    private static void DrawVerticalBadges(
        DrawingContext dc, MapState state, MapViewModel vm, MapRoomRecord room)
    {
        var edges = state.GetOutgoingEdges(room.RoomId);
        if (edges.Count == 0) return;

        var hasUp = edges.Any(e => e.DirectionKind == MapDirection.Up);
        var hasDown = edges.Any(e => e.DirectionKind == MapDirection.Down);
        if (!hasUp && !hasDown) return;

        var centre = Wpf(vm.Viewport.Project(room));
        var radius = NodeRadius(vm);
        var size = Math.Clamp(radius * 0.7, 5.0, 11.0);

        var slot = 0;
        if (hasUp)
        {
            var tip = new Point(centre.X, centre.Y - radius - size);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - size * 0.7, tip.Y + size), true, false);
                ctx.LineTo(new Point(tip.X + size * 0.7, tip.Y + size), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(VerticalBrush, VerticalPen, geometry);
            slot++;
        }

        if (hasDown)
        {
            var tip = new Point(centre.X, centre.Y + radius + size);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(tip.X - size * 0.7, tip.Y - size), true, false);
                ctx.LineTo(new Point(tip.X + size * 0.7, tip.Y - size), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(VerticalBrush, VerticalPen, geometry);
        }
    }

    // â”€â”€ Room nodes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Draws every visible discovered room as a compact node.
    /// </summary>
    /// <remarks>
    /// Nodes stay small and dense on purpose: the map has to remain readable
    /// with hundreds of rooms on screen. Visual weight is spent only where it
    /// earns attention â€” the current room, the selection, and landmark rooms.
    /// </remarks>
    private static void DrawRooms(
        DrawingContext dc, MapState state, MapViewModel vm, IReadOnlyList<MapRoomRecord> rooms)
    {
        var selectedId = vm.SelectedRoomId;
        var radius = NodeRadius(vm);
        var showLabels = vm.Viewport.Scale >= 26;
        var showMarkerBadges = vm.Viewport.Scale >= 20;

        // Far-to-near so the current room and the selection draw last and are
        // never hidden behind an ordinary node.
        foreach (var room in rooms)
        {
            var centre = Wpf(vm.Viewport.Project(room));
            if (!IsOnScreen(centre, vm)) continue;

            var isCurrent = state.IsCurrentRoom(room.RoomId);
            var isSelected = string.Equals(room.RoomId, selectedId, StringComparison.Ordinal);
            var isLandmark = IsLandmark(room);

            var half = isCurrent ? radius * 1.9 : (isSelected ? radius * 1.5 : radius);
            var rect = new Rect(
                centre.X - half, centre.Y - half,
                half * 2, half * 2);

            // Distinct outline + fill so the current room is unmistakable even in
            // greyscale or for a colour-blind viewer: it is the only room with a
            // heavy ring AND a saturated fill, and it is roughly twice the size.
            if (isCurrent)
            {
                dc.DrawRectangle(null, CurrentPen, rect);
                dc.DrawRectangle(CurrentBrush, CurrentPen, rect);
            }
            else if (isLandmark)
            {
                // Landmark rooms get the warmer gold fill so services, banks and
                // cities stand out from ordinary rooms by colour AND fill.
                dc.DrawRectangle(LandmarkerFillBrush, LandmarkerPen, rect);
            }
            else
            {
                dc.DrawRectangle(RoomFillBrush, RoomPen, rect);
            }

            if (isSelected)
            {
                var outer = new Rect(
                    rect.X - 3, rect.Y - 3,
                    rect.Width + 6, rect.Height + 6);
                dc.DrawRectangle(null, SelectedPen, outer);
            }

            // A frontier-adjacent room gets a dotted halo so "this room still has
            // somewhere unexplored" is visible without opening it.
            if (!isCurrent && state.HasFrontier(room.RoomId))
            {
                var halo = new Rect(
                    rect.X - 2.5, rect.Y - 2.5,
                    rect.Width + 5, rect.Height + 5);
                dc.DrawRectangle(null, FrontierPen, halo);
            }

            if (state.HasVerticalConnection(room.RoomId))
                DrawVerticalBadges(dc, state, vm, room);

            if (showMarkerBadges && (isCurrent || isSelected || isLandmark))
                DrawMarkerBadge(dc, room, centre, radius, isCurrent);

            if (showLabels && (isCurrent || isSelected))
                DrawRoomLabel(dc, room, centre, half);
        }
    }

    /// <summary>
    /// True when a room carries a marker worth calling out visually.
    /// </summary>
    /// <remarks>
    /// Only markers Keystone actually sends are considered, and only ones with a
    /// clear, non-speculative meaning. "safe" is excluded because every room in
    /// the realm carries it, so highlighting it would mark the entire map.
    /// </remarks>
    private static bool IsLandmark(MapRoomRecord room) =>
        room.Markers.Any(m => LandmarkMarkers.Contains(m));

    private static readonly HashSet<string> LandmarkMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "bank", "healing", "shop", "institution", "service",
        "city", "town_centre", "settlement", "area_ingress",
        "start_room", "reserved_connector"
    };

    /// <summary>
    /// Draws a compact two-letter badge identifying the room's strongest marker.
    /// </summary>
    private static void DrawMarkerBadge(
        DrawingContext dc, MapRoomRecord room, Point centre, double radius, bool isCurrent)
    {
        foreach (var marker in room.Markers)
        {
            var text = MarkerGlyph(marker);
            if (text is null) continue;

            var badgeSize = Math.Clamp(radius * 0.95, 8.0, 16.0);
            var origin = new Point(
                centre.X + radius * 0.85 - badgeSize * 0.5,
                centre.Y - radius * 0.85 - badgeSize * 0.5);

            dc.DrawRectangle(
                isCurrent ? CurrentBrush : LandmarkerFillBrush,
                isCurrent ? CurrentPen : LandmarkerPen,
                new Rect(origin, new Size(badgeSize, badgeSize)));

            DrawCenteredGlyph(dc, text, new Point(
                origin.X + badgeSize / 2.0,
                origin.Y + badgeSize / 2.0),
                Math.Max(7.0, badgeSize * 0.62),
                isCurrent ? BackgroundBrush : LandmarkerBrushForText);

            // One badge per room is enough; the details pane lists the rest.
            break;
        }
    }

    private static readonly Brush LandmarkerBrushForText = Frozen(Color.FromRgb(0xF2, 0xE3, 0xC0));

    /// <summary>Maps a Keystone marker to a short, honest glyph.</summary>
    private static string? MarkerGlyph(string marker) => marker.ToLowerInvariant() switch
    {
        "bank" => "$",
        "healing" => "+",
        "shop" => "$",
        "institution" => "I",
        "service" => "?",
        "city" => "C",
        "town_centre" => "T",
        "settlement" => "S",
        "area_ingress" => ">",
        "start_room" => "*",
        "reserved_connector" => "^",
        _ => null
    };

    // â”€â”€ Frontier / fog of war â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Draws the unexplored exits leaving each visible room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the fog-of-war boundary of the whole feature.</b> Keystone
    /// discloses only the source room and the direction, so this method draws
    /// exactly that: a short dashed stub leaving the source room with a "?" at
    /// the end. No destination node is created, no destination coordinates are
    /// computed, and no destination name, id, area or district is shown â€”
    /// because none of that was ever sent.
    /// </para>
    /// <para>
    /// The stub length is a fixed fraction of a cell and is purely a drawing
    /// affordance. It is emphatically NOT a claim about where the hidden room
    /// is, and it is never fed back into any model.
    /// </para>
    /// </remarks>
    private static void DrawFrontier(
        DrawingContext dc, MapState state, MapViewModel vm, IReadOnlyList<MapRoomRecord> rooms)
    {
        foreach (var room in rooms)
        {
            var stubs = state.GetFrontier(room.RoomId);
            if (stubs.Count == 0) continue;

            var centre = Wpf(vm.Viewport.Project(room));
            if (!IsOnScreen(centre, vm)) continue;

            // The stub must start OUTSIDE everything drawn for this room,
            // including the player's large reticle, or the "?" would be buried
            // under it.
            var radius = NodeRadius(vm);
            var extent = RoomExtent(state, vm, room);
            var stubLength = Math.Clamp(radius * 2.0, 12.0, 30.0);

            foreach (var stub in stubs)
            {
                var direction = stub.DirectionKind;
                if (direction == MapDirection.Unknown) continue;

                // Where the stub points is the real direction sign, not a
                // destination: it tells the player which way to walk to explore.
                var (unitX, unitY) = MapDirections.HorizontalUnitStep(direction);
                double offsetX = 0, offsetY = 0;

                if (MapDirections.IsVertical(direction))
                {
                    // A vertical unexplored exit has no place on this plane, so
                    // it is flagged straight above/below instead of sideways.
                    offsetY = direction == MapDirection.Up ? -1 : 1;
                }
                else
                {
                    offsetX = unitX * 0.7071;
                    offsetY = -unitY * 0.7071;
                }

                var start = new Point(
                    centre.X + offsetX * (extent + 2),
                    centre.Y + offsetY * (extent + 2));
                var end = new Point(
                    centre.X + offsetX * (extent + 2 + stubLength),
                    centre.Y + offsetY * (extent + 2 + stubLength));

                dc.DrawLine(FrontierPen, start, end);

                // A "?" marks the end of the fog. The dashed line and the glyph
                // both mean "unexplored", so the meaning survives greyscale.
                DrawCenteredGlyph(dc, "?", end, Math.Max(9.0, radius * 1.1), FrontierBrush);
            }
        }
    }

    /// <summary>
    /// The full half-size actually drawn for a room, including the enlarged
    /// current-room node and the player reticle drawn over it.
    /// </summary>
    /// <remarks>
    /// Used so frontier stubs and marker badges start clear of the room rather
    /// than on top of it. The reticle is the widest thing drawn for a room, so
    /// it sets the clearance for the current room.
    /// </remarks>
    private static double RoomExtent(MapState state, MapViewModel vm, MapRoomRecord room)
    {
        var radius = NodeRadius(vm);

        if (state.IsCurrentRoom(room.RoomId))
        {
            // Matches the outer reticle ring drawn by DrawCurrentRoomMarker.
            return radius * 2.6;
        }

        return string.Equals(room.RoomId, vm.SelectedRoomId, StringComparison.Ordinal)
            ? radius * 1.5
            : radius;
    }

    /// <summary>
    /// Draws the player's position marker as the last, topmost element.
    /// </summary>
    /// <remarks>
    /// A pulsing-looking target reticle built purely from rings, so it reads as
    /// "you are here" without colour. Drawn last so no other node can overlap it.
    /// </remarks>
    private static void DrawCurrentRoomMarker(
        DrawingContext dc, MapState state, MapViewModel vm)
    {
        if (state.CurrentRoomId is null) return;
        if (!state.TryGetRoom(state.CurrentRoomId, out var room)) return;

        // Respect the active floor filter: if the player is on another floor the
        // marker must not float over the current one.
        if (state.SelectedZ is int floor && room.Z != floor) return;

        var centre = Wpf(vm.Viewport.Project(room));
        var radius = NodeRadius(vm);

        var outer = radius * 2.6;
        dc.DrawEllipse(null, CurrentPen, centre, outer, outer);

        var inner = radius * 1.9;
        dc.DrawEllipse(CurrentBrush, CurrentPen, centre, inner, inner);

        // A solid centre dot keeps the marker legible when the room node itself
        // is small at low zoom.
        dc.DrawEllipse(BackgroundBrush, CurrentPen, centre, radius * 0.55, radius * 0.55);

        // Cardinal ticks make the marker's orientation obvious and give a
        // stronger visual anchor than colour alone.
        DrawMarkerTicks(dc, centre, outer);
    }

    private static void DrawMarkerTicks(DrawingContext dc, Point centre, double radius)
    {
        var tick = radius * 0.45;
        dc.DrawLine(CurrentPen, new Point(centre.X, centre.Y - radius - tick),
                                new Point(centre.X, centre.Y - radius));
        dc.DrawLine(CurrentPen, new Point(centre.X, centre.Y + radius),
                                new Point(centre.X, centre.Y + radius + tick));
        dc.DrawLine(CurrentPen, new Point(centre.X - radius - tick, centre.Y),
                                new Point(centre.X - radius, centre.Y));
        dc.DrawLine(CurrentPen, new Point(centre.X + radius, centre.Y),
                                new Point(centre.X + radius + tick, centre.Y));
    }

    // â”€â”€ Drawing helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// The half-size of a room node at the current zoom.
    /// </summary>
    /// <remarks>
    /// Nodes shrink with zoom but are clamped so they never become invisible or
    /// unboundedly large. The floor of ~1.6px keeps a fully revealed 7943-room
    /// map legible as a dense field rather than vanishing.
    /// </remarks>
    private static double NodeRadius(MapViewModel vm) =>
        Math.Clamp(vm.Viewport.Scale * 0.16, 1.6, 18.0);

    /// <summary>
    /// True when a projected point is near enough to the viewport to be worth
    /// drawing.
    /// </summary>
    /// <remarks>
    /// Culling is the main reason the full-reveal map stays fast: at a normal
    /// zoom only a small fraction of 7943 rooms is ever on screen, so the
    /// expensive per-room work is skipped for everything else.
    /// </remarks>
    private static bool IsOnScreen(Point point, MapViewModel vm)
    {
        const double margin = 80.0;
        var width = vm.Viewport.ViewportCentre.X * 2.0;
        var height = vm.Viewport.ViewportCentre.Y * 2.0;

        return point.X >= -margin && point.X <= width + margin &&
               point.Y >= -margin && point.Y <= height + margin;
    }

    /// <summary>Unit-length vector, or (0,0) for a degenerate input.</summary>
    private static Vector Normalize(Vector vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        if (length < 1e-6) return default;
        return new Vector(vector.X / length, vector.Y / length);
    }

    /// <summary>
    /// Draws a single glyph centred on a point.
    /// </summary>
    /// <remarks>
    /// Glyphs are only used for a handful of symbols ("?", marker letters), so
    /// building a FormattedText on demand is cheaper than caching every possible
    /// string at every zoom level.
    /// </remarks>
    private static double _pixelsPerDip = 1.0;

    /// <summary>
    /// Draws a single glyph centred on a point.
    /// </summary>
    /// <remarks>
    /// Glyphs are only used for a handful of symbols ("?", marker letters), so
    /// building a FormattedText on demand is cheaper than caching every possible
    /// string at every zoom level. The DPI scale is cached per render pass by
    /// <see cref="OnRender"/> so no visual-tree lookup happens per glyph.
    /// </remarks>
    private static void DrawCenteredGlyph(
        DrawingContext dc, string text, Point centre, double fontSize, Brush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            Math.Clamp(fontSize, 6.0, 22.0),
            brush,
            _pixelsPerDip);

        dc.DrawText(formatted, new Point(
            centre.X - (formatted.Width / 2.0),
            centre.Y - (formatted.Height / 2.0)));
    }

    /// <summary>
    /// Draws a room's name above its node.
    /// </summary>
    /// <remarks>
    /// Only ever called for the current and selected rooms, so the map never
    /// fills with unreadable overlapping labels at low zoom.
    /// </remarks>
    private static void DrawRoomLabel(
        DrawingContext dc, MapRoomRecord room, Point centre, double half)
    {
        if (string.IsNullOrWhiteSpace(room.Name)) return;

        var formatted = new FormattedText(
            room.Name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11.0,
            LabelBrush,
            _pixelsPerDip);

        // A dark plate behind the text keeps it readable over edges and nodes.
        var origin = new Point(
            centre.X - (formatted.Width / 2.0),
            centre.Y - half - formatted.Height - 4);

        dc.DrawRectangle(
            BackgroundBrush,
            null,
            new Rect(origin.X - 3, origin.Y - 1, formatted.Width + 6, formatted.Height + 2));

        dc.DrawText(formatted, origin);
    }

    // â”€â”€ Frozen resources â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness, DashStyle? dashStyle = null)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var pen = dashStyle is null
            ? new Pen(brush, thickness)
            : new Pen(brush, thickness);

        // A frozen pen is shared safely across the single render thread and
        // removes all per-frame allocation from the drawing hot path.
        if (dashStyle is not null)
            pen.DashStyle = dashStyle;

        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// Converts a UI-free <see cref="MapPoint"/> into a WPF <see cref="Point"/>.
    /// </summary>
    /// <remarks>
    /// The viewport deliberately lives in a plain net8.0 project so it can be
    /// unit tested without WPF; this adapter is the single, explicit boundary
    /// where its screen coordinates become WPF drawing coordinates.
    /// </remarks>
    private static Point Wpf(MapPoint point) => new(point.X, point.Y);
}
