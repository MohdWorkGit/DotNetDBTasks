namespace Jadwal.Domain.Entities;

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
    /// The longest SQL text a saved query may hold, in characters.
    ///
    /// <para>4000 by default, which is what the limit was before it became a setting, so an
    /// upgraded installation behaves the same until someone changes it. The column is a CLOB, so
    /// the database itself is not the constraint; the ceiling of <see cref="QuerySqlMaxLengthMax"/>
    /// only stops a runaway paste. Lowering it does not touch queries already saved — a longer
    /// one keeps running, and is refused only when it is next saved.</para>
    /// </summary>
    public const string QuerySqlMaxLength = "query.sqlMaxLength";

    public const int QuerySqlMaxLengthDefault = 4000;
    public const int QuerySqlMaxLengthMin = 1000;
    public const int QuerySqlMaxLengthMax = 1_000_000;

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
    /// Whether the AI assistant's buttons are offered.
    ///
    /// <para>On by default, because it is the second of two switches: AI only works once the
    /// server's configuration points at a model (<c>Ai:Enabled</c>), which is the operator's
    /// decision. This one lets an administrator take it away again from System Settings, without
    /// a restart — while a model misbehaves, say.</para>
    /// </summary>
    public const string AiEnabled = "ai.enabled";

    public const bool AiEnabledDefault = true;

    /// <summary>
    /// Whether the AI assistant may read the default connection's tables to write SQL.
    ///
    /// <para>Off by default. The default connection is Jadwal's own database: its own tables are
    /// always hidden from the AI, but anything else in that schema would be shown. A named
    /// connection has its own switch (<c>DatabaseUser.AllowAi</c>); this is that switch for the
    /// connection that has no row.</para>
    /// </summary>
    public const string AiDefaultConnection = "ai.defaultConnection";

    public const bool AiDefaultConnectionDefault = false;

    /// <summary>
    /// Whether the AI assistant is shown a few example values from each text column of the
    /// tables it writes SQL for — it then knows a status is <c>'A'</c>, not <c>'Active'</c>.
    ///
    /// <para>Off by default (docs/ai.html#decisions, decision 3): without it the model sees
    /// table and column names, types and keys, never the data. The model runs on this server
    /// either way.</para>
    /// </summary>
    public const string AiSampleValues = "ai.sampleValues";

    public const bool AiSampleValuesDefault = false;

    /// <summary>
    /// The administrator's own rules for the AI, added to every request that writes, fixes or
    /// explains SQL — "deleted rows have STATUS = 'X'; leave them out", "money is in halalas".
    /// Jadwal's own rules (how parameters bind, what a query may contain) are always sent; these
    /// are the installation's. Empty by default.
    /// </summary>
    public const string AiInstructions = "ai.instructions";

    public const int AiInstructionsMaxLength = 4000;

    /// <summary>
    /// How many days of query execution logs to keep. Older entries are deleted by the rotation
    /// sweep, which runs every few hours and on demand from System Settings.
    ///
    /// <para>0 — the default — keeps them forever, which is how the log behaved before it could
    /// be rotated. Otherwise bounded at <see cref="LogRetentionDaysMin"/>–<see
    /// cref="LogRetentionDaysMax"/>: a week is the shortest window that still leaves time to
    /// notice a problem and look it up, and ten years is longer than any retention policy asks
    /// for.</para>
    /// </summary>
    public const string AuditExecutionLogRetentionDays = "audit.executionLogRetentionDays";

    /// <summary>
    /// How many days of the system audit trail (administrative actions) to keep. Same rules as
    /// <see cref="AuditExecutionLogRetentionDays"/>, set separately because the two answer
    /// different questions — and an audit policy commonly keeps configuration changes far
    /// longer than query runs.
    /// </summary>
    public const string AuditSystemLogRetentionDays = "audit.systemLogRetentionDays";

    /// <summary>Keep forever.</summary>
    public const int LogRetentionDaysDefault = 0;
    public const int LogRetentionDaysMin = 7;
    public const int LogRetentionDaysMax = 3650;

    /// <summary>True for 0 (keep forever) or a day count inside the bounds.</summary>
    public static bool IsValidRetention(int days) =>
        days == 0 || (days >= LogRetentionDaysMin && days <= LogRetentionDaysMax);

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
