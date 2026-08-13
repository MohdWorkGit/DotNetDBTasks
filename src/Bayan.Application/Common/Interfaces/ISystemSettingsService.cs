namespace Bayan.Application.Common.Interfaces;

/// <summary>
/// Reads and writes the runtime toggles in <c>SystemSettings</c>.
///
/// <para>
/// Reads fall back to the compiled default when no row exists, so callers never have to
/// handle "unset" — an upgraded database behaves exactly like a fresh one until an
/// administrator changes something.
/// </para>
///
/// <para>
/// Only the two shapes the settings page actually uses. Values are stored as text, so adding
/// another type is a parse method here rather than a schema change.
/// </para>
/// </summary>
public interface ISystemSettingsService
{
    Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken = default);

    Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default);

    Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default);

    Task SetIntAsync(string key, int value, CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored text, or null when unset. Unlike the bool and int readers there is no
    /// compiled default: for the site name "nothing stored" is a real state the caller has to
    /// act on, because the banner then falls back to the translated application name.
    /// </summary>
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the text. Null or blank clears the setting, which reads back as unset — note
    /// that clearing removes the row rather than blanking it, because Oracle cannot hold an
    /// empty string in a NOT NULL column.
    /// </summary>
    Task SetStringAsync(string key, string? value, CancellationToken cancellationToken = default);
}
