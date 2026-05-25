using AutoMapper;
using DotNetDBTasks.Application.Common.Interfaces;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Creates a new dynamic query with its parameters. Admin-only.
/// </summary>
public class CreateDynamicQueryCommand : IRequest<DynamicQueryDto>
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public Guid? DatabaseUserId { get; set; }
    public Guid? QueryGroupId { get; set; }
    public List<QueryParameterDto> Parameters { get; set; } = new();
}

public class CreateDynamicQueryCommandHandler
    : IRequestHandler<CreateDynamicQueryCommand, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ICurrentUserService _currentUser;

    public CreateDynamicQueryCommandHandler(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ICurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<DynamicQueryDto> Handle(
        CreateDynamicQueryCommand request,
        CancellationToken cancellationToken)
    {
        var entity = _mapper.Map<DynamicQuery>(request);
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedByUserId = _currentUser.UserId;
        entity.IsEnabled = true;
        entity.DatabaseUserId = request.DatabaseUserId;
        entity.QueryGroupId = request.QueryGroupId;

        foreach (var paramDto in request.Parameters)
        {
            entity.Parameters.Add(new QueryParameter
            {
                Id = Guid.NewGuid(),
                DynamicQueryId = entity.Id,
                Name = paramDto.Name,
                DisplayName = paramDto.DisplayName,
                ParameterType = paramDto.ParameterType,
                IsRequired = paramDto.IsRequired,
                DefaultValue = paramDto.DefaultValue,
                SortOrder = paramDto.SortOrder,
                AllowMultiple = paramDto.AllowMultiple,
                DropdownSourceType = paramDto.DropdownSourceType,
                DropdownStaticValues = paramDto.DropdownStaticValues,
                DropdownQueryId = paramDto.DropdownQueryId,
                DropdownQueryValueColumn = paramDto.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = paramDto.DropdownQueryLabelColumn,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _unitOfWork.DynamicQueries.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<DynamicQueryDto>(entity);
    }
}
