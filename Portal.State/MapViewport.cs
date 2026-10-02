using System.ComponentModel;
using System.Runtime.CompilerServices;
using Portal.Protocol;

namespace Portal.State;

/// <summary>
/// A UI-independent screen-space point, in pixels.
/// </summary>
/// <remarks>
/// Intentionally NOT <c>System.Windows.Point</c> or
/// <c>System.Drawing.Point</c>: this project targets plain <c>net8.0</c> with no
/// WPF or Windows Forms dependency, which keeps the whole projection and
/// viewport model unit testable outside a UI thread.
/// </remarks>
/// <param name="X">Horizontal pixel offset from the left of the map surface.</param>
/// <param name="Y">Vertical pixel offset from the top of the map surface.</param>
public readonly record struct MapPoint(double X, double Y);

/// <summary>
/// Maps world coordinates to screen pixels and owns the map's zoom and pan.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a plain, UI-free class: all transform maths lives here
/// so it can be unit tested directly, and the WPF control only supplies the
/// current viewport size.
/// </para>
/// <para>
/// <b>Coordinate mapping (matches Keystone exactly):</b> world X increases
/// east, world Y increases north, world Z is the floor. Screen Y grows downward,
/// so world north is drawn UP: <c>screenY = originY - worldY * scale</c>. Z is
/// never folded into the 2-D projection — floors are selected, not stacked.
/// </para>
/// <para>
/// <b>Zoom is clamped to <see cref="MinZoom"/>..<see cref="MaxZoom"/> on every
/// single mutation</b>, including repeated wheel input and programmatic calls,
/// so no sequence of operations can produce a runaway or degenerate transform.
/// </para>
/// </remarks>
public sealed class MapViewport : INotifyPropertyChanged
{
    /// <summary>Smallest permitted zoom factor.</summary>
    public const double MinZoom = 0.15;

    /// <summary>Largest permitted zoom factor.</summary>
    public const double MaxZoom = 6.0;

    /// <summary>Zoom used by <see cref="Reset"/> and by "center on player".</summary>
    public const double DefaultZoom = 1.0;

    /// <summary>Zoom applied by one step of the +/- buttons.</summary>
    public const double ZoomStep = 1.25;

    /// <summary>Screen pixels per world unit at zoom 1.0. Tuned for readability.</summary>
    public const double PixelsPerCell = 34.0;

    private double _zoom = DefaultZoom;
    private double _originX;
    private double _originY;
    private double _viewWidth = 1;
    private double _viewHeight = 1;

