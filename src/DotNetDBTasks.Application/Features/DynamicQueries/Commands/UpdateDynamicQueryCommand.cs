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

        // Snapshot existing parameters before modification
        var existingParams = await _unitOfWork.QueryParameters.FindAsync(
            p => p.DynamicQueryId == entity.Id, cancellationToken);
        var oldParamsByName = existingParams.ToDictionary(p => p.Name);

        var now = DateTime.UtcNow;

        // Detect parameter changes and record history
        await RecordParameterChanges(entity.Id, oldParamsByName, request.Parameters, now, cancellationToken);

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.SqlQuery = request.SqlQuery;
        entity.IsEnabled = request.IsEnabled;
        entity.TimeoutSeconds = request.TimeoutSeconds;
        entity.UpdatedAt = now;

        // Remove old parameters
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
                DropdownSourceType = paramDto.DropdownSourceType,
                DropdownStaticValues = paramDto.DropdownStaticValues,
                DropdownQueryId = paramDto.DropdownQueryId,
                DropdownQueryValueColumn = paramDto.DropdownQueryValueColumn,
                DropdownQueryLabelColumn = paramDto.DropdownQueryLabelColumn,
                CreatedAt = now
            }, cancellationToken);
        }

        _unitOfWork.DynamicQueries.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return _mapper.Map<DynamicQueryDto>(entity);
    }

    private async Task RecordParameterChanges(
        Guid queryId,
        Dictionary<string, QueryParameter> oldParamsByName,
        List<QueryParameterDto> newParams,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var newParamsByName = newParams.ToDictionary(p => p.Name);

        // Detect removed parameters
        foreach (var (name, oldParam) in oldParamsByName)
        {
            if (!newParamsByName.ContainsKey(name))
            {
                await _unitOfWork.ParameterChangeHistories.AddAsync(new ParameterChangeHistory
                {
                    Id = Guid.NewGuid(),
                    DynamicQueryId = queryId,
                    ParameterName = name,
                    ChangeType = "Removed",
                    FieldName = "Parameter",
                    OldValue = oldParam.DisplayName,
                    NewValue = null,
                    ChangedAt = now,
                    CreatedAt = now
                }, cancellationToken);
            }
        }

        // Detect added and modified parameters
        foreach (var (name, newParam) in newParamsByName)
        {
            if (!oldParamsByName.TryGetValue(name, out var oldParam))
            {
                // New parameter added
                await _unitOfWork.ParameterChangeHistories.AddAsync(new ParameterChangeHistory
                {
                    Id = Guid.NewGuid(),
                    DynamicQueryId = queryId,
                    ParameterName = name,
                    ChangeType = "Added",
                    FieldName = "Parameter",
                    OldValue = null,
                    NewValue = newParam.DisplayName,
                    ChangedAt = now,
                    CreatedAt = now
                }, cancellationToken);
            }
            else
            {
                // Existing parameter — check each field for changes
                await CompareAndRecord(queryId, name, "DisplayName",
                    oldParam.DisplayName, newParam.DisplayName, now, cancellationToken);

                await CompareAndRecord(queryId, name, "ParameterType",
                    oldParam.ParameterType.ToString(), newParam.ParameterType.ToString(), now, cancellationToken);

                await CompareAndRecord(queryId, name, "IsRequired",
                    oldParam.IsRequired.ToString(), newParam.IsRequired.ToString(), now, cancellationToken);

                await CompareAndRecord(queryId, name, "DefaultValue",
                    oldParam.DefaultValue, newParam.DefaultValue, now, cancellationToken);

                await CompareAndRecord(queryId, name, "SortOrder",
                    oldParam.SortOrder.ToString(), newParam.SortOrder.ToString(), now, cancellationToken);

                await CompareAndRecord(queryId, name, "DropdownSourceType",
                    oldParam.DropdownSourceType?.ToString(), newParam.DropdownSourceType?.ToString(), now, cancellationToken);

                await CompareAndRecord(queryId, name, "DropdownStaticValues",
                    oldParam.DropdownStaticValues, newParam.DropdownStaticValues, now, cancellationToken);

                await CompareAndRecord(queryId, name, "DropdownQueryId",
                    oldParam.DropdownQueryId?.ToString(), newParam.DropdownQueryId?.ToString(), now, cancellationToken);

                await CompareAndRecord(queryId, name, "DropdownQueryValueColumn",
                    oldParam.DropdownQueryValueColumn, newParam.DropdownQueryValueColumn, now, cancellationToken);

                await CompareAndRecord(queryId, name, "DropdownQueryLabelColumn",
                    oldParam.DropdownQueryLabelColumn, newParam.DropdownQueryLabelColumn, now, cancellationToken);
            }
        }
    }

    private async Task CompareAndRecord(
        Guid queryId, string parameterName, string fieldName,
        string? oldValue, string? newValue,
        DateTime now, CancellationToken cancellationToken)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;

        await _unitOfWork.ParameterChangeHistories.AddAsync(new ParameterChangeHistory
        {
            Id = Guid.NewGuid(),
            DynamicQueryId = queryId,
            ParameterName = parameterName,
            ChangeType = "Modified",
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = now,
            CreatedAt = now
        }, cancellationToken);
    }
}
