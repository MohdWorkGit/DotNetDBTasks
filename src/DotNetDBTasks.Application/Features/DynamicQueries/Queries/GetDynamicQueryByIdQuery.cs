using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Common.Security;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Retrieves a single dynamic query by its identifier. Backs both the Admin edit form and
/// the accessibility page, so the SQL is blanked out for Access Managers.
/// </summary>
public record GetDynamicQueryByIdQuery(Guid Id) : IRequest<DynamicQueryDto>;

public class GetDynamicQueryByIdQueryHandler
    : IRequestHandler<GetDynamicQueryByIdQuery, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public GetDynamicQueryByIdQueryHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DynamicQueryDto> Handle(
        GetDynamicQueryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken,
            "DynamicQueryRoles.Role", "DynamicQueryDepartments", "DynamicQueryUsers.User",
            "Parameters", "DatabaseUser", "QueryGroup");
        if (query is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.Id);

        var dto = _mapper.Map<DynamicQueryDto>(query);
        QueryAccessRoles.RedactQueryTextFor(_currentUser, dto);
        return dto;
    }
}
