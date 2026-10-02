namespace Portal.Protocol;

/// <summary>
/// The ten canonical map directions used by Keystone's generated realm map.
/// The wire format is the lower-case Keystone direction string; this enum
/// mirrors that vocabulary exactly and adds nothing. A direction Keystone did
/// not send is always <see cref="MapDirection.Unknown"/> rather than a guess.
/// </summary>
public enum MapDirection
{
    /// <summary>Not a direction Keystone recognises.</summary>
    Unknown = 0,

    /// <summary>North — increasing world Y.</summary>
    North,

    /// <summary>South — decreasing world Y.</summary>
    South,

    /// <summary>East — increasing world X.</summary>
    East,

    /// <summary>West — decreasing world X.</summary>
    West,

    /// <summary>North-east.</summary>
    NorthEast,

    /// <summary>North-west.</summary>
    NorthWest,

    /// <summary>South-east.</summary>
    SouthEast,

    /// <summary>South-west.</summary>
    SouthWest,

    /// <summary>Vertical ascent — increasing world Z.</summary>
    Up,

    /// <summary>Vertical descent — decreasing world Z.</summary>
    Down
}

/// <summary>
/// Helpers for converting between Keystone's canonical direction strings and
/// <see cref="MapDirection"/>, and for reasoning about direction geometry.
/// </summary>
/// <remarks>
/// <para><b>Geometry contract (verified against the frozen canonical map):</b></para>
/// <list type="bullet">
/// <item><description>north = +Y, south = -Y, east = +X, west = -X.</description></item>
/// <item><description>northeast = (+X,+Y), northwest = (-X,+Y),
/// southeast = (+X,-Y), southwest = (-X,-Y).</description></item>
/// <item><description>up = +Z, down = -Z.</description></item>
/// </list>
/// <para>
/// Real Keystone exits are NOT always a single grid step — "east" may span
/// three cells and "up" may climb two levels. The unit step below is therefore
/// only a drawing hint and a sign test; it is never used to work out where a
/// room actually is. Every room is always drawn at its authoritative x/y/z.
/// </para>
/// </remarks>
public static class MapDirections
{
    /// <summary>Short, high-contrast label used for map badges and text.</summary>
    public static string ToLabel(MapDirection direction) => direction switch
    {
        MapDirection.North => "N",
        MapDirection.South => "S",
        MapDirection.East => "E",
        MapDirection.West => "W",
        MapDirection.NorthEast => "NE",
        MapDirection.NorthWest => "NW",
        MapDirection.SouthEast => "SE",
        MapDirection.SouthWest => "SW",
        MapDirection.Up => "U",
        MapDirection.Down => "D",
        _ => "?"
    };

    /// <summary>Human readable direction name for tooltips and the inspection panel.</summary>
    public static string ToDisplayName(MapDirection direction) => direction switch
    {
        MapDirection.North => "North",
        MapDirection.South => "South",
        MapDirection.East => "East",
        MapDirection.West => "West",
        MapDirection.NorthEast => "North-east",
        MapDirection.NorthWest => "North-west",
        MapDirection.SouthEast => "South-east",
        MapDirection.SouthWest => "South-west",
        MapDirection.Up => "Up",
        MapDirection.Down => "Down",
        _ => "Unknown"
    };

    /// <summary>The exact lower-case wire token Keystone sends for a direction.</summary>
    public static string ToWireValue(MapDirection direction) => direction switch
    {
        MapDirection.North => "north",
        MapDirection.South => "south",
        MapDirection.East => "east",
        MapDirection.West => "west",
        MapDirection.NorthEast => "northeast",
        MapDirection.NorthWest => "northwest",
        MapDirection.SouthEast => "southeast",
        MapDirection.SouthWest => "southwest",
        MapDirection.Up => "up",
        MapDirection.Down => "down",
        _ => string.Empty
    };

    /// <summary>
    /// Parses a Keystone direction string. Case and surrounding whitespace are
    /// tolerated; anything unrecognised yields <see cref="MapDirection.Unknown"/>
    /// rather than an exception, so a malformed payload can never crash the map.
    /// </summary>
    public static MapDirection Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return MapDirection.Unknown;

