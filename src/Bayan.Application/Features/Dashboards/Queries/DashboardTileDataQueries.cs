using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Reporting;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.Dashboards.Dtos;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Application.Features.QueryExecution.Commands;
using Bayan.Domain.Entities;
using Bayan.Domain.Enums;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Bayan.Application.Features.Dashboards.Queries;

/// <summary>A tile with everything needed to run it for this viewer, every check already passed.</summary>
public sealed record ResolvedDashboardTile(
    Dashboard Dashboard,
    DashboardTile Tile,
    DynamicQuery Query,
    Dictionary<string, string> Parameters);

/// <summary>
/// The checks and the parameter routing every tile request shares — the data poll and the
/// drill-to-rows click alike.
/// </summary>
public static class DashboardTileResolver
{
    /// <summary>
    /// Confirms the viewer may run the tile's query and use its database login (throwing
    /// <see cref="ForbiddenAccessException"/>, which the data poll turns into an error on that one
    /// tile), and routes the filter values onto the query's parameters.
    ///
    /// <para>The dashboard grant is the caller's to check first, with
    /// <see cref="DashboardAccess.EnsureCanViewAsync"/>: failing it is a 403 for the whole page,
    /// not a tile error, and the caller needs to tell the two apart.</para>
    /// </summary>
    public static async Task<ResolvedDashboardTile> ResolveAsync(
        Guid dashboardId,
        Guid tileId,
        IReadOnlyDictionary<string, string>? filterValues,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        var dashboard = await unitOfWork.Dashboards.GetByIdAsync(
            dashboardId, cancellationToken, "Tiles", "Tiles.ParameterMaps", "Filters")
            ?? throw new NotFoundException(nameof(Dashboard), dashboardId);

        if (!dashboard.IsEnabled)
            throw new DomainException("This dashboard is currently disabled.");

        var tile = dashboard.Tiles.FirstOrDefault(t => t.Id == tileId)
            ?? throw new NotFoundException(nameof(DashboardTile), tileId);

        var query = await unitOfWork.DynamicQueries.GetByIdAsync(tile.DynamicQueryId, cancellationToken)
            ?? throw new NotFoundException(nameof(DynamicQuery), tile.DynamicQueryId);

        if (!query.IsEnabled)
            throw new DomainException("This tile's query is currently disabled.");

        await QueryAccess.EnsureCanRunAsync(query, unitOfWork, currentUser, cancellationToken);
        if (query.DatabaseUserId is { } databaseUserId)
            await QueryAccess.EnsureCanUseDatabaseUserAsync(databaseUserId, unitOfWork, currentUser, cancellationToken);

        var values = ResolveFilterValues(dashboard, filterValues);
        return new ResolvedDashboardTile(dashboard, tile, query, BuildQueryParameters(dashboard, tile, values));
    }

