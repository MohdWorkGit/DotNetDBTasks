using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.UserGroups.Queries;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.UserGroups.Commands;

/// <summary>
/// Renames a user group or edits its description. Membership is changed separately —
/// see <see cref="SetUserGroupMembersCommand"/> — so the audit trail can tell a rename
/// apart from a change to who the group grants access to.
/// </summary>
public class UpdateUserGroupCommand : IRequest<UserGroupDto>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class UpdateUserGroupCommandHandler : IRequestHandler<UpdateUserGroupCommand, UserGroupDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly IAppLocalizer _messages;

    public UpdateUserGroupCommandHandler(IUnitOfWork unitOfWork, IMapper mapper, IAppLocalizer messages)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _messages = messages;
    }

    public async Task<UserGroupDto> Handle(UpdateUserGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.UserGroups.GetByIdAsync(request.Id, cancellationToken, "Members.User");
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.UserGroup), request.Id);

        var name = request.Name.Trim();

        if (await _unitOfWork.UserGroups.ExistsAsync(
                g => g.Name == name && g.Id != request.Id, cancellationToken))
            throw new DomainException(_messages[MessageKeys.UserGroupNameTaken, name]);

        entity.Name = name;
        entity.Description = request.Description;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.UserGroups.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<UserGroupDto>(entity);
    }
}
