using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Features.Dashboards.Commands;
using Jadwal.Application.Features.Dashboards.Dtos;
using Jadwal.Application.Features.DynamicQueries.Transfer;
using Jadwal.Application.Features.Reports.Commands;
using Jadwal.Application.Features.Reports.Dtos;
using Jadwal.Domain.Constants;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using MediatR;
using ValidationException = Jadwal.Application.Common.Exceptions.ValidationException;

namespace Jadwal.Application.Features.Transfer;

/// <summary>
/// Restores queries, reports and dashboards from a transfer file, in that order, so each can
/// point at what came in before it. It follows the query import's rules throughout
/// (<see cref="ImportQueriesCommand"/>, which it calls for the queries):
/// <list type="bullet">
/// <item>Nothing that exists is modified or deleted: a name clash comes in as "Name (imported)",
/// so a mistaken import is undone by deleting what it added.</item>
/// <item>A name in the file resolves to what this same file brought in first — under its new
/// name, if it was renamed — and otherwise to what already exists here by that name.</item>
/// <item>What cannot be resolved becomes a warning, never a failed import: a missing role is not
/// granted, a tile whose query is missing is left out, a drill to a missing report is turned
/// off, a dropdown whose lookup query is missing becomes a text field. A report whose dataset
/// query is missing cannot run at all, so that report is skipped, with the reason.</item>
/// </list>
/// Each report and dashboard is saved through the same command the builder uses, so it is
/// validated exactly as a hand-built one would be; one that fails is skipped with the reason and
/// the rest still come in.
/// </summary>
public record ImportTransferCommand(TransferFile File) : IRequest<TransferImportResult>;

