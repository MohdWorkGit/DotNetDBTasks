using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Constants;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DatabaseUsers.Queries;

/// <summary>
/// Returns the list of database users that the current user has been granted access to
/// via their assigned roles. Admins see all active database users.
/// </summary>
public class GetAccessibleDatabaseUsersQuery : IRequest<List<DatabaseUserSummaryDto>> { }

public class GetAccessibleDatabaseUsersQueryHandler
    : IRequestHandler<GetAccessibleDatabaseUsersQuery, List<DatabaseUserSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public GetAccessibleDatabaseUsersQueryHandler(
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<List<DatabaseUserSummaryDto>> Handle(
        GetAccessibleDatabaseUsersQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.Roles.Contains(RoleNames.Admin))
        {
            var allActive = await _unitOfWork.DatabaseUsers.FindAsync(
                du => du.IsActive, cancellationToken);
            return allActive.Select(du => new DatabaseUserSummaryDto
            {
                Id = du.Id,
                Name = du.Name
            }).ToList();
        }

        // Same granting-role set as query execution: a database user reached only through
        // Auditor or AccessManager is not reachable at all, since neither runs queries.
        var userRoleIds = await QueryAccessRoles.GrantingRoleIdsAsync(
            _unitOfWork, _currentUser.UserId, cancellationToken);

        if (userRoleIds.Count == 0)
            return new List<DatabaseUserSummaryDto>();

        var accessEntries = await _unitOfWork.DatabaseUserRoleAccess.FindAsync(
            a => userRoleIds.Contains(a.RoleId), cancellationToken);

        var dbUserIds = accessEntries.Select(a => a.DatabaseUserId).Distinct().ToList();

        var result = new List<DatabaseUserSummaryDto>();
        foreach (var dbUserId in dbUserIds)
        {
            var du = await _unitOfWork.DatabaseUsers.GetByIdAsync(dbUserId, cancellationToken);
            if (du is { IsActive: true })
            {
                result.Add(new DatabaseUserSummaryDto { Id = du.Id, Name = du.Name });
            }
        }

        return result;
    }
}
