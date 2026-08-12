namespace DotNetDBTasks.Domain.Entities;

/// <summary>
/// A system-wide toggle an administrator can change at runtime, stored as a key/value pair.
///
/// <para>
/// Deliberately in the database rather than appsettings.json: these are meant to be flipped
/// from the UI without editing a file and restarting, which matters most on the air-gapped
/// installs where a restart is a scheduled event.
/// </para>
///
/// <para>
/// A missing row means "use the default" — see <c>SystemSettingKeys</c>. Nothing seeds these,
/// so an upgraded database behaves the same as a fresh one until someone changes something.
/// </para>
/// </summary>
public class SystemSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;

    /// <summary>Stored as text; the reader parses it. Booleans are "true"/"false".</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The known setting keys and their defaults.
///
/// <para>Keys are persisted, so renaming one silently resets it to the default — treat them
/// as a contract.</para>
/// </summary>
public static class SystemSettingKeys
{
    /// <summary>
    /// Whether an Access Manager may change access on an <em>individual query</em>.
    ///
    /// <para>Off by default: the role's normal remit is query <em>groups</em>, which is the
    /// coarser and safer control — granting a group is a deliberate, visible act, where
    /// per-query grants accumulate quietly. Turning this on restores the per-query
    /// role/department/user assignment endpoints for the role. Admins are never affected.</para>
    /// </summary>
    public const string AccessManagerCanManageQueryAccess = "accessManager.canManageQueryAccess";

    public const bool AccessManagerCanManageQueryAccessDefault = false;
}
