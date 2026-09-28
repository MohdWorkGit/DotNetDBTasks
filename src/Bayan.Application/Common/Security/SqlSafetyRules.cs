using System.Text.RegularExpressions;

namespace Bayan.Application.Common.Security;

/// <summary>
/// The forbidden-pattern check applied to a query's SQL when it is created or updated. One copy,
/// shared by both validators, so the two can no longer drift apart.
///
/// <para>
/// The system-procedure and package prefixes (<c>XP_</c>, <c>SP_</c>, <c>DBMS_</c>,
/// <c>UTL_</c>) only count at the start of an identifier. They used to match anywhere, which
/// rejected ordinary SQL such as <c>REGEXP_REPLACE</c> (re-g-e-<c>XP_</c>), <c>EXP_DATE</c> or
/// <c>RESP_CODE</c>. <c>xp_cmdshell</c>, <c>dbo.sp_executesql</c>, <c>[xp_x]</c> and
/// <c>SYS.DBMS_SQL</c> are still caught, since <c>.</c>, <c>[</c>, quotes and whitespace are all
/// word boundaries. Line comments and statement separators are still rejected anywhere.
/// </para>
/// </summary>
public static class SqlSafetyRules
{
    private static readonly Regex ForbiddenPattern = new(
        @"\b(XP_|SP_|DBMS_|UTL_)|--|;",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// True when the SQL contains a forbidden pattern and must not be saved.
    /// </summary>
    public static bool ContainsForbiddenPattern(string? sql) =>
        !string.IsNullOrEmpty(sql) && ForbiddenPattern.IsMatch(sql);
}
