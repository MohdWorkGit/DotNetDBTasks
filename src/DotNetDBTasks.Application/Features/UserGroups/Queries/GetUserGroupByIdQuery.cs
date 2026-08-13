using AutoMapper;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.UserGroups.Queries;

public record GetUserGroupByIdQuery(Guid Id) : IRequest<UserGroupDto>;

public class GetUserGroupByIdQueryHandler : IRequestHandler<GetUserGroupByIdQuery, UserGroupDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public GetUserGroupByIdQueryHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<UserGroupDto> Handle(GetUserGroupByIdQuery request, CancellationToken cancellationToken)
    {
        var groups = await _unitOfWork.UserGroups.FindAsync(
            g => g.Id == request.Id, cancellationToken, "Members.User");
        var group = groups.FirstOrDefault();

        if (group is null)
            throw new NotFoundException(nameof(Domain.Entities.UserGroup), request.Id);

        return _mapper.Map<UserGroupDto>(group);
    }
}
