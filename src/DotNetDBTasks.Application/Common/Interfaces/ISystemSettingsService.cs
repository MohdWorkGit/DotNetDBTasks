namespace DotNetDBTasks.Application.Common.Interfaces;

/// <summary>
/// Reads and writes the runtime toggles in <c>SystemSettings</c>.
///
/// <para>
/// Reads fall back to the compiled default when no row exists, so callers never have to
/// handle "unset" — an upgraded database behaves exactly like a fresh one until an
/// administrator changes something.
/// </para>
/// </summary>
public interface ISystemSettingsService
{
    Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken = default);

    Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default);
}
