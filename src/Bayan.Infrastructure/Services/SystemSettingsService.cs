using System.Globalization;
using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;

namespace Bayan.Infrastructure.Services;

/// <summary>
/// Key/value settings backed by the <c>SystemSettings</c> table.
///
/// <para>
/// Not cached. These are read on authorization paths where a stale value would mean granting
/// access an administrator believes they just revoked, and the table holds a handful of rows
/// behind a unique index — the lookup is cheaper than the risk of serving a toggle that is
/// seconds out of date across instances.
/// </para>
/// </summary>
public class SystemSettingsService : ISystemSettingsService
{
    private readonly IUnitOfWork _unitOfWork;

    public SystemSettingsService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken = default)
    {
        var text = await ReadAsync(key, cancellationToken);
        if (text is null)
            return defaultValue;

        // An unparseable value falls back rather than throwing: a corrupt row must not take
        // the endpoint down, and for a permission toggle the safe reading is the default.
        return bool.TryParse(text, out var parsed) ? parsed : defaultValue;
    }

    public Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
        WriteAsync(key, value ? "true" : "false", cancellationToken);

    public async Task<int> GetIntAsync(string key, int defaultValue, CancellationToken cancellationToken = default)
    {
        var text = await ReadAsync(key, cancellationToken);
        if (text is null)
            return defaultValue;

        // Invariant culture both ways, so a row written on one server reads the same on
        // another whose locale formats numbers differently.
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    public Task SetIntAsync(string key, int value, CancellationToken cancellationToken = default) =>
        WriteAsync(key, value.ToString(CultureInfo.InvariantCulture), cancellationToken);

    /// <summary>The stored text, or null when there is no row or it is blank.</summary>
    private async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var setting = (await _unitOfWork.SystemSettings.FindAsync(
            s => s.Key == key, cancellationToken)).FirstOrDefault();

        return setting is null || string.IsNullOrWhiteSpace(setting.Value) ? null : setting.Value;
    }

    private async Task WriteAsync(string key, string text, CancellationToken cancellationToken)
    {
        var setting = (await _unitOfWork.SystemSettings.FindAsync(
            s => s.Key == key, cancellationToken)).FirstOrDefault();

        if (setting is null)
        {
            await _unitOfWork.SystemSettings.AddAsync(new SystemSetting
            {
                Id = Guid.NewGuid(),
                Key = key,
                Value = text,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            setting.Value = text;
            setting.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.SystemSettings.Update(setting);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
