using AutoMapper;
using DotNetDBTasks.Application.Features.QueryGroups.Queries;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.QueryGroups.Commands;

public class UpdateQueryGroupCommand : IRequest<QueryGroupDto>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class UpdateQueryGroupCommandHandler : IRequestHandler<UpdateQueryGroupCommand, QueryGroupDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateQueryGroupCommandHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<QueryGroupDto> Handle(UpdateQueryGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.QueryGroups.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.QueryGroup), request.Id);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.QueryGroups.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<QueryGroupDto>(entity);
    }
}
