namespace Jadwal.Application.Features.Transfer;

/// <summary>
/// The rule every import follows for a name that is already taken: never overwrite, come in as
/// "Name (imported)", then "(imported 2)" and so on — so a mistaken import is undone by deleting
/// what it added.
/// </summary>
public static class ImportNaming
{
    /// <summary>Queries, reports and dashboards all cap their name at 200 characters.</summary>
    public const int MaxNameLength = 200;

    public static string UniqueName(string desired, ISet<string> taken, out bool wasRenamed)
    {
        wasRenamed = false;
        desired = desired.Trim();
        if (desired.Length > MaxNameLength)
            desired = desired[..MaxNameLength];
        if (!taken.Contains(desired))
            return desired;

        wasRenamed = true;
        var suffix = 1;
        while (true)
        {
            var marker = suffix == 1 ? " (imported)" : $" (imported {suffix})";
            // Trim the base rather than the marker, so the copy is still recognisable.
            var stem = desired.Length + marker.Length > MaxNameLength
                ? desired[..(MaxNameLength - marker.Length)]
                : desired;
            var candidate = stem + marker;
            if (!taken.Contains(candidate))
                return candidate;
            suffix++;
        }
    }
}
