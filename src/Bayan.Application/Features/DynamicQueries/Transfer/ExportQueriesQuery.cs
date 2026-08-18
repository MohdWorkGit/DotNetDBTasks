using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using Bayan.Domain.Services;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Transfer;

/// <summary>
/// Builds a portable snapshot of one query (<paramref name="QueryId"/> set) or of every query
/// (null) for backup or transfer to another system.
/// </summary>
/// <remarks>
/// Database *credentials* are never included. A query records which connection it runs against,
/// and that connection's password is encrypted at rest; writing it into a file an admin then
/// emails or commits to source control would undo that protection entirely. Only the connection
/// *name* is exported, and import re-matches it against connections already configured on the
/// target — an admin still has to have set that connection up, with its own credentials.
/// </remarks>
public record ExportQueriesQuery(Guid? QueryId) : IRequest<QueryExportFile>;

public class ExportQueriesQueryHandler : IRequestHandler<ExportQueriesQuery, QueryExportFile>
{
    private static readonly string[] Includes =
    {
        "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
        "Parameters", "DatabaseUser", "QueryGroup"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public ExportQueriesQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<QueryExportFile> Handle(
        ExportQueriesQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DynamicQuery> queries;
        if (request.QueryId is { } id)
        {
            var single = await _unitOfWork.DynamicQueries.GetByIdAsync(id, cancellationToken, Includes);
            if (single is null)
                throw new NotFoundException(nameof(DynamicQuery), id);
            queries = new[] { single };
        }
        else
        {
            queries = await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken, Includes);
        }

        // A parameter's dropdown source is stored as the id of another query; the file carries
        // that query's name instead, so it can be re-resolved on a system with different ids.
        var nameById = (await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken))
            .ToDictionary(q => q.Id, q => q.Name);

        return new QueryExportFile
        {
            FormatVersion = QueryExportFile.CurrentFormatVersion,
            ExportedAt = DateTime.UtcNow,
            ExportedBy = _currentUser.Username,
            Queries = queries.Select(q => ToExported(q, nameById)).ToList()
        };
    }

    private static ExportedQuery ToExported(DynamicQuery q, IReadOnlyDictionary<Guid, string> nameById) => new()
    {
        Name = q.Name,
        Description = q.Description,
        SqlQuery = q.SqlQuery,
        IsEnabled = q.IsEnabled,
        TimeoutSeconds = q.TimeoutSeconds,
        IsLongRunning = q.IsLongRunning,
        AllowRunWithoutConfirmation = q.AllowRunWithoutConfirmation,
        SaveOldValues = q.SaveOldValues,
        AllowedExportFormats = ExportPermissions.Parse(q.AllowedExportFormats)
            .Select(f => f.ToString()).ToList(),
        DatabaseUserName = q.DatabaseUser?.Name,
        QueryGroupName = q.QueryGroup?.Name,
        WordTemplateFileName = q.WordTemplateFileName,
        WordTemplateBase64 = q.WordTemplate is null ? null : Convert.ToBase64String(q.WordTemplate),
        Parameters = q.Parameters
            .OrderBy(p => p.SortOrder)
            .Select(p => new ExportedParameter
            {
                Name = p.Name,
                DisplayName = p.DisplayName,
                ParameterType = p.ParameterType,
                IsRequired = p.IsRequired,
                DefaultValue = p.DefaultValue,
                SortOrder = p.SortOrder,
                AllowMultiple = p.AllowMultiple,
                DropdownSourceType = p.DropdownSourceType,
                DropdownStaticValues = p.DropdownStaticValues,
                DropdownQueryName = p.DropdownQueryId is { } dq && nameById.TryGetValue(dq, out var n) ? n : null,
                DropdownQueryValueColumn = p.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = p.DropdownQueryLabelColumn
            })
            .ToList(),
        AssignedRoles = q.DynamicQueryRoles.Select(r => r.Role?.Name ?? string.Empty)
            .Where(n => n.Length > 0).ToList(),
        AssignedUserGroups = q.DynamicQueryUserGroups.Select(g => g.UserGroup?.Name ?? string.Empty)
            .Where(n => n.Length > 0).ToList(),
        AssignedUsers = q.DynamicQueryUsers.Select(u => u.User?.Username ?? string.Empty)
            .Where(n => n.Length > 0).ToList()
    };
}
