using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.ScheduledTasks.Queries;

/// <summary>
/// Downloads an export file produced by a scheduled task run. Requires the blanket
/// <c>scheduledTasks.download</c> permission, or a viewer grant carrying CanDownloadFiles — Auditors see the run history but not its files, and
/// plain viewers only see statuses and file names. The requested file name
/// must be one recorded in the run's item results — the file is then served from the
/// task's output folder (or the archive folder when the output copy is gone, e.g.
/// picked up by a downstream consumer).
/// </summary>
public record DownloadScheduledTaskRunFileQuery(Guid TaskId, Guid RunId, string FileName)
    : IRequest<ScheduledTaskRunFileDto>;

public class ScheduledTaskRunFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "application/octet-stream";
    public string FileName { get; set; } = string.Empty;
}

public class DownloadScheduledTaskRunFileQueryHandler
    : IRequestHandler<DownloadScheduledTaskRunFileQuery, ScheduledTaskRunFileDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public DownloadScheduledTaskRunFileQueryHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    public async Task<ScheduledTaskRunFileDto> Handle(
        DownloadScheduledTaskRunFileQuery request, CancellationToken cancellationToken)
    {
        var task = (await _unitOfWork.ScheduledTasks.FindAsync(
            t => t.Id == request.TaskId, cancellationToken,
            ScheduledTaskAccess.GrantIncludes)).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTask), request.TaskId);

        await ScheduledTaskAccess.EnsureCanDownloadFilesAsync(
            task, _unitOfWork, _currentUser, _permissions, cancellationToken);

        var run = (await _unitOfWork.ScheduledTaskRuns.FindAsync(
            r => r.Id == request.RunId && r.ScheduledTaskId == task.Id, cancellationToken)).FirstOrDefault()
            ?? throw new NotFoundException(nameof(ScheduledTaskRun), request.RunId);

        // Only file names recorded in this run's results may be served, and never a
        // path — this pins the download to the task's own folders (no traversal).
        var requested = Path.GetFileName(request.FileName ?? string.Empty);
        var recorded = ScheduledTaskMapper.ToRunDto(run).Items
            .Any(i => i.FileName is not null &&
                      string.Equals(i.FileName, requested, StringComparison.OrdinalIgnoreCase));
        if (!recorded)
            throw new NotFoundException($"This run has no export file named \"{requested}\".");

        // Prefer the output folder; fall back to the archive copy when the original
        // has been consumed or cleaned up.
        var path = new[] { task.OutputFolder, task.ArchiveFolder }
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => Path.Combine(folder!, requested))
            .FirstOrDefault(File.Exists)
            ?? throw new NotFoundException(
                $"The file \"{requested}\" no longer exists on the server. " +
                "It may have been moved or deleted, or overwritten by a newer run.");

        return new ScheduledTaskRunFileDto
        {
            Content = await File.ReadAllBytesAsync(path, cancellationToken),
            ContentType = GetContentType(requested),
            FileName = requested
        };
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
}
