using System.Text.Json;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Enums;
using DotNetDBTasks.Domain.Constants;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Returns the list of selectable options for a dropdown parameter.
/// Options are either read from a static JSON list or fetched at runtime
/// by executing the configured lookup query.
/// </summary>
public class GetParameterDropdownOptionsQuery : IRequest<List<DropdownOptionDto>>
{
    public Guid QueryId { get; set; }
    public Guid ParameterId { get; set; }
}

public class GetParameterDropdownOptionsQueryHandler
    : IRequestHandler<GetParameterDropdownOptionsQuery, List<DropdownOptionDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IQueryExecutor _queryExecutor;
    private readonly ICurrentUserService _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public GetParameterDropdownOptionsQueryHandler(
        IUnitOfWork unitOfWork,
        IQueryExecutor queryExecutor,
        ICurrentUserService currentUser,
        IEncryptionService encryption,
        IDatabaseConnectionFactory connectionFactory)
    {
        _unitOfWork = unitOfWork;
        _queryExecutor = queryExecutor;
        _currentUser = currentUser;
        _encryption = encryption;
        _connectionFactory = connectionFactory;
    }

    public async Task<List<DropdownOptionDto>> Handle(
        GetParameterDropdownOptionsQuery request,
        CancellationToken cancellationToken)
    {
        // Verify the query exists and is enabled
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException("DynamicQuery", request.QueryId);

        if (!query.IsEnabled)
            throw new DomainException("This query is currently disabled.");

        // Verify user access (same 4-tier check as execution)
        var hasAccess = false;

        var userRoleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => qr.DynamicQueryId == request.QueryId, cancellationToken);
        hasAccess = queryRoles.Any(qr => userRoleIds.Contains(qr.RoleId));

        if (!hasAccess && !string.IsNullOrEmpty(_currentUser.Department))
        {
            hasAccess = await _unitOfWork.DynamicQueryDepartments.ExistsAsync(
                qd => qd.DynamicQueryId == request.QueryId && qd.Department == _currentUser.Department,
                cancellationToken);
        }

        if (!hasAccess)
        {
            hasAccess = await _unitOfWork.DynamicQueryUsers.ExistsAsync(
                qu => qu.DynamicQueryId == request.QueryId && qu.UserId == _currentUser.UserId,
                cancellationToken);
        }

        // Group-level access — any assignment on the parent group grants access to this query.
        if (!hasAccess && query.QueryGroupId.HasValue)
        {
            var groupId = query.QueryGroupId.Value;

            hasAccess = userRoleIds.Count > 0 && await _unitOfWork.QueryGroupRoles.ExistsAsync(
                gr => gr.QueryGroupId == groupId && userRoleIds.Contains(gr.RoleId),
                cancellationToken);

            if (!hasAccess && !string.IsNullOrEmpty(_currentUser.Department))
            {
                hasAccess = await _unitOfWork.QueryGroupDepartments.ExistsAsync(
                    gd => gd.QueryGroupId == groupId && gd.Department == _currentUser.Department,
                    cancellationToken);
            }

            if (!hasAccess)
            {
                hasAccess = await _unitOfWork.QueryGroupUsers.ExistsAsync(
                    gu => gu.QueryGroupId == groupId && gu.UserId == _currentUser.UserId,
                    cancellationToken);
            }
        }

        if (!hasAccess && !_currentUser.Roles.Contains(RoleNames.Admin))
            throw new ForbiddenAccessException("You do not have access to this query.");

        // Load the specific parameter
        var parameters = await _unitOfWork.QueryParameters.FindAsync(
            p => p.DynamicQueryId == request.QueryId && p.Id == request.ParameterId,
            cancellationToken);

        var param = parameters.FirstOrDefault();
        if (param is null)
            throw new NotFoundException("QueryParameter", request.ParameterId);

        if (param.ParameterType != ParameterType.Dropdown)
            throw new DomainException("Parameter is not a dropdown type.");

        if (param.DropdownSourceType == DropdownSourceType.Static)
        {
            return DeserializeStaticOptions(param.DropdownStaticValues);
        }

        if (param.DropdownSourceType == DropdownSourceType.Query)
        {
            return await FetchQueryOptions(param, cancellationToken);
        }

        throw new DomainException("Dropdown source type is not configured.");
    }

    private static List<DropdownOptionDto> DeserializeStaticOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<DropdownOptionDto>();

        try
        {
            var options = JsonSerializer.Deserialize<List<DropdownOptionDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return options ?? new List<DropdownOptionDto>();
        }
        catch
        {
            throw new DomainException("Dropdown static values are not valid JSON.");
        }
    }

    private async Task<List<DropdownOptionDto>> FetchQueryOptions(
        Domain.Entities.QueryParameter param,
        CancellationToken cancellationToken)
    {
        if (param.DropdownQueryId is null)
            throw new DomainException("Dropdown lookup query is not configured.");

        var lookupQuery = await _unitOfWork.DynamicQueries.GetByIdAsync(
            param.DropdownQueryId.Value, cancellationToken);

        if (lookupQuery is null)
            throw new DomainException("The configured dropdown lookup query no longer exists.");

        if (!lookupQuery.IsEnabled)
            throw new DomainException("The configured dropdown lookup query is disabled.");

        var valueCol = param.DropdownQueryValueColumn!;
        var labelCol = param.DropdownQueryLabelColumn!;

        // Use the lookup query's database user if configured
        string? connectionString = null;
        Domain.Entities.DatabaseUser? dbUser = null;
        if (lookupQuery.DatabaseUserId.HasValue)
        {
            dbUser = await _unitOfWork.DatabaseUsers.GetByIdAsync(lookupQuery.DatabaseUserId.Value, cancellationToken);
            if (dbUser is { IsActive: true })
            {
                var password = _encryption.Decrypt(dbUser.EncryptedPassword);
                connectionString = _connectionFactory.BuildConnectionString(dbUser, password);
            }
        }

        var result = (connectionString != null && dbUser != null)
            ? await _queryExecutor.ExecuteAsync(
                lookupQuery.SqlQuery, new Dictionary<string, object?>(), lookupQuery.TimeoutSeconds, connectionString, dbUser.ServerType, cancellationToken)
            : await _queryExecutor.ExecuteAsync(
                lookupQuery.SqlQuery, new Dictionary<string, object?>(), lookupQuery.TimeoutSeconds, cancellationToken);

        var options = new List<DropdownOptionDto>();
        foreach (var row in result.Rows)
        {
            var value = row.TryGetValue(valueCol, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
            var label = row.TryGetValue(labelCol, out var l) ? Convert.ToString(l) ?? value : value;
            options.Add(new DropdownOptionDto { Value = value, Label = label });
        }

        return options;
    }
}
