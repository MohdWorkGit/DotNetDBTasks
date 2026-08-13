using AutoMapper;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.UserGroups.Queries;
using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.UserGroups.Commands;

/// <summary>
/// Creates a user group, optionally with its starting membership.
/// </summary>
public class CreateUserGroupCommand : IRequest<UserGroupDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Guid> MemberUserIds { get; set; } = new();
}

public class CreateUserGroupCommandHandler : IRequestHandler<CreateUserGroupCommand, UserGroupDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;
    private readonly IAppLocalizer _messages;
    private readonly AdminAccountGuard _adminGuard;

    public CreateUserGroupCommandHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser,
        IAppLocalizer messages,
        AdminAccountGuard adminGuard)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
        _messages = messages;
        _adminGuard = adminGuard;
    }

    public async Task<UserGroupDto> Handle(CreateUserGroupCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        // A new group has no members yet, so any self-inclusion here is a join.
        _adminGuard.EnsureNotJoiningGroup(request.MemberUserIds, new HashSet<Guid>());

        // The unique index would catch this too, but as a provider error nobody can act on.
        if (await _unitOfWork.UserGroups.ExistsAsync(g => g.Name == name, cancellationToken))
            throw new DomainException(_messages[MessageKeys.UserGroupNameTaken, name]);

        var entity = new UserGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = request.Description,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.UserGroups.AddAsync(entity, cancellationToken);

        foreach (var userId in request.MemberUserIds.Distinct())
        {
            await _unitOfWork.UserGroupMembers.AddAsync(
                new UserGroupMember { UserGroupId = entity.Id, UserId = userId }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<UserGroupDto>(entity);
    }
}
