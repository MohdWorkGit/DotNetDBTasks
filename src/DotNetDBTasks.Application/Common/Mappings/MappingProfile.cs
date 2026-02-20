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
                s.DynamicQueryRoles.Select(qr => new RoleAssignmentDto
                {
                    RoleId = qr.RoleId,
                    RoleName = qr.Role.Name
                }).ToList()));

        CreateMap<QueryParameter, QueryParameterDto>();
        CreateMap<QueryParameterDto, QueryParameter>();

        CreateMap<CreateDynamicQueryCommand, DynamicQuery>()
            .ForMember(d => d.Parameters, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryRoles, opt => opt.Ignore());

        CreateMap<QueryExecutionLog, ExecutionLogDto>()
            .ForMember(d => d.QueryName, opt => opt.MapFrom(s => s.DynamicQuery.Name))
            .ForMember(d => d.Username, opt => opt.MapFrom(s => s.User.Username));
    }
}
