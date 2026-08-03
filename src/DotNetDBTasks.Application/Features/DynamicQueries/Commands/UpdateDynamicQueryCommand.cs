using AutoMapper;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Exceptions;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Updates an existing dynamic query including its parameters.
/// </summary>
public class UpdateDynamicQueryCommand : IRequest<DynamicQueryDto>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlQuery { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int TimeoutSeconds { get; set; }
    public bool IsLongRunning { get; set; }
    public bool AllowRunWithoutConfirmation { get; set; }
    public bool SaveOldValues { get; set; }
    public Guid? DatabaseUserId { get; set; }
    public Guid? QueryGroupId { get; set; }
    public List<QueryParameterDto> Parameters { get; set; } = new();
}

public class UpdateDynamicQueryCommandHandler
    : IRequestHandler<UpdateDynamicQueryCommand, DynamicQueryDto>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateDynamicQueryCommandHandler(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<DynamicQueryDto> Handle(
        UpdateDynamicQueryCommand request,
        CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DynamicQueries.GetByIdAsync(request.Id, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(DynamicQuery), request.Id);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.SqlQuery = request.SqlQuery;
        entity.IsEnabled = request.IsEnabled;
        entity.TimeoutSeconds = request.TimeoutSeconds;
        entity.IsLongRunning = request.IsLongRunning;
        entity.AllowRunWithoutConfirmation = request.AllowRunWithoutConfirmation;
        entity.SaveOldValues = request.SaveOldValues;
        entity.DatabaseUserId = request.DatabaseUserId;
        entity.QueryGroupId = request.QueryGroupId;
        entity.UpdatedAt = DateTime.UtcNow;

        // Remove old parameters
        var existingParams = await _unitOfWork.QueryParameters.FindAsync(
            p => p.DynamicQueryId == entity.Id, cancellationToken);
        foreach (var param in existingParams)
            _unitOfWork.QueryParameters.Delete(param);

        // Add new parameters
        foreach (var paramDto in request.Parameters)
        {
            await _unitOfWork.QueryParameters.AddAsync(new QueryParameter
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
            }, cancellationToken);
        }

        _unitOfWork.DynamicQueries.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<DynamicQueryDto>(entity);
    }
}
