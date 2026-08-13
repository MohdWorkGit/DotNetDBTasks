namespace Bayan.Domain.Entities;

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
    /// How long an issued access token stays valid, in minutes.
    ///
    /// <para>One hour by default. Shorter means a revoked account, or a user removed from a
    /// group, loses what the token still carries sooner; longer means fewer silent refreshes.
    /// Bounded at <see cref="AccessTokenMinutesMin"/>–<see cref="AccessTokenMinutesMax"/>: below
    /// the floor the application spends its time refreshing, and a token good for more than a
    /// day defeats the point of expiry.</para>
    /// </summary>
    public const string SessionAccessTokenMinutes = "session.accessTokenMinutes";

    public const int SessionAccessTokenMinutesDefault = 60;
    public const int AccessTokenMinutesMin = 5;
    public const int AccessTokenMinutesMax = 1440;

    /// <summary>
    /// How long a refresh token stays valid, in days — in effect, how long someone may stay
    /// away and still return without signing in again. Seven days by default; must be at least
    /// as long as the access token, or refreshing could never happen.
    /// </summary>
    public const string SessionRefreshTokenDays = "session.refreshTokenDays";

    public const int SessionRefreshTokenDaysDefault = 7;
    public const int RefreshTokenDaysMin = 1;
    public const int RefreshTokenDaysMax = 90;

    /// <summary>
    /// The most rows read in one go on the paths that hold a whole result in memory.
    ///
    /// <para>
    /// Ten thousand by default. It governs three things: the preview of a write query, the
    /// before-change snapshot recorded for an UPDATE or DELETE, and the option list behind a
    /// dropdown parameter. Each of those reads everything at once, so an unbounded one is a
    /// memory incident waiting for the wrong <c>WHERE</c> clause.
    /// </para>
    ///
    /// <para>
    /// It is deliberately <em>not</em> a display limit. The results grid caches the full result
    /// set and pages through it, exports stream it, and scheduled tasks run reads unlimited —
    /// none of them are affected. A row here overrides the <c>MaxQueryRows</c> value in
    /// appsettings.json, which stays the fallback for installations that already set it.
    /// </para>
    /// </summary>
    public const string QueryMaxRows = "query.maxRows";

    public const int QueryMaxRowsDefault = 10000;
    public const int QueryMaxRowsMin = 100;
    public const int QueryMaxRowsMax = 1_000_000;

    /// <summary>
    /// Whether the Active Directory pages are available.
    ///
    /// <para>On by default. Turn it off on an installation with no directory, where the AD Users
    /// page can only ever report a connection failure — access itself comes from user groups
    /// this application owns, so nothing is lost by hiding it.</para>
    ///
    /// <para>Deliberately does <em>not</em> gate authentication. Accounts already imported from
    /// the directory keep signing in against it, so flipping this by mistake hides a page rather
    /// than locking people out.</para>
    /// </summary>
    public const string DirectoryEnabled = "directory.enabled";

    public const bool DirectoryEnabledDefault = true;

    /// <summary>
    /// The site name shown in the top banner, per language.
    ///
    /// <para>Two rows rather than one, because the banner has to read correctly in both
    /// languages: an installation is called something different in Arabic than in English, and
    /// storing a single string would force one of the two audiences to read the other's name.
    /// Either may be left blank on its own — a blank one falls back to the translated
    /// application name for that language, so filling in only the Arabic is valid.</para>
    ///
    /// <para>Lives here rather than in <c>SystemTemplates</c> beside the logo because it is
    /// text, not an uploaded file — but it is edited in the same dialog and guarded by the
    /// same <c>branding.manage</c> permission, not by <c>settings.manage</c>.</para>
    /// </summary>
    public const string BrandingSiteNameEn = "branding.siteNameEn";

    public const string BrandingSiteNameAr = "branding.siteNameAr";

    /// <summary>
    /// Longest accepted site name. The banner caps the logo at 200 px wide and the name shares
    /// that space with the navigation, so a longer string would be truncated on screen rather
    /// than displayed — refusing it is clearer than silently clipping it.
    /// </summary>
    public const int BrandingSiteNameMaxLength = 60;
}
