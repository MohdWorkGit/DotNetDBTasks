using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;

namespace DotNetDBTasks.Infrastructure.Services;

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
        var setting = (await _unitOfWork.SystemSettings.FindAsync(
            s => s.Key == key, cancellationToken)).FirstOrDefault();

        if (setting is null || string.IsNullOrWhiteSpace(setting.Value))
            return defaultValue;

        // An unparseable value falls back rather than throwing: a corrupt row must not take
        // the endpoint down, and for a permission toggle the safe reading is the default.
        return bool.TryParse(setting.Value, out var parsed) ? parsed : defaultValue;
    }

    public async Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
    {
        var setting = (await _unitOfWork.SystemSettings.FindAsync(
            s => s.Key == key, cancellationToken)).FirstOrDefault();

        var text = value ? "true" : "false";

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
