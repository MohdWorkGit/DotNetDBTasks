using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves all enabled dynamic queries assigned to the current user's roles.
/// </summary>
public record GetQueriesForUserQuery : IRequest<IReadOnlyList<DynamicQueryDto>>;

public class GetQueriesForUserQueryHandler
    : IRequestHandler<GetQueriesForUserQuery, IReadOnlyList<DynamicQueryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetQueriesForUserQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DynamicQueryDto>> Handle(
        GetQueriesForUserQuery request,
        CancellationToken cancellationToken)
    {
        // Get user's role IDs
        var userRoles = await _unitOfWork.UserRoles.FindAsync(
            ur => ur.UserId == _currentUser.UserId, cancellationToken);
        var roleIds = userRoles.Select(ur => ur.RoleId).ToHashSet();

        // Get queries assigned to those roles that are enabled
        var queryRoles = await _unitOfWork.DynamicQueryRoles.FindAsync(
            qr => roleIds.Contains(qr.RoleId), cancellationToken);
        var queryIds = queryRoles.Select(qr => qr.DynamicQueryId).Distinct().ToHashSet();

        var queries = await _unitOfWork.DynamicQueries.FindAsync(
            q => queryIds.Contains(q.Id) && q.IsEnabled, cancellationToken);

        return _mapper.Map<IReadOnlyList<DynamicQueryDto>>(queries);
    }
}
