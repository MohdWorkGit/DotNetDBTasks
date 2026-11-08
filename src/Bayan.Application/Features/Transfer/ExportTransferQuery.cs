using Bayan.Application.Common.Interfaces;
using Bayan.Application.Features.Dashboards;
using Bayan.Application.Features.DynamicQueries.Transfer;
using Bayan.Application.Features.Reports;
using Bayan.Domain.Constants;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Transfer;

/// <summary>
/// Builds a portable snapshot of one report or dashboard (<paramref name="Id"/> set) or of all
/// of them (null), for backup or for moving to another system.
/// </summary>
/// <param name="IncludeDependencies">
/// Also export what they need to work: every query a report or dashboard reads (datasets, tiles,
/// dropdowns), and for a dashboard the reports its tiles open on click. Import then brings those
/// in too, as copies when the names are taken — the same rule a query import follows. Without
/// it, import links to whatever of those names already exists on the target.
/// </param>
/// <remarks>
/// Including the queries puts their SQL in the file, so it needs <c>queries.transfer</c> as
/// well; including a dashboard's reports needs <c>reports.transfer</c>. Database credentials are
/// never exported — see <see cref="ExportQueriesQuery"/>.
/// </remarks>
public record ExportTransferQuery(TransferKind Kind, Guid? Id, bool IncludeDependencies) : IRequest<TransferFile>;

