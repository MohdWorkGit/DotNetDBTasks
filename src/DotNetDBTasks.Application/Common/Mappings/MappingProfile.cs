using AutoMapper;
using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryGroups.Queries;
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
            .ForMember(d => d.DatabaseUserName, opt => opt.MapFrom(s =>
                s.DatabaseUser != null ? s.DatabaseUser.Name : null))
            .ForMember(d => d.QueryGroupName, opt => opt.MapFrom(s =>
                s.QueryGroup != null ? s.QueryGroup.Name : null))
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
            .ForMember(d => d.DynamicQueryUsers, opt => opt.Ignore())
            .ForMember(d => d.DatabaseUser, opt => opt.Ignore())
            .ForMember(d => d.QueryGroup, opt => opt.Ignore());

        CreateMap<QueryGroup, QueryGroupDto>()
            .ForMember(d => d.QueryCount, opt => opt.MapFrom(s =>
                s.DynamicQueries == null ? 0 : s.DynamicQueries.Count))
            .ForMember(d => d.AssignedRoles, opt => opt.MapFrom(s =>
                s.QueryGroupRoles == null ? new List<RoleAssignmentDto>() :
                s.QueryGroupRoles.Select(gr => new RoleAssignmentDto
                {
                    RoleId = gr.RoleId,
                    RoleName = gr.Role != null ? gr.Role.Name : string.Empty
                }).ToList()))
            .ForMember(d => d.AssignedDepartments, opt => opt.MapFrom(s =>
                s.QueryGroupDepartments == null ? new List<DepartmentAssignmentDto>() :
                s.QueryGroupDepartments.Select(gd => new DepartmentAssignmentDto
                {
                    Department = gd.Department
                }).ToList()))
            .ForMember(d => d.AssignedUsers, opt => opt.MapFrom(s =>
                s.QueryGroupUsers == null ? new List<UserAssignmentDto>() :
                s.QueryGroupUsers.Select(gu => new UserAssignmentDto
                {
                    UserId = gu.UserId,
                    Username = gu.User != null ? gu.User.Username : string.Empty
                }).ToList()));

        // QueryExecutionLog -> ExecutionLogDto is intentionally not mapped here: log list
        // queries project directly in the database so the potentially huge OldValuesJson
        // column is never loaded (see ExecutionLogQueryHelper).
    }
}