        return value.Trim().ToLowerInvariant() switch
        {
            "north" or "n" => MapDirection.North,
            "south" or "s" => MapDirection.South,
            "east" or "e" => MapDirection.East,
            "west" or "w" => MapDirection.West,
            "northeast" or "ne" => MapDirection.NorthEast,
            "northwest" or "nw" => MapDirection.NorthWest,
            "southeast" or "se" => MapDirection.SouthEast,
            "southwest" or "sw" => MapDirection.SouthWest,
            "up" or "u" => MapDirection.Up,
            "down" or "d" => MapDirection.Down,
            _ => MapDirection.Unknown
        };
    }

    /// <summary>
    /// True for the two vertical directions, which must never be drawn as an
    /// ordinary 2-D line because they do not move across the map plane.
    /// </summary>
    public static bool IsVertical(MapDirection direction) =>
        direction is MapDirection.Up or MapDirection.Down;

    /// <summary>True when the direction lies in the map plane (everything except Up/Down).</summary>
    public static bool IsHorizontal(MapDirection direction) =>
        direction != MapDirection.Unknown && !IsVertical(direction);

    /// <summary>True when the direction points at least partly toward decreasing world X.</summary>
    public static bool PointsWest(MapDirection direction) => direction
        is MapDirection.West or MapDirection.NorthWest or MapDirection.SouthWest;

    /// <summary>True when the direction points at least partly toward increasing world X.</summary>
    public static bool PointsEast(MapDirection direction) => direction
        is MapDirection.East or MapDirection.NorthEast or MapDirection.SouthEast;

    /// <summary>True when the direction points at least partly toward increasing world Y (map "up").</summary>
    public static bool PointsNorth(MapDirection direction) => direction
        is MapDirection.North or MapDirection.NorthEast or MapDirection.NorthWest;

    /// <summary>True when the direction points at least partly toward decreasing world Y (map "down").</summary>
    public static bool PointsSouth(MapDirection direction) => direction
        is MapDirection.South or MapDirection.SouthEast or MapDirection.SouthWest;

    /// <summary>
    /// The one-cell step vector for a direction. This is a DRAWING HINT ONLY:
    /// real Keystone exits may span several cells, so it is never used to place
    /// or infer a room's coordinates.
    /// </summary>
    public static (int X, int Y) HorizontalUnitStep(MapDirection direction) => direction switch
    {
        MapDirection.North => (0, 1),
        MapDirection.South => (0, -1),
        MapDirection.East => (1, 0),
        MapDirection.West => (-1, 0),
        MapDirection.NorthEast => (1, 1),
        MapDirection.NorthWest => (-1, 1),
        MapDirection.SouthEast => (1, -1),
        MapDirection.SouthWest => (-1, -1),
        _ => (0, 0)
    };

    /// <summary>
    /// Tests whether a displacement between two rooms is consistent with a
    /// direction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used ONLY to decide whether a frontier stub that Keystone reported for
    /// <c>(from, direction)</c> is now satisfied by a room the character has
    /// just discovered. Both endpoints are already-discovered rooms whose
    /// coordinates Keystone sent, so nothing hidden is inferred and no exit is
    /// ever invented.
    /// </para>
    /// <para>
    /// The test is an axis/sign test rather than an equality test, because real
    /// Keystone exits span variable distances ("east" may be three cells, "up"
    /// may be two levels). A diagonal requires movement on BOTH horizontal
    /// axes; a horizontal direction requires the other axis to be unchanged; a
    /// vertical direction requires no horizontal movement at all.
    /// </para>
    /// </remarks>
    /// <param name="direction">The direction Keystone reported.</param>
    /// <param name="deltaX">Destination X minus source X.</param>
    /// <param name="deltaY">Destination Y minus source Y.</param>
    /// <param name="deltaZ">Destination Z minus source Z.</param>
    /// <returns>True when the displacement could be reached in that direction.</returns>
    public static bool IsDisplacementConsistentWith(
        MapDirection direction, int deltaX, int deltaY, int deltaZ) => direction switch
    {
        MapDirection.North => deltaY > 0 && deltaX == 0 && deltaZ == 0,
        MapDirection.South => deltaY < 0 && deltaX == 0 && deltaZ == 0,
        MapDirection.East => deltaX > 0 && deltaY == 0 && deltaZ == 0,
        MapDirection.West => deltaX < 0 && deltaY == 0 && deltaZ == 0,
        MapDirection.NorthEast => deltaX > 0 && deltaY > 0 && deltaZ == 0,
        MapDirection.NorthWest => deltaX < 0 && deltaY > 0 && deltaZ == 0,
        MapDirection.SouthEast => deltaX > 0 && deltaY < 0 && deltaZ == 0,
        MapDirection.SouthWest => deltaX < 0 && deltaY < 0 && deltaZ == 0,
        MapDirection.Up => deltaZ > 0 && deltaX == 0 && deltaY == 0,
        MapDirection.Down => deltaZ < 0 && deltaX == 0 && deltaY == 0,
        _ => false
    };
}