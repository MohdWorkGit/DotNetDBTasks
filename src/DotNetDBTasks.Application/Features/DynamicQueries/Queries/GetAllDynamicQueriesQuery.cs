using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all dynamic queries, for Admins and Access Managers. The latter get the
/// list with the SQL blanked out — they pick queries to assign, they do not read them.
/// </summary>
public record GetAllDynamicQueriesQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetAllDynamicQueriesQueryHandler
    : IRequestHandler<GetAllDynamicQueriesQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetAllDynamicQueriesQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetAllDynamicQueriesQuery request,
        CancellationToken cancellationToken)
    {
        var queries = await _unitOfWork.DynamicQueries.GetAllAsync(cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");

        var dtos = _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
        QueryAccessRoles.RedactQueryTextFor(_currentUser, dtos);
        return dtos;
    }
}
