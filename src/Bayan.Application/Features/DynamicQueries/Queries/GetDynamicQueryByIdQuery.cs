using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves a single dynamic query by its identifier. Backs both the Admin edit form and
/// the accessibility page, so the SQL is blanked out for anyone without queries.readSql.
/// </summary>
public record GetDynamicQueryByIdQuery(Guid Id) : IRequest<DynamicQueryDto>;

public class GetDynamicQueryByIdQueryHandler
    : IRequestHandler<GetDynamicQueryByIdQuery, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _permissions;

    public GetDynamicQueryByIdQueryHandler(
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

    public async Task<DynamicQueryDto> Handle(
        GetDynamicQueryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryUserGroups.UserGroup", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");
        if (query is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        var dto = _mapper.Map<DynamicQueryDto>(query);
        QueryAccessRoles.RedactQueryTextFor(
            await QueryAccessRoles.MayReadSqlAsync(_permissions, _currentUser, cancellationToken), dto);
        return dto;
    }
}
