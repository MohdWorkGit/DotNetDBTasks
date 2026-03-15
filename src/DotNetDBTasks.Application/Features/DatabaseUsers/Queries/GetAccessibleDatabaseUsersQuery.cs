using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DatabaseUsers.Queries;

/// <summary>
/// Returns the list of database users that the current user has been granted access to.
/// Admins see all active database users.
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
        if (_currentUser.Roles.Contains("Admin"))
        {
            var allActive = await _unitOfWork.DatabaseUsers.FindAsync(
                du => du.IsActive, cancellationToken);
            return allActive.Select(du => new DatabaseUserSummaryDto
            {
                Id = du.Id,
                Name = du.Name
            }).ToList();
        }

        var accessEntries = await _unitOfWork.UserDatabaseUserAccess.FindAsync(
            a => a.UserId == _currentUser.UserId, cancellationToken);

        var result = new List<DatabaseUserSummaryDto>();
        foreach (var entry in accessEntries)
        {
            var du = await _unitOfWork.DatabaseUsers.GetByIdAsync(entry.DatabaseUserId, cancellationToken);
            if (du is { IsActive: true })
            {
                result.Add(new DatabaseUserSummaryDto { Id = du.Id, Name = du.Name });
            }
        }

        return result;
    }
}
