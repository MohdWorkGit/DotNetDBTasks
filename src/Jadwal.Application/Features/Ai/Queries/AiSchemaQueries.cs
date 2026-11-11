using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Security;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using MediatR;

namespace Jadwal.Application.Features.Ai.Queries;

// ---------------------------------------------------------------- connections

/// <summary>
/// The connections the AI may read for the caller: ones they may use that have "Allow AI"
/// switched on, and the default connection when System Settings allows it. Built here rather
/// than from the general connection list, which is not filtered by who is asking.
/// </summary>
public record GetAiConnectionsQuery : IRequest<List<AiConnectionDto>>;

public class GetAiConnectionsQueryHandler : IRequestHandler<GetAiConnectionsQuery, List<AiConnectionDto>>
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IQueryExecutor _queryExecutor;
    private readonly AiConnectionResolver _resolver;

    public GetAiConnectionsQueryHandler(
        IAiChatClient ai, ISystemSettingsService settings, IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        IQueryExecutor queryExecutor, AiConnectionResolver resolver)
    {
        _ai = ai;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _queryExecutor = queryExecutor;
        _resolver = resolver;
    }

    public async Task<List<AiConnectionDto>> Handle(GetAiConnectionsQuery request, CancellationToken cancellationToken)
    {
        var list = new List<AiConnectionDto>();
        if (!await AiAvailability.IsEnabledAsync(_ai, _settings, cancellationToken))
            return list;

        if (await _resolver.DefaultConnectionAllowedAsync(cancellationToken))
            list.Add(new AiConnectionDto { IsDefault = true, ServerType = _queryExecutor.DefaultServerType });

        // AllowAi is filtered here, not in SQL: it is a NUMBER(1) column, and the Oracle provider
        // cannot write a bool literal for one (Int16→Boolean cast while generating the WHERE).
        // There are a handful of connections, so nothing is lost.
        var active = await _unitOfWork.DatabaseUsers.FindAsync(d => d.IsActive, cancellationToken);
        foreach (var candidate in active.Where(d => d.AllowAi).OrderBy(d => d.Name))
        {
            try
            {
                // The same check a test run makes, so the list never offers what would be refused.
                await QueryAccess.EnsureCanUseDatabaseUserAsync(candidate.Id, _unitOfWork, _currentUser, cancellationToken);
                list.Add(new AiConnectionDto { Id = candidate.Id, Name = candidate.Name, ServerType = candidate.ServerType });
            }
            catch (Exception ex) when (ex is ForbiddenAccessException or DomainException or NotFoundException)
            {
                // Not this caller's to use.
            }
        }
        return list;
    }
}

// ---------------------------------------------------------------- tables and views

/// <summary>The tables and views a connection's account can read, from the cache unless refreshed.</summary>
public class GetAiSchemaQuery : IRequest<AiSchemaDto>
{
    public Guid? DatabaseUserId { get; set; }
    public bool Refresh { get; set; }
}

public class GetAiSchemaQueryHandler : IRequestHandler<GetAiSchemaQuery, AiSchemaDto>
{
    private readonly AiConnectionResolver _resolver;
    private readonly ISchemaReader _reader;
    private readonly ISchemaCache _cache;
    private readonly ISystemSettingsService _settings;

    public GetAiSchemaQueryHandler(
        AiConnectionResolver resolver, ISchemaReader reader, ISchemaCache cache, ISystemSettingsService settings)
    {
        _resolver = resolver;
        _reader = reader;
        _cache = cache;
        _settings = settings;
    }

    public async Task<AiSchemaDto> Handle(GetAiSchemaQuery request, CancellationToken cancellationToken)
    {
        var target = await _resolver.ResolveAsync(request.DatabaseUserId, cancellationToken);
        var objects = await _cache.GetOrAddAsync(
            request.DatabaseUserId ?? Guid.Empty,
            () => _reader.ListObjectsAsync(target, cancellationToken),
            request.Refresh);

        return new AiSchemaDto
        {
            Objects = objects.Select(o => new AiSchemaObjectDto
            {
                QualifiedName = o.QualifiedName,
                Schema = o.Schema,
                Name = o.Name,
                Kind = o.Kind
            }).ToList(),
            SampleValues = await _settings.GetBoolAsync(
                SystemSettingKeys.AiSampleValues, SystemSettingKeys.AiSampleValuesDefault, cancellationToken)
        };
    }
}