    /// <summary>
    /// Fills in defaults and enforces required filters, reporting a missing value against the
    /// filter the viewer can see rather than a query parameter they have never heard of.
    /// </summary>
    private static Dictionary<string, string> ResolveFilterValues(
        Dashboard dashboard,
        IReadOnlyDictionary<string, string>? submitted)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filter in dashboard.Filters.OrderBy(f => f.SortOrder))
        {
            string? raw = null;
            submitted?.TryGetValue(filter.Name, out raw);

            // An emptied multi-select arrives as "[]", which is no choice at all, not a value.
            if (filter.AllowMultiple && raw?.Trim() == "[]")
                raw = null;

            if (string.IsNullOrWhiteSpace(raw))
                raw = filter.DefaultValue;

            if (filter.IsRequired && string.IsNullOrWhiteSpace(raw))
                throw new DomainException($"Choose a value for '{filter.DisplayName}'.");

            // A relative date (month_start, today-7) becomes today's meaning of it here, at the
            // last moment — so the cache key below, built from these values, rolls over with it.
            if (filter.ParameterType == ParameterType.Date && !string.IsNullOrWhiteSpace(raw))
                raw = DatePresets.Resolve(raw, DateTime.Today);

            if (!string.IsNullOrWhiteSpace(raw))
                values[filter.Name] = raw;
        }

        return values;
    }

    private static Dictionary<string, string> BuildQueryParameters(
        Dashboard dashboard,
        DashboardTile tile,
        IReadOnlyDictionary<string, string> filterValues)
    {
        var filterNames = dashboard.Filters.ToDictionary(f => f.Id, f => f.Name);
        var parameters = new Dictionary<string, string>();

        foreach (var map in tile.ParameterMaps)
        {
            string? value = map.SourceKind switch
            {
                DashboardTileParameterSource.Constant => map.ConstantValue,
                DashboardTileParameterSource.Filter =>
                    map.DashboardFilterId is { } id &&
                    filterNames.TryGetValue(id, out var name) &&
                    filterValues.TryGetValue(name, out var v)
                        ? v
                        : null,
                _ => null
            };

            // A parameter with no value is left out, so the query falls back to its own default.
            if (value is not null)
                parameters[map.TargetParameterName] = value;
        }

        return parameters;
    }

    /// <summary>
    /// The shared-cache key: the tile plus the exact values its query will be bound with. Two
    /// viewers whose filters differ only in ways the tile ignores share one entry, because only
    /// the mapped values are part of the key.
    /// </summary>
    public static string CacheKey(ResolvedDashboardTile resolved)
    {
        var canonical = JsonSerializer.Serialize(
            resolved.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ToList());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"{resolved.Tile.Id:N}:{hash}";
    }
}

// ---------------------------------------------------------------- tile data

/// <summary>
/// One tile's current data for one viewer. The dashboard grant and the tile's query grant are
/// checked on every call, before the shared cache is consulted, so a warm cache never hands a
/// result to someone who could not have produced it.
/// </summary>
public class GetDashboardTileDataQuery : IRequest<DashboardTileDataDto>
{
    public Guid DashboardId { get; set; }
    public Guid TileId { get; set; }
    public Dictionary<string, string>? Filters { get; set; }
}

public class GetDashboardTileDataQueryHandler : IRequestHandler<GetDashboardTileDataQuery, DashboardTileDataDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IMediator _mediator;
    private readonly IDashboardTileCache _cache;
    private readonly ILogger<GetDashboardTileDataQueryHandler> _logger;

    public GetDashboardTileDataQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IMediator mediator,
        IDashboardTileCache cache,
        ILogger<GetDashboardTileDataQueryHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _mediator = mediator;
        _cache = cache;
        _logger = logger;
    }

    public async Task<DashboardTileDataDto> Handle(GetDashboardTileDataQuery request, CancellationToken cancellationToken)
    {
        // Not being allowed to open the dashboard at all is a 403 for the whole page, so it
        // propagates. Everything below it is about this one tile, and becomes the tile's error.
        await DashboardAccess.EnsureCanViewAsync(request.DashboardId, _unitOfWork, _currentUser, cancellationToken);

        ResolvedDashboardTile resolved;
        try
        {
            resolved = await DashboardTileResolver.ResolveAsync(
                request.DashboardId, request.TileId, request.Filters, _unitOfWork, _currentUser, cancellationToken);
        }
        catch (Exception ex) when (ex is DomainException or ForbiddenAccessException)
        {
            // Per viewer, so never cached.
            var now = DateTime.UtcNow;
            return new DashboardTileDataDto
            {
                TileId = request.TileId,
                GeneratedAt = now,
                NextRefreshAt = now.Add(DashboardTileProducer.MaxErrorLifetime),
                Error = ex.Message
            };
        }

        var refresh = TimeSpan.FromSeconds(
            DashboardMapper.EffectiveRefreshSeconds(resolved.Dashboard, resolved.Tile));

        // The factory deliberately ignores this request's cancellation token: its result is shared,
        // and one viewer closing the tab must not fail the load everyone else is waiting on. The
        // query's own timeout still bounds it, and this request awaits it to the end, so the scope
        // it runs in outlives it.
        return await _cache.GetOrCreateAsync(
            request.DashboardId,
            DashboardTileResolver.CacheKey(resolved),
            () => DashboardTileProducer.ProduceAsync(_mediator, _logger, resolved, refresh, CancellationToken.None));
    }
}

