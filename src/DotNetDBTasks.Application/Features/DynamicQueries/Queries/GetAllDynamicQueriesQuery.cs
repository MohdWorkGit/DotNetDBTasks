using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all dynamic queries for anyone holding <c>queries.view</c>. The SQL is blanked
/// unless they also hold <c>queries.readSql</c> — picking a query to assign does not require
/// reading it.
/// </summary>
public record GetAllDynamicQueriesQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetAllDynamicQueriesQueryHandler
    : IRequestHandler<GetAllDynamicQueriesQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetAllDynamicQueriesQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser,
        IPermissionService permissions)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
        _permissions = permissions;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetAllDynamicQueriesQuery request,
        CancellationToken cancellationToken)
    {
        var queries = await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        var dtos = _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
        QueryAccessRoles.RedactQueryTextFor(
            await QueryAccessRoles.MayReadSqlAsync(_permissions, _currentUser, cancellationToken), dtos);
        return dtos;
    }
}
