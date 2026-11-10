using System.Text.Json;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Interfaces;

namespace Jadwal.Application.Features.SystemAudit.Rotation;

/// <summary>What one rotation removed, and the retention it applied (0 = that log is kept forever).</summary>
public class AuditRotationResult
{
    public int ExecutionLogRetentionDays { get; set; }
    public int SystemLogRetentionDays { get; set; }
    public int ExecutionLogsDeleted { get; set; }
    public int SystemLogsDeleted { get; set; }
}

/// <summary>
/// Deletes query execution logs and system audit entries older than their retention settings.
/// Run every few hours by <c>AuditLogRotationService</c>, and on demand from System Settings.
///
/// <para>A retention of 0 leaves that log alone, so an installation that never sets one keeps
/// everything, as before. Rows are removed with one DELETE per log rather than loaded first —
/// a year of execution logs, with their before-change snapshots, is far too much to read just
/// to throw away.</para>
///
/// <para>A rotation that removes anything records itself in the system audit trail — after the
/// delete, so the entry saying the trail was trimmed is never the thing trimmed.</para>
/// </summary>
public class AuditLogRotation
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISystemSettingsService _settings;
    private readonly IAuditLogger _audit;

    public AuditLogRotation(IUnitOfWork unitOfWork, ISystemSettingsService settings, IAuditLogger audit)
    {
        _unitOfWork = unitOfWork;
        _settings = settings;
        _audit = audit;
    }

    public async Task<AuditRotationResult> RotateAsync(CancellationToken cancellationToken = default)
    {
        var result = new AuditRotationResult
        {
            ExecutionLogRetentionDays = await ReadRetentionAsync(
                SystemSettingKeys.AuditExecutionLogRetentionDays, cancellationToken),
            SystemLogRetentionDays = await ReadRetentionAsync(
                SystemSettingKeys.AuditSystemLogRetentionDays, cancellationToken)
        };

        var now = DateTime.UtcNow;

        if (result.ExecutionLogRetentionDays > 0)
        {
            var cutoff = now.AddDays(-result.ExecutionLogRetentionDays);
            result.ExecutionLogsDeleted = await _unitOfWork.QueryExecutionLogs.DeleteWhereAsync(
                l => l.ExecutedAt < cutoff, cancellationToken);
        }

        if (result.SystemLogRetentionDays > 0)
        {
            var cutoff = now.AddDays(-result.SystemLogRetentionDays);
            result.SystemLogsDeleted = await _unitOfWork.SystemAuditLogs.DeleteWhereAsync(
                l => l.OccurredAt < cutoff, cancellationToken);
        }

        if (result.ExecutionLogsDeleted > 0 || result.SystemLogsDeleted > 0)
        {
            await _audit.RecordAsync(new AuditEntry
            {
                Action = AuditActions.LogsRotated,
                Category = AuditActions.CategorySettings,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    executionLogRetentionDays = result.ExecutionLogRetentionDays,
                    executionLogsDeleted = result.ExecutionLogsDeleted,
                    systemLogRetentionDays = result.SystemLogRetentionDays,
                    systemLogsDeleted = result.SystemLogsDeleted
                })
            }, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// A stored value outside the rules (edited in the table by hand) is treated as "keep
    /// forever": refusing to delete is the safe misreading of a retention setting.
    /// </summary>
    private async Task<int> ReadRetentionAsync(string key, CancellationToken cancellationToken)
    {
        var days = await _settings.GetIntAsync(key, SystemSettingKeys.LogRetentionDaysDefault, cancellationToken);
        return SystemSettingKeys.IsValidRetention(days) ? days : 0;
    }
}
