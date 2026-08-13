using AutoMapper;
using DotNetDBTasks.Application.Features.DynamicQueries.Commands;
using DotNetDBTasks.Application.Features.DynamicQueries.Queries;
using DotNetDBTasks.Application.Features.QueryGroups.Queries;
using DotNetDBTasks.Application.Features.UserGroups.Queries;
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
            .ForMember(d => d.AssignedUserGroups, opt => opt.MapFrom(s =>
                s.DynamicQueryUserGroups == null ? new List<UserGroupAssignmentDto>() :
                s.DynamicQueryUserGroups.Select(qg => new UserGroupAssignmentDto
                {
                    UserGroupId = qg.UserGroupId,
                    UserGroupName = qg.UserGroup != null ? qg.UserGroup.Name : string.Empty
                }).ToList()))
            .ForMember(d => d.AssignedUsers, opt => opt.MapFrom(s =>
                s.DynamicQueryUsers == null ? new List<UserAssignmentDto>() :
                s.DynamicQueryUsers.Select(qu => new UserAssignmentDto
                {
                    UserId = qu.UserId,
                    Username = qu.User != null ? qu.User.Username : string.Empty
                }).ToList()));

        CreateMap<UserGroup, UserGroupDto>()
            .ForMember(d => d.MemberCount, opt => opt.MapFrom(s =>
                s.Members == null ? 0 : s.Members.Count))
            .ForMember(d => d.Members, opt => opt.MapFrom(s =>
                s.Members == null ? new List<UserGroupMemberDto>() :
                s.Members.Select(m => new UserGroupMemberDto
                {
                    UserId = m.UserId,
                    Username = m.User != null ? m.User.Username : string.Empty,
                    FirstName = m.User != null ? m.User.FirstName : string.Empty,
                    LastName = m.User != null ? m.User.LastName : string.Empty,
                    IsActive = m.User != null && m.User.IsActive
                }).ToList()));

        CreateMap<QueryParameter, QueryParameterDto>();
        CreateMap<QueryParameterDto, QueryParameter>();

        CreateMap<CreateDynamicQueryCommand, DynamicQuery>()
            .ForMember(d => d.Parameters, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryRoles, opt => opt.Ignore())
            .ForMember(d => d.DynamicQueryUserGroups, opt => opt.Ignore())
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
            .ForMember(d => d.AssignedUserGroups, opt => opt.MapFrom(s =>
                s.QueryGroupUserGroups == null ? new List<UserGroupAssignmentDto>() :
                s.QueryGroupUserGroups.Select(gg => new UserGroupAssignmentDto
                {
                    UserGroupId = gg.UserGroupId,
                    UserGroupName = gg.UserGroup != null ? gg.UserGroup.Name : string.Empty
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