public class ExportTransferQueryHandler : IRequestHandler<ExportTransferQuery, TransferFile>
{
    private static readonly string[] QueryIncludes =
    {
        "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
        "Parameters", "DatabaseUser", "QueryGroup"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IAppLocalizer _localizer;

    public ExportTransferQueryHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IPermissionService permissions, IAppLocalizer localizer)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
        _localizer = localizer;
    }

    public async Task<TransferFile> Handle(ExportTransferQuery request, CancellationToken cancellationToken)
    {
        if (request.IncludeDependencies)
        {
            await RequireAsync(Permissions.QueriesTransfer, cancellationToken);
            if (request.Kind == TransferKind.Dashboard)
                await RequireAsync(Permissions.ReportsTransfer, cancellationToken);
        }

        var queryNames = (await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken))
            .ToDictionary(q => q.Id, q => q.Name);
        var reportNames = (await _unitOfWork.Reports.GetAllAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => r.Name);

        var file = new TransferFile
        {
            FormatVersion = TransferFile.CurrentFormatVersion,
            ExportedAt = DateTime.UtcNow,
            ExportedBy = _currentUser.Username
        };

        var neededQueries = new HashSet<Guid>();
        var neededReports = new HashSet<Guid>();

        if (request.Kind == TransferKind.Dashboard)
        {
            var dashboards = await LoadAsync(_unitOfWork.Dashboards, request.Id, cancellationToken,
                DashboardMapper.FullIncludes.Concat(new[]
                {
                    "DashboardRoles.Role", "DashboardUserGroups.UserGroup", "DashboardUsers.User"
                }).ToArray());

            foreach (var dashboard in dashboards.OrderBy(d => d.SortOrder).ThenBy(d => d.Name))
            {
                file.Dashboards.Add(ToExported(dashboard, queryNames, reportNames));
                foreach (var tile in dashboard.Tiles)
                {
                    neededQueries.Add(tile.DynamicQueryId);
                    if (tile.DrillReportId is { } report)
                        neededReports.Add(report);
                }
                foreach (var filter in dashboard.Filters)
                {
                    if (filter.DropdownQueryId is { } q)
                        neededQueries.Add(q);
                }
            }
        }

        // A dashboard's drill-through reports come along when dependencies are included.
        var reportIds = request.Kind == TransferKind.Report
            ? null
            : request.IncludeDependencies ? neededReports : new HashSet<Guid>();

        if (request.Kind == TransferKind.Report || reportIds!.Count > 0)
        {
            var reports = request.Kind == TransferKind.Report
                ? await LoadAsync(_unitOfWork.Reports, request.Id, cancellationToken, ReportIncludes)
                : await _unitOfWork.Reports.FindAsync(r => reportIds!.Contains(r.Id), cancellationToken, ReportIncludes);

            foreach (var report in reports.OrderBy(r => r.Name))
            {
                file.Reports.Add(ToExported(report, queryNames));
                foreach (var dataset in report.Datasets)
                {
                    if (dataset.DynamicQueryId is { } q)
                        neededQueries.Add(q);
                }
                foreach (var parameter in report.Parameters)
                {
                    if (parameter.DropdownQueryId is { } q)
                        neededQueries.Add(q);
                }
            }
        }

        if (request.IncludeDependencies && neededQueries.Count > 0)
        {
            var queries = (await _unitOfWork.DynamicQueries.FindAsync(
                q => neededQueries.Contains(q.Id), cancellationToken, QueryIncludes)).ToList();

            // A query's own dropdowns read other queries; bring those too, so the copies work.
            var extra = queries
                .SelectMany(q => q.Parameters)
                .Where(p => p.DropdownQueryId is { } id && !neededQueries.Contains(id))
                .Select(p => p.DropdownQueryId!.Value)
                .ToHashSet();
            if (extra.Count > 0)
            {
                queries.AddRange(await _unitOfWork.DynamicQueries.FindAsync(
                    q => extra.Contains(q.Id), cancellationToken, QueryIncludes));
            }

            file.Queries = queries
                .OrderBy(q => q.Name)
                .Select(q => ExportQueriesQueryHandler.ToExported(q, queryNames))
                .ToList();
        }

        return file;
    }

    private static readonly string[] ReportIncludes = ReportMapper.FullIncludes.Concat(new[]
    {
        "ReportRoles.Role", "ReportUserGroups.UserGroup", "ReportUsers.User"
    }).ToArray();

    private async Task RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await _permissions.HasAsync(_currentUser.Roles, permission, cancellationToken))
            throw new ForbiddenAccessException(_localizer[MessageKeys.TransferNeedsPermission, permission]);
    }

    private static async Task<IReadOnlyList<T>> LoadAsync<T>(
        IRepository<T> repository, Guid? id, CancellationToken cancellationToken, string[] includes)
        where T : BaseEntity
    {
        if (id is null)
            return await repository.GetAllAsync(cancellationToken, includes);

        var single = await repository.GetByIdAsync(id.Value, cancellationToken, includes)
            ?? throw new NotFoundException(typeof(T).Name, id.Value);
        return new[] { single };
    }

    private static ExportedReport ToExported(Report report, IReadOnlyDictionary<Guid, string> queryNames)
    {
        // The DTO has already turned dataset ids into keys; parameter maps still carry ids.
        var dto = ReportMapper.ToDto(report, heldPermissions: null);
        var parameterNames = report.Parameters.ToDictionary(p => p.Id, p => p.Name);

        var exported = new ExportedReport
        {
            Name = report.Name,
            Description = report.Description,
            IsEnabled = report.IsEnabled,
            QueryGroupName = report.QueryGroup?.Name,
            TimeoutSeconds = report.TimeoutSeconds,
            MaxDetailRows = report.MaxDetailRows,
            MaxTotalRows = report.MaxTotalRows,
            AllowedExportFormats = dto.AllowedExportFormats,
            TemplateFileName = report.TemplateFileName,
            TemplateBase64 = report.TemplateDocx is null ? null : Convert.ToBase64String(report.TemplateDocx),
            Datasets = dto.Datasets.Select(d => new ExportedReportDataset
            {
                DatasetKey = d.DatasetKey,
                DisplayName = d.DisplayName,
                SourceType = d.SourceType,
                SortOrder = d.SortOrder,
                IsVisibleInViewer = d.IsVisibleInViewer,
                DynamicQueryName = d.DynamicQueryId is { } q && queryNames.TryGetValue(q, out var n) ? n : null,
                ParentDatasetKey = d.ParentDatasetKey,
                LeftDatasetKey = d.LeftDatasetKey,
                RightDatasetKey = d.RightDatasetKey,
                JoinType = d.JoinType,
                LeftColumn = d.LeftColumn,
                RightColumn = d.RightColumn,
                ParameterMaps = d.ParameterMaps.Select(m => new Reports.Dtos.ReportParameterMapInput
                {
                    TargetParameterName = m.TargetParameterName,
                    SourceKind = m.SourceKind,
                    ReportParameterName = m.ReportParameterId is { } p && parameterNames.TryGetValue(p, out var pn) ? pn : null,
                    ConstantValue = m.ConstantValue,
                    ParentColumn = m.ParentColumn
                }).ToList()
            }).ToList(),
            Parameters = dto.Parameters.Select(p => new ExportedParameter
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
                DropdownQueryName = p.DropdownQueryId is { } q && queryNames.TryGetValue(q, out var n) ? n : null,
                DropdownQueryValueColumn = p.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = p.DropdownQueryLabelColumn
            }).ToList(),
            Charts = dto.Charts.Select(c => new Reports.Dtos.ReportChartInput
            {
                ChartKey = c.ChartKey,
                Title = c.Title,
                ChartType = c.ChartType,
                DatasetKey = c.DatasetKey ?? string.Empty,
                CategoryColumn = c.CategoryColumn,
                SeriesColumns = c.SeriesColumns,
                MaxCategories = c.MaxCategories,
                SortOrder = c.SortOrder
            }).ToList()
        };

        AddGrants(exported,
            report.ReportRoles.Select(r => r.Role?.Name),
            report.ReportUserGroups.Select(g => g.UserGroup?.Name),
            report.ReportUsers.Select(u => u.User?.Username));
        return exported;
    }

    private static ExportedDashboard ToExported(
        Dashboard dashboard,
        IReadOnlyDictionary<Guid, string> queryNames,
        IReadOnlyDictionary<Guid, string> reportNames)
    {
        // The DTO already names filters where the entity holds their ids.
        var dto = DashboardMapper.ToDto(dashboard);

        var exported = new ExportedDashboard
        {
            Name = dashboard.Name,
            Description = dashboard.Description,
            IsEnabled = dashboard.IsEnabled,
            DefaultRefreshSeconds = dashboard.DefaultRefreshSeconds,
            SortOrder = dashboard.SortOrder,
            Filters = dto.Filters.Select(f => new ExportedParameter
            {
                Name = f.Name,
                DisplayName = f.DisplayName,
                ParameterType = f.ParameterType,
                IsRequired = f.IsRequired,
                DefaultValue = f.DefaultValue,
                SortOrder = f.SortOrder,
                AllowMultiple = f.AllowMultiple,
                DropdownSourceType = f.DropdownSourceType,
                DropdownStaticValues = f.DropdownStaticValues,
                DropdownQueryName = f.DropdownQueryId is { } q && queryNames.TryGetValue(q, out var n) ? n : null,
                DropdownQueryValueColumn = f.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = f.DropdownQueryLabelColumn
            }).ToList(),
            Tiles = dto.Tiles.Select(t => new ExportedDashboardTile
            {
                Title = t.Title,
                DynamicQueryName = queryNames.TryGetValue(t.DynamicQueryId, out var n) ? n : null,
                SortOrder = t.SortOrder,
                Width = t.Width,
                Height = t.Height,
                VisualType = t.VisualType,
                CategoryColumn = t.CategoryColumn,
                SeriesColumns = t.SeriesColumns,
                MaxCategories = t.MaxCategories,
                ValueColumn = t.ValueColumn,
                CompareColumn = t.CompareColumn,
                KpiAggregate = t.KpiAggregate,
                ValueFormat = t.ValueFormat,
                TargetValue = t.TargetValue,
                TargetWarnPercent = t.TargetWarnPercent,
                ConditionalRules = t.ConditionalRules,
                DisplayOptions = t.DisplayOptions,
                HigherIsBetter = t.HigherIsBetter,
                RefreshSeconds = t.RefreshSeconds,
                DrillAction = t.DrillAction,
                DrillFilterName = t.DrillFilterName,
                DrillReportName = t.DrillReportId is { } r && reportNames.TryGetValue(r, out var rn) ? rn : null,
                DrillReportParameter = t.DrillReportParameter,
                ParameterMaps = t.ParameterMaps
            }).ToList()
        };

        AddGrants(exported,
            dashboard.DashboardRoles.Select(r => r.Role?.Name),
            dashboard.DashboardUserGroups.Select(g => g.UserGroup?.Name),
            dashboard.DashboardUsers.Select(u => u.User?.Username));
        return exported;
    }

    private static void AddGrants(
        ExportedGrants target, IEnumerable<string?> roles, IEnumerable<string?> groups, IEnumerable<string?> users)
    {
        target.AssignedRoles = roles.OfType<string>().Where(n => n.Length > 0).OrderBy(n => n).ToList();
        target.AssignedUserGroups = groups.OfType<string>().Where(n => n.Length > 0).OrderBy(n => n).ToList();
        target.AssignedUsers = users.OfType<string>().Where(n => n.Length > 0).OrderBy(n => n).ToList();
    }
}