/// <summary>
/// Runs one resolved tile and turns the result into what it draws. Shared by the live data poll
/// and the scheduled snapshot, so a tile in a PDF and the same tile on screen are built the same
/// way. Never throws for a tile-level failure: the reason is carried on the result instead.
/// </summary>
public static class DashboardTileProducer
{
    /// <summary>
    /// A failing tile is retried sooner than a healthy one refreshes, capped so a slow interval
    /// does not leave a transient error on screen for an hour.
    /// </summary>
    public static readonly TimeSpan MaxErrorLifetime = TimeSpan.FromSeconds(60);

    public static async Task<DashboardTileDataDto> ProduceAsync(
        IMediator mediator,
        ILogger logger,
        ResolvedDashboardTile resolved,
        TimeSpan refresh,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var data = new DashboardTileDataDto
        {
            TileId = resolved.Tile.Id,
            GeneratedAt = now,
            NextRefreshAt = now.Add(refresh)
        };

        try
        {
            var result = await mediator.Send(new ExecuteQueryCommand
            {
                QueryId = resolved.Query.Id,
                Parameters = resolved.Parameters
            }, cancellationToken);

            DashboardTileBuilder.Fill(data, resolved.Tile, result);
        }
        catch (Exception ex) when (ex is DomainException or ForbiddenAccessException or NotFoundException)
        {
            data.Error = ex.Message;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A database error's text can carry schema detail, so the tile gets a plain sentence
            // and the log gets the rest.
            logger.LogWarning(ex, "Dashboard tile {TileId} failed to run query {QueryId}",
                resolved.Tile.Id, resolved.Query.Id);
            data.Error = "The tile's query failed. The error has been logged.";
        }

        if (data.Error is not null && refresh > MaxErrorLifetime)
            data.NextRefreshAt = now.Add(MaxErrorLifetime);

        return data;
    }
}

// ---------------------------------------------------------------- drill to rows

/// <summary>
/// Runs a tile's query in full — for the rows dialog and for downloading a tile — with the same
/// checks and the same parameters the tile ran with, and returns the result for the controller to
/// file into the job store the grid pages and exports from. Not cached: the viewer asked for the
/// rows as of now.
///
/// <para>Open to every tile, whatever its drill action: it is the viewer's own grant on the tile's
/// own query, so it reaches nothing the viewer could not run anyway.</para>
/// </summary>
public class RunDashboardTileRowsQuery : IRequest<(ResolvedDashboardTile Tile, Common.Models.QueryExecutionResult Result)>
{
    public Guid DashboardId { get; set; }
    public Guid TileId { get; set; }
    public Dictionary<string, string>? Filters { get; set; }
}

public class RunDashboardTileRowsQueryHandler
    : IRequestHandler<RunDashboardTileRowsQuery, (ResolvedDashboardTile Tile, Common.Models.QueryExecutionResult Result)>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IMediator _mediator;

    public RunDashboardTileRowsQueryHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUser, IMediator mediator)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _mediator = mediator;
    }

    public async Task<(ResolvedDashboardTile Tile, Common.Models.QueryExecutionResult Result)> Handle(
        RunDashboardTileRowsQuery request, CancellationToken cancellationToken)
    {
        await DashboardAccess.EnsureCanViewAsync(request.DashboardId, _unitOfWork, _currentUser, cancellationToken);

        var resolved = await DashboardTileResolver.ResolveAsync(
            request.DashboardId, request.TileId, request.Filters, _unitOfWork, _currentUser, cancellationToken);

        var result = await _mediator.Send(new ExecuteQueryCommand
        {
            QueryId = resolved.Query.Id,
            Parameters = resolved.Parameters,
            CacheFullResult = true
        }, cancellationToken);

        return (resolved, result);
    }
}