    /// <summary>Raised whenever the transform changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Current zoom factor, always within <see cref="MinZoom"/>..<see cref="MaxZoom"/>.</summary>
    public double Zoom
    {
        get => _zoom;
        private set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(_zoom - clamped) < 1e-9) return;
            _zoom = clamped;
            Raise();
            Raise(nameof(ZoomPercent));
        }
    }

    /// <summary>The zoom as a whole percentage, for the zoom readout.</summary>
    public int ZoomPercent => (int)Math.Round(_zoom * 100.0);

    /// <summary>Screen X of world origin (0,0) on the selected floor.</summary>
    public double OriginX => _originX;

    /// <summary>Screen Y of world origin (0,0) on the selected floor.</summary>
    public double OriginY => _originY;

    /// <summary>The current viewport size in pixels.</summary>
    public void SetViewportSize(double width, double height)
    {
        // Guard against a zero/negative size during layout, which would make
        // every later projection produce NaN or infinity.
        _viewWidth = Math.Max(1.0, width);
        _viewHeight = Math.Max(1.0, height);
    }

    /// <summary>The scale in pixels per world unit at the current zoom.</summary>
    public double Scale => PixelsPerCell * _zoom;

    // ─── Projection ──────────────────────────────────────────────────────

    /// <summary>Projects a world X/Y pair to screen pixels.</summary>
    public MapPoint Project(int worldX, int worldY)
    {
        var scale = Scale;
        return new MapPoint(
            _originX + worldX * scale,
            _originY - worldY * scale);
    }

    /// <summary>Projects a room's authoritative world X/Y to screen pixels.</summary>
    public MapPoint Project(MapRoomRecord room) => Project(room.X, room.Y);

    /// <summary>Converts a screen point back to world coordinates (inverse projection).</summary>
    public (double X, double Y) Unproject(MapPoint screenPoint)
    {
        var scale = Scale;
        if (scale <= 0) return (0, 0);
        return ((screenPoint.X - _originX) / scale, (_originY - screenPoint.Y) / scale);
    }

    // ─── Zoom ───────────────────────────────────────────────────────────

    /// <summary>
    /// Multiplies the zoom by <paramref name="factor"/>, keeping the world point
    /// currently under <paramref name="anchor"/> (screen pixels) fixed.
    /// </summary>
    /// <remarks>
    /// Anchored zoom is what makes the mouse wheel feel correct: the room the
    /// user is pointing at stays under the cursor instead of drifting away.
    /// </remarks>
    public void ZoomAt(MapPoint anchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            return;

        var before = Unproject(anchor);
        Zoom = _zoom * factor;

        // Re-derive the origin so the same world point is still under the anchor.
        var scale = Scale;
        if (scale <= 0) return;

        _originX = anchor.X - before.X * scale;
        _originY = anchor.Y + before.Y * scale;
        Raise(nameof(OriginX));
        Raise(nameof(OriginY));
        Raise(nameof(Scale));
    }

    /// <summary>Zooms in one step about the centre of the viewport.</summary>
    public void ZoomIn() => ZoomAt(ViewportCentre, ZoomStep);

    /// <summary>Zooms out one step about the centre of the viewport.</summary>
    public void ZoomOut() => ZoomAt(ViewportCentre, 1.0 / ZoomStep);

    /// <summary>The centre of the current viewport in screen pixels.</summary>
    public MapPoint ViewportCentre => new(_viewWidth / 2.0, _viewHeight / 2.0);

    // ─── Pan ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Drags the map by a screen-space delta in pixels.
    /// </summary>
    /// <param name="deltaX">Horizontal mouse movement in pixels.</param>
    /// <param name="deltaY">Vertical mouse movement in pixels.</param>
    /// <remarks>
    /// The Y sign is inverted because world north is drawn upward: dragging the
    /// content down must move the view toward higher world Y.
    /// </remarks>
    public void PanBy(double deltaX, double deltaY)
    {
        if (double.IsNaN(deltaX) || double.IsNaN(deltaY) ||
            double.IsInfinity(deltaX) || double.IsInfinity(deltaY))
        {
            return;
        }

        _originX += deltaX;
        _originY += deltaY;

        Raise(nameof(OriginX));
        Raise(nameof(OriginY));
    }

    /// <summary>
    /// Centres the view on a world coordinate, optionally changing zoom.
    /// </summary>
    /// <remarks>
    /// This is the implementation behind both "CENTER ON PLAYER" and "FIT
    /// AREA". Zoom is only changed when explicitly requested, so following the
    /// player while the user has zoomed in does not silently undo their zoom.
    /// </remarks>
    public void CenterOn(int worldX, int worldY, double? zoom = null)
    {
        if (zoom.HasValue)
            Zoom = zoom.Value;

        var scale = Scale;
        if (scale <= 0) return;

        _originX = _viewWidth / 2.0 - worldX * scale;
        _originY = _viewHeight / 2.0 + worldY * scale;

        Raise(nameof(OriginX));
        Raise(nameof(OriginY));
    }

    /// <summary>Centres the view on a room's authoritative coordinates.</summary>
    public void CenterOn(MapRoomRecord room) => CenterOn(room.X, room.Y);

    /// <summary>
    /// Zooms and pans so the supplied world points all fit inside the viewport,
    /// with a margin. No-ops on an empty set.
    /// </summary>
    /// <remarks>
    /// Used by "FIT AREA". The result is clamped by <see cref="Zoom"/> in both
    /// directions, so fitting a very large or very small region can never
    /// produce a degenerate transform.
    /// </remarks>
    public void FitTo(IReadOnlyCollection<MapRoomRecord> rooms, double marginPixels = 48.0)
    {
        if (rooms is null || rooms.Count == 0) return;

        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var minY = int.MaxValue;
        var maxY = int.MinValue;

        foreach (var room in rooms)
        {
            if (room.X < minX) minX = room.X;
            if (room.X > maxX) maxX = room.X;
            if (room.Y < minY) minY = room.Y;
            if (room.Y > maxY) maxY = room.Y;
        }

        // A single room (or a perfectly vertical/horizontal run) has zero extent
        // on one axis; use the full viewport for that axis instead of dividing
        // by zero.
        var spanX = Math.Max(maxX - minX, 1);
        var spanY = Math.Max(maxY - minY, 1);

        var usableWidth = Math.Max(1.0, _viewWidth - marginPixels * 2.0);
        var usableHeight = Math.Max(1.0, _viewHeight - marginPixels * 2.0);

        // Solve for the zoom that makes the span fit, then clamp it.
        var rawZoom = Math.Min(
            usableWidth / (spanX * PixelsPerCell),
            usableHeight / (spanY * PixelsPerCell));

        if (double.IsNaN(rawZoom) || double.IsInfinity(rawZoom)) rawZoom = DefaultZoom;

        Zoom = Math.Clamp(rawZoom, MinZoom, MaxZoom);

        var centreX = (minX + maxX) / 2.0;
        var centreY = (minY + maxY) / 2.0;
        CenterOn((int)Math.Round(centreX), (int)Math.Round(centreY));
    }

    /// <summary>
    /// Restores the default zoom and re-centres on the given world point.
    /// </summary>
    public void Reset(int worldX, int worldY) => CenterOn(worldX, worldY, DefaultZoom);

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}