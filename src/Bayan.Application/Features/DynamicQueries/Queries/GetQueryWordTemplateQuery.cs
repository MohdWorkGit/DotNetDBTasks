using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Queries;

/// <summary>The stored Word export template of a query; Content is the raw .docx bytes.</summary>
public record WordTemplateDto(string FileName, byte[] Content);

/// <summary>
/// Fetches a query's Word export template. Returns null when the query has no template
/// (callers fall back to the built-in default document layout).
/// </summary>
public record GetQueryWordTemplateQuery(Guid QueryId) : IRequest<WordTemplateDto?>;

public class GetQueryWordTemplateQueryHandler
    : IRequestHandler<GetQueryWordTemplateQuery, WordTemplateDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetQueryWordTemplateQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<WordTemplateDto?> Handle(
        GetQueryWordTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.QueryId);

        return entity.WordTemplate is null
            ? null
            : new WordTemplateDto(entity.WordTemplateFileName ?? "template.docx", entity.WordTemplate);
    }
}