// ---------------------------------------------------------------- filter options

/// <summary>
/// The options of a dropdown filter. Gated on the dashboard grant, the way a query parameter's
/// options are gated on the query grant; a lookup query is the admin's choice of option list,
/// not a data source the viewer is running.
/// </summary>
public class GetDashboardFilterOptionsQuery : IRequest<List<DropdownOptionDto>>
{
    public Guid DashboardId { get; set; }
    public Guid FilterId { get; set; }
}

public class GetDashboardFilterOptionsQueryHandler
    : IRequestHandler<GetDashboardFilterOptionsQuery, List<DropdownOptionDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IEncryptionService _encryption;
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public GetDashboardFilterOptionsQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IQueryExecutor queryExecutor,
        IEncryptionService encryption,
        IDatabaseConnectionFactory connectionFactory)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _queryExecutor = queryExecutor;
        _encryption = encryption;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<DropdownOptionDto>> Handle(
        GetDashboardFilterOptionsQuery request, CancellationToken cancellationToken)
    {
        await DashboardAccess.EnsureCanViewAsync(request.DashboardId, _unitOfWork, _currentUser, cancellationToken);

        var filter = (await _unitOfWork.DashboardFilters.FindAsync(
            f => f.Id == request.FilterId && f.DashboardId == request.DashboardId, cancellationToken))
            .FirstOrDefault()
            ?? throw new NotFoundException(nameof(DashboardFilter), request.FilterId);

        if (filter.ParameterType != ParameterType.Dropdown)
            throw new DomainException("This filter is not a dropdown.");

        if (filter.DropdownSourceType == DropdownSourceType.Static)
            return ParseStatic(filter.DropdownStaticValues);

        if (filter.DropdownSourceType != DropdownSourceType.Query || filter.DropdownQueryId is null)
            throw new DomainException("This filter's options are not configured.");

        var lookup = await _unitOfWork.DynamicQueries.GetByIdAsync(filter.DropdownQueryId.Value, cancellationToken)
            ?? throw new DomainException("The lookup query for this filter no longer exists.");

        if (!lookup.IsEnabled)
            throw new DomainException("The lookup query for this filter is disabled.");

        string? connectionString = null;
        DatabaseUser? dbUser = null;
        if (lookup.DatabaseUserId.HasValue)
        {
            dbUser = await _unitOfWork.DatabaseUsers.GetByIdAsync(lookup.DatabaseUserId.Value, cancellationToken);
            if (dbUser is { IsActive: true })
                connectionString = _connectionFactory.BuildConnectionString(dbUser, _encryption.Decrypt(dbUser.EncryptedPassword));
        }

        var result = connectionString is not null && dbUser is not null
            ? await _queryExecutor.ExecuteAsync(
                lookup.SqlQuery, new Dictionary<string, object?>(), lookup.TimeoutSeconds,
                connectionString, dbUser.ServerType, cancellationToken)
            : await _queryExecutor.ExecuteAsync(
                lookup.SqlQuery, new Dictionary<string, object?>(), lookup.TimeoutSeconds, cancellationToken);

        var valueColumn = filter.DropdownQueryValueColumn!;
        var labelColumn = filter.DropdownQueryLabelColumn ?? valueColumn;

        return result.Rows
            .Select(row =>
            {
                var value = row.TryGetValue(valueColumn, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
                var label = row.TryGetValue(labelColumn, out var l) ? Convert.ToString(l) ?? value : value;
                return new DropdownOptionDto { Value = value, Label = label };
            })
            .ToList();
    }

    private static List<DropdownOptionDto> ParseStatic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<DropdownOptionDto>();

        try
        {
            return JsonSerializer.Deserialize<List<DropdownOptionDto>>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (JsonException)
        {
            throw new DomainException("This filter's option list is not valid JSON.");
        }
    }
}
