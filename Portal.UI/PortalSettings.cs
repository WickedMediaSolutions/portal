using System.IO;
using System.Text.Json;

namespace Portal.UI;

/// <summary>
/// Local, non-authoritative Portal client preferences that Portal owns
/// outright and that are autosaved by the UI (no manual save action).
///
/// Scope is deliberately tiny: ONLY values that belong to this client on this
/// machine.  The authoritative gameplay state that Keystone owns — HP, XP,
/// level, inventory, equipment, current target, skills, currency and room
/// state — is never stored here and always continues to arrive from Keystone
/// through the existing event stream.
///
/// The account password is never stored in any form: Portal has no secure
/// credential store, so it is neither persisted nor written to disk.
///
/// Storage: one simple human-readable JSON file at
/// <c>%LOCALAPPDATA%\Portal\settings.json</c>.  No database is involved.  All
/// file access here is best-effort: a read or write failure falls back to
/// defaults (or is ignored) so settings can never break startup or shutdown.
/// </summary>
public sealed class PortalSettings
{
    private const string FolderName = "Portal";
    private const string FileName = "settings.json";

    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private static PortalSettings? _current;

    /// <summary>
    /// The single in-memory settings instance, loaded from disk on first
    /// access.  Loading is lazy and swallowed on failure so a missing or
    /// corrupt file simply yields defaults.
    /// </summary>
    public static PortalSettings Current
    {
        get
        {
            lock (Gate)
            {
                return _current ??= Load();
            }
        }
    }

    /// <summary>Full path of the single local settings file.</summary>
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FolderName,
        FileName);

    /// <summary>
    /// On-disk schema version, reserved for future migrations of this file.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Left edge of the last normal (un-maximized) window position, or null
    /// when no position has been recorded yet.
    /// </summary>
    public double? WindowLeft { get; set; }

    /// <summary>
    /// Top edge of the last normal (un-maximized) window position, or null
    /// when no position has been recorded yet.
    /// </summary>
    public double? WindowTop { get; set; }

    /// <summary>Width of the last normal (un-maximized) window.</summary>
    public double WindowWidth { get; set; }

    /// <summary>Height of the last normal (un-maximized) window.</summary>
    public double WindowHeight { get; set; }

    /// <summary>
    /// True when the window was maximized the last time Portal closed.
    /// </summary>
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// Login-bar username convenience value, so the user does not have to
    /// retype the account name every launch.  This is a client-side
    /// convenience only — Keystone still authenticates the account on every
    /// login.  Passwords are never stored.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Client-side development switch: when true, Portal connects to the
    /// developer's own Keystone instance instead of the live game.
    ///
    /// False is the default and is what every ordinary player gets, because a
    /// fresh settings object (and a missing or corrupt settings file) yields
    /// exactly this value. Turning it on is the only way to reach localhost,
    /// and it is persisted here so a developer's choice survives a restart.
    ///
    /// This value selects a connection endpoint only. It never changes any
    /// gameplay behaviour, never alters what Keystone sends, and grants no
    /// additional privilege on the server.
    /// </summary>
    public bool UseLocalDevelopmentServer { get; set; }

    /// <summary>
    /// Writes the current values to the single settings file.  Best-effort:
    /// any I/O failure is ignored because no gameplay state depends on this
    /// file.
    /// </summary>
    public void Save()
    {
        lock (Gate)
        {
            try
            {
                var path = FilePath;
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write to a temporary file and replace, so an interrupted
                // write cannot leave a truncated settings file behind.
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(tempPath, path, overwrite: true);
            }
            catch
            {
                // Best-effort persistence only.
            }
        }
    }

    /// <summary>
    /// Reads the settings file, returning defaults when it is missing,
    /// unreadable or corrupt.
    /// </summary>
    private static PortalSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return new PortalSettings();
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<PortalSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return new PortalSettings();
            }

            // Defensive: a hand-edited file may contain an explicit null.
            if (loaded.Username is null)
            {
                loaded.Username = string.Empty;
            }

            return loaded;
        }
        catch
        {
            return new PortalSettings();
        }
    }
}