public class ImportTransferCommandHandler : IRequestHandler<ImportTransferCommand, TransferImportResult>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IAppLocalizer _localizer;
    private readonly IMediator _mediator;

    public ImportTransferCommandHandler(
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPermissionService permissions,
        IAppLocalizer localizer, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _permissions = permissions;
        _localizer = localizer;
        _mediator = mediator;
    }

    public async Task<TransferImportResult> Handle(ImportTransferCommand request, CancellationToken cancellationToken)
    {
        var file = request.File;
        if (file is not null)
            Normalize(file);
        if (file is null || file.Queries.Count + file.Reports.Count + file.Dashboards.Count == 0)
            throw new DomainException(_localizer[MessageKeys.TransferNoContent]);
        if (file.FormatVersion > TransferFile.CurrentFormatVersion)
            throw new DomainException(_localizer[MessageKeys.TransferNewerFormat,
                file.FormatVersion, TransferFile.CurrentFormatVersion]);

        // Each kind needs its own transfer permission; all are checked before anything is written.
        if (file.Queries.Count > 0) await RequireAsync(Permissions.QueriesTransfer, cancellationToken);
        if (file.Reports.Count > 0) await RequireAsync(Permissions.ReportsTransfer, cancellationToken);
        if (file.Dashboards.Count > 0) await RequireAsync(Permissions.DashboardsTransfer, cancellationToken);

        var result = new TransferImportResult();
        var context = await ImportContext.LoadAsync(_unitOfWork, cancellationToken);

        if (file.Queries.Count > 0)
        {
            var queries = await _mediator.Send(new ImportQueriesCommand(new QueryExportFile
            {
                FormatVersion = file.FormatVersion,
                ExportedAt = file.ExportedAt,
                ExportedBy = file.ExportedBy,
                Queries = file.Queries
            }), cancellationToken);

            result.Queries = queries.Queries;
            result.Warnings.AddRange(queries.Warnings);
            foreach (var q in queries.Queries)
                context.ImportedQueryNames.TryAdd(q.OriginalName, q.ImportedName);

            // Re-read, so the queries just added resolve by their (possibly new) names.
            await context.ReloadQueriesAsync(_unitOfWork, cancellationToken);
        }

        foreach (var report in file.Reports)
            await ImportReportAsync(report, context, result, cancellationToken);

        foreach (var dashboard in file.Dashboards)
            await ImportDashboardAsync(dashboard, context, result, cancellationToken);

        return result;
    }

    // ---------------------------------------------------------------- reports

    private async Task ImportReportAsync(
        ExportedReport exported, ImportContext context, TransferImportResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exported.Name))
        {
            result.Warnings.Add(_localizer[MessageKeys.TransferNoNameOrSql]);
            return;
        }

        var name = ImportNaming.UniqueName(exported.Name, context.ReportNames, out var wasRenamed);

        // A dataset without its query cannot run, and the others may join or repeat under it,
        // so a report missing one is skipped whole rather than imported broken.
        var datasets = new List<ReportDatasetInput>();
        foreach (var d in exported.Datasets)
        {
            Guid? queryId = null;
            if (d.SourceType != ReportDatasetSourceType.Join)
            {
                queryId = context.ResolveQuery(d.DynamicQueryName);
                if (queryId is null)
                {
                    result.Warnings.Add(_localizer[MessageKeys.TransferReportQueryMissing,
                        name, d.DatasetKey, d.DynamicQueryName ?? "—"]);
                    return;
                }
            }

            datasets.Add(new ReportDatasetInput
            {
                DatasetKey = d.DatasetKey,
                DisplayName = d.DisplayName,
                SourceType = d.SourceType,
                SortOrder = d.SortOrder,
                IsVisibleInViewer = d.IsVisibleInViewer,
                DynamicQueryId = queryId,
                ParentDatasetKey = d.ParentDatasetKey,
                LeftDatasetKey = d.LeftDatasetKey,
                RightDatasetKey = d.RightDatasetKey,
                JoinType = d.JoinType,
                LeftColumn = d.LeftColumn,
                RightColumn = d.RightColumn,
                ParameterMaps = d.ParameterMaps
            });
        }

        var input = new ReportInput
        {
            Name = name,
            Description = exported.Description,
            IsEnabled = exported.IsEnabled,
            QueryGroupId = await context.ResolveGroupAsync(exported.QueryGroupName, _unitOfWork, _currentUser, cancellationToken),
            TimeoutSeconds = exported.TimeoutSeconds,
            MaxDetailRows = exported.MaxDetailRows,
            MaxTotalRows = exported.MaxTotalRows,
            AllowedExportFormats = exported.AllowedExportFormats,
            Datasets = datasets,
            Parameters = exported.Parameters.Select(p => ToParameterInput(p, name, context, result)).ToList(),
            Charts = exported.Charts
        };

        ReportDto saved;
        try
        {
            saved = await _mediator.Send(new SaveReportCommand { Input = input }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException and not ForbiddenAccessException or ValidationException)
        {
            result.Warnings.Add(_localizer[MessageKeys.TransferItemSkipped, name, ex.Message]);
            return;
        }

        context.ReportNames.Add(name);
        context.ImportedReportIds.TryAdd(exported.Name, saved.Id);
        result.Reports.Add(new ImportedQueryResult { OriginalName = exported.Name, ImportedName = name, WasRenamed = wasRenamed });

        if (!string.IsNullOrWhiteSpace(exported.TemplateBase64))
        {
            try
            {
                await _mediator.Send(new SetReportTemplateCommand
                {
                    ReportId = saved.Id,
                    Content = Convert.FromBase64String(exported.TemplateBase64),
                    FileName = string.IsNullOrWhiteSpace(exported.TemplateFileName) ? "template.docx" : exported.TemplateFileName
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A bad template must not cost the report itself: it runs with the starter layout.
                result.Warnings.Add(_localizer[MessageKeys.TransferTemplateUnreadable, name]);
            }
        }

        var grants = ResolveGrants(exported, name, context, result);
        if (grants.Roles.Count + grants.Groups.Count + grants.Users.Count > 0)
        {
            await _mediator.Send(new SetReportAccessCommand
            {
                ReportId = saved.Id, RoleIds = grants.Roles, UserGroupIds = grants.Groups, UserIds = grants.Users
            }, cancellationToken);
        }
    }

    private ReportParameterInput ToParameterInput(
        ExportedParameter p, string owner, ImportContext context, TransferImportResult result)
    {
        var (type, source, queryId) = ResolveDropdown(p, owner, context, result);
        return new ReportParameterInput
        {
            Name = p.Name,
            DisplayName = p.DisplayName,
            ParameterType = type,
            IsRequired = p.IsRequired,
            DefaultValue = p.DefaultValue,
            SortOrder = p.SortOrder,
            AllowMultiple = type == ParameterType.Dropdown && p.AllowMultiple,
            DropdownSourceType = source,
            DropdownStaticValues = source is null ? null : p.DropdownStaticValues,
            DropdownQueryId = queryId,
            DropdownQueryValueColumn = queryId is null ? null : p.DropdownQueryValueColumn,
            DropdownQueryLabelColumn = queryId is null ? null : p.DropdownQueryLabelColumn
        };
    }

    // ---------------------------------------------------------------- dashboards

    private async Task ImportDashboardAsync(
        ExportedDashboard exported, ImportContext context, TransferImportResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exported.Name))
        {
            result.Warnings.Add(_localizer[MessageKeys.TransferNoNameOrSql]);
            return;
        }

        var name = ImportNaming.UniqueName(exported.Name, context.DashboardNames, out var wasRenamed);

        var filters = exported.Filters.Select(f =>
        {
            var (type, source, queryId) = ResolveDropdown(f, name, context, result);
            return new DashboardFilterInput
            {
                Name = f.Name,
                DisplayName = f.DisplayName,
                ParameterType = type,
                IsRequired = f.IsRequired,
                DefaultValue = f.DefaultValue,
                SortOrder = f.SortOrder,
                AllowMultiple = type == ParameterType.Dropdown && f.AllowMultiple,
                DropdownSourceType = source,
                DropdownStaticValues = source is null ? null : f.DropdownStaticValues,
                DropdownQueryId = queryId,
                DropdownQueryValueColumn = queryId is null ? null : f.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = queryId is null ? null : f.DropdownQueryLabelColumn
            };
        }).ToList();

        var tiles = new List<DashboardTileInput>();
        foreach (var t in exported.Tiles)
        {
            var queryId = context.ResolveQuery(t.DynamicQueryName);
            if (queryId is null)
            {
                result.Warnings.Add(_localizer[MessageKeys.TransferTileQueryMissing, name, t.Title, t.DynamicQueryName ?? "—"]);
                continue;
            }

            var drillAction = t.DrillAction;
            Guid? drillReportId = null;
            if (drillAction == DashboardDrillAction.OpenReport)
            {
                drillReportId = context.ResolveReport(t.DrillReportName);
                if (drillReportId is null)
                {
                    result.Warnings.Add(_localizer[MessageKeys.TransferDrillReportMissing, name, t.Title, t.DrillReportName ?? "—"]);
                    drillAction = DashboardDrillAction.None;
                }
            }

            tiles.Add(new DashboardTileInput
            {
                Title = t.Title,
                DynamicQueryId = queryId.Value,
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
                DrillAction = drillAction,
                DrillFilterName = drillAction == DashboardDrillAction.FilterDashboard ? t.DrillFilterName : null,
                DrillReportId = drillReportId,
                DrillReportParameter = drillReportId is null ? null : t.DrillReportParameter,
                ParameterMaps = t.ParameterMaps
            });
        }

        var input = new DashboardInput
        {
            Name = name,
            Description = exported.Description,
            IsEnabled = exported.IsEnabled,
            DefaultRefreshSeconds = exported.DefaultRefreshSeconds,
            SortOrder = exported.SortOrder,
            Filters = filters,
            Tiles = tiles
        };

        DashboardDto saved;
        try
        {
            saved = await _mediator.Send(new SaveDashboardCommand { Input = input }, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException and not ForbiddenAccessException or ValidationException)
        {
            result.Warnings.Add(_localizer[MessageKeys.TransferItemSkipped, name, ex.Message]);
            return;
        }

        context.DashboardNames.Add(name);
        result.Dashboards.Add(new ImportedQueryResult { OriginalName = exported.Name, ImportedName = name, WasRenamed = wasRenamed });

        var grants = ResolveGrants(exported, name, context, result);
        if (grants.Roles.Count + grants.Groups.Count + grants.Users.Count > 0)
        {
            await _mediator.Send(new SetDashboardAccessCommand
            {
                DashboardId = saved.Id, RoleIds = grants.Roles, UserGroupIds = grants.Groups, UserIds = grants.Users
            }, cancellationToken);
        }
    }

    // ---------------------------------------------------------------- shared

    /// <summary>
    /// A dropdown fed by a query keeps its source when that query resolves here. When it does
    /// not, the field becomes plain text — a dropdown with no options would block the form, and
    /// a free-text field still lets the report or dashboard run.
    /// </summary>
    private (ParameterType Type, DropdownSourceType? Source, Guid? QueryId) ResolveDropdown(
        ExportedParameter p, string owner, ImportContext context, TransferImportResult result)
    {
        if (p.ParameterType != ParameterType.Dropdown)
            return (p.ParameterType, null, null);
        if (p.DropdownSourceType != DropdownSourceType.Query)
            return (p.ParameterType, p.DropdownSourceType, null);

        var queryId = context.ResolveQuery(p.DropdownQueryName);
        if (queryId is not null)
            return (ParameterType.Dropdown, DropdownSourceType.Query, queryId);

        result.Warnings.Add(_localizer[MessageKeys.TransferDropdownToText,
            owner, string.IsNullOrWhiteSpace(p.DisplayName) ? p.Name : p.DisplayName, p.DropdownQueryName ?? "—"]);
        return (ParameterType.String, null, null);
    }

    private (List<Guid> Roles, List<Guid> Groups, List<Guid> Users) ResolveGrants(
        ExportedGrants exported, string owner, ImportContext context, TransferImportResult result)
    {
        var roles = new List<Guid>();
        foreach (var role in exported.AssignedRoles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (context.Roles.TryGetValue(role, out var id)) roles.Add(id);
            else result.Warnings.Add(_localizer[MessageKeys.TransferRoleMissing, owner, role]);
        }

        var groups = new List<Guid>();
        foreach (var group in exported.AssignedUserGroups.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (context.UserGroups.TryGetValue(group, out var id)) groups.Add(id);
            else result.Warnings.Add(_localizer[MessageKeys.TransferUserGroupMissing, owner, group]);
        }

        var users = new List<Guid>();
        foreach (var user in exported.AssignedUsers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (context.Users.TryGetValue(user, out var id)) users.Add(id);
            else result.Warnings.Add(_localizer[MessageKeys.TransferUserMissing, owner, user]);
        }

        return (roles, groups, users);
    }

    private async Task RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!await _permissions.HasAsync(_currentUser.Roles, permission, cancellationToken))
            throw new ForbiddenAccessException(_localizer[MessageKeys.TransferNeedsPermission, permission]);
    }

    /// <summary>
    /// An export never writes a null list, but a file edited by hand may say <c>"tiles": null</c>;
    /// that means "none", not a crash halfway through an import.
    /// </summary>
    private static void Normalize(TransferFile file)
    {
        file.Queries ??= new();
        file.Reports ??= new();
        file.Dashboards ??= new();
        file.Reports.RemoveAll(r => r is null);
        file.Dashboards.RemoveAll(d => d is null);

        foreach (var r in file.Reports)
        {
            r.Datasets ??= new();
            r.Parameters ??= new();
            r.Charts ??= new();
            r.AllowedExportFormats ??= new();
            foreach (var d in r.Datasets) d.ParameterMaps ??= new();
            foreach (var c in r.Charts) c.SeriesColumns ??= new();
            NormalizeGrants(r);
        }

        foreach (var d in file.Dashboards)
        {
            d.Filters ??= new();
            d.Tiles ??= new();
            foreach (var t in d.Tiles)
            {
                t.SeriesColumns ??= new();
                t.ConditionalRules ??= new();
                t.ParameterMaps ??= new();
            }
            NormalizeGrants(d);
        }
    }

    private static void NormalizeGrants(ExportedGrants grants)
    {
        grants.AssignedRoles ??= new();
        grants.AssignedUserGroups ??= new();
        grants.AssignedUsers ??= new();
    }

    /// <summary>Everything an import looks names up in, read once and kept current as items come in.</summary>
    private sealed class ImportContext
    {
        public Dictionary<string, string> ImportedQueryNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ImportedReportIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> QueryIds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> ReportIds { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReportNames { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DashboardNames { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> QueryGroups { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> Roles { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> UserGroups { get; private init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Guid> Users { get; private init; } = new(StringComparer.OrdinalIgnoreCase);

        public static async Task<ImportContext> LoadAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            var reports = await unitOfWork.Reports.GetAllAsync(cancellationToken);
            var context = new ImportContext
            {
                ReportIds = ByName(reports, r => r.Name, r => r.Id),
                ReportNames = reports.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                DashboardNames = (await unitOfWork.Dashboards.GetAllAsync(cancellationToken))
                    .Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                QueryGroups = ByName(await unitOfWork.QueryGroups.GetAllAsync(cancellationToken), g => g.Name, g => g.Id),
                Roles = ByName(await unitOfWork.Roles.GetAllAsync(cancellationToken), r => r.Name, r => r.Id),
                UserGroups = ByName(await unitOfWork.UserGroups.GetAllAsync(cancellationToken), g => g.Name, g => g.Id),
                Users = ByName(await unitOfWork.Users.GetAllAsync(cancellationToken), u => u.Username, u => u.Id)
            };
            await context.ReloadQueriesAsync(unitOfWork, cancellationToken);
            return context;
        }

        public async Task ReloadQueriesAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
            QueryIds = ByName(await unitOfWork.DynamicQueries.GetAllAsync(cancellationToken), q => q.Name, q => q.Id);

        /// <summary>This file's copy first (under its imported name), then one already here.</summary>
        public Guid? ResolveQuery(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (ImportedQueryNames.TryGetValue(name, out var imported) && QueryIds.TryGetValue(imported, out var copy))
                return copy;
            return QueryIds.TryGetValue(name, out var existing) ? existing : null;
        }

        public Guid? ResolveReport(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (ImportedReportIds.TryGetValue(name, out var imported))
                return imported;
            return ReportIds.TryGetValue(name, out var existing) ? existing : null;
        }

        /// <summary>
        /// Groups are just folders, so a missing one is created — as the query import does — to
        /// keep the imported report filed where it was. Saved at once, so a report that then
        /// fails validation does not leave it pending in the next save.
        /// </summary>
        public async Task<Guid?> ResolveGroupAsync(
            string? name, IUnitOfWork unitOfWork, ICurrentUserService currentUser, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (QueryGroups.TryGetValue(name, out var existing))
                return existing;

            var group = new QueryGroup
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = "Created by import.",
                CreatedByUserId = currentUser.UserId,
                CreatedAt = DateTime.UtcNow
            };
            await unitOfWork.QueryGroups.AddAsync(group, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            QueryGroups[name] = group.Id;
            return group.Id;
        }

        /// <summary>First by name wins when names repeat; names are not unique everywhere.</summary>
        private static Dictionary<string, Guid> ByName<T>(IEnumerable<T> items, Func<T, string> name, Func<T, Guid> id) =>
            items.GroupBy(name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => id(g.First()), StringComparer.OrdinalIgnoreCase);
    }
}
