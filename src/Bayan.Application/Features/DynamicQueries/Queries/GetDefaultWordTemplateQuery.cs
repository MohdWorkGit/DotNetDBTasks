using Bayan.Domain.Entities;
using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Queries;

/// <summary>
/// Fetches the stored system-wide default Word export template. Returns null when none
/// is uploaded (callers then use the built-in starter layout).
/// </summary>
public record GetDefaultWordTemplateQuery : IRequest<WordTemplateDto?>;

/// <summary>
/// Resolves the template a Word export of the given query should use:
/// per-query template, else the system default, else null (built-in starter layout).
/// </summary>
public record GetEffectiveWordTemplateQuery(Guid QueryId) : IRequest<byte[]?>;

public class GetDefaultWordTemplateQueryHandler
    : IRequestHandler<GetDefaultWordTemplateQuery, WordTemplateDto?>,
      IRequestHandler<GetEffectiveWordTemplateQuery, byte[]?>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetDefaultWordTemplateQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<WordTemplateDto?> Handle(
        GetDefaultWordTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var stored = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.WordDefaultKey, cancellationToken)).FirstOrDefault();
        return stored is null ? null : new WordTemplateDto(stored.FileName, stored.Content);
    }

    public async Task<byte[]?> Handle(
        GetEffectiveWordTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var query = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (query is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.QueryId);
        if (query.WordTemplate is not null)
            return query.WordTemplate;

        var stored = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.WordDefaultKey, cancellationToken)).FirstOrDefault();
        return stored?.Content;
    }
}
