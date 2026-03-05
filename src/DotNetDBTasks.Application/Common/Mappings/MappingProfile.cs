using System.Text.Json;
using AutoMapper;
using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryExecution.Queries;
using DotNetDBTasks.Domain.Entities;

namespace DotNetDBTasks.Application.Common.Mappings;

/// <summary>
/// AutoMapper profile defining all entity-to-DTO mappings.
/// </summary>
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<DynamicQuery, DynamicQueryDto>()
            .ForMember(d => d.AssignedRoles, opt => opt.MapFrom(s =>
                s.DynamicQueryRoles == null ? new List<RoleAssignmentDto>() :
                s.DynamicQueryRoles.Select(qr => new RoleAssignmentDto
                {
                    RoleId = qr.RoleId,
                    RoleName = qr.Role != null ? qr.Role.Name : string.Empty
                }).ToList()))
            .ForMember(d => d.AssignedDepartments, opt => opt.MapFrom(s =>
                s.DynamicQueryDepartments == null ? new List<DepartmentAssignmentDto>() :
                s.DynamicQueryDepartments.Select(qd => new DepartmentAssignmentDto
                {
                    Department = qd.Department
                }).ToList()))
            .ForMember(d => d.AssignedUsers, opt => opt.MapFrom(s =>
                s.DynamicQueryUsers == null ? new List<UserAssignmentDto>() :
                s.DynamicQueryUsers.Select(qu => new UserAssignmentDto
                {
                    UserId = qu.UserId,
                    Username = qu.User != null ? qu.User.Username : string.Empty
                }).ToList()));

        CreateMap<QueryParameter, QueryParameterDto>();
        CreateMap<QueryParameterDto, QueryParameter>();

        CreateMap<CreateDynamicQueryCommand, DynamicQuery>()
            .ForMember(d => d.Parameters, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryRoles, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryDepartments, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryUsers, opt => opt.Ignore());

        CreateMap<QueryExecutionLog, ExecutionLogDto>()
            .ForMember(d => d.QueryName, opt => opt.MapFrom(s => s.DynamicQuery != null ? s.DynamicQuery.Name : string.Empty))
            .ForMember(d => d.Username, opt => opt.MapFrom(s => s.User != null ? s.User.Username : string.Empty))
            .ForMember(d => d.Parameters, opt => opt.MapFrom<ParametersJsonResolver>())
            .ForMember(d => d.OldValues, opt => opt.MapFrom<OldValuesJsonResolver>())
            .ForMember(d => d.IsUpdateQuery, opt => opt.MapFrom(s =>
                s.DynamicQuery != null &&
                s.DynamicQuery.SqlQuery.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>
/// Deserializes ParametersJson (stored as JSON string) into a Dictionary for the DTO.
/// Extracted to a resolver because JsonSerializer.Deserialize has optional parameters
/// which cannot be used inside expression trees (CS0854).
/// </summary>
public class ParametersJsonResolver : IValueResolver<QueryExecutionLog, ExecutionLogDto, Dictionary<string, string>>
{
    public Dictionary<string, string> Resolve(
        QueryExecutionLog source,
        ExecutionLogDto destination,
        Dictionary<string, string> destMember,
        ResolutionContext context)
    {
        if (string.IsNullOrEmpty(source.ParametersJson))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(source.ParametersJson)
            ?? new Dictionary<string, string>();
    }
}

/// <summary>
/// Deserializes OldValuesJson (stored as JSON string) into a nullable Dictionary for the DTO.
/// </summary>
public class OldValuesJsonResolver : IValueResolver<QueryExecutionLog, ExecutionLogDto, Dictionary<string, string>?>
{
    public Dictionary<string, string>? Resolve(
        QueryExecutionLog source,
        ExecutionLogDto destination,
        Dictionary<string, string>? destMember,
        ResolutionContext context)
    {
        if (string.IsNullOrEmpty(source.OldValuesJson))
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, string>>(source.OldValuesJson);
    }
}
