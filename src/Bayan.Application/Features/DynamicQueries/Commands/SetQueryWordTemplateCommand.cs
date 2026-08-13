using Bayan.Domain.Exceptions;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Stores (or replaces) the Word (.docx) export template of a dynamic query. The
/// template's placeholders — {{RESULTS}}, {{QUERY_NAME}}, {{GENERATED_AT}},
/// {{ROW_COUNT}} — are filled in when a user exports this query's results as Word.
/// </summary>
public record SetQueryWordTemplateCommand(Guid QueryId, string FileName, byte[] Content) : IRequest<Unit>;

/// <summary>Removes the Word export template so the built-in default layout is used again.</summary>
public record DeleteQueryWordTemplateCommand(Guid QueryId) : IRequest<Unit>;

public class SetQueryWordTemplateCommandHandler
    : IRequestHandler<SetQueryWordTemplateCommand, Unit>,
      IRequestHandler<DeleteQueryWordTemplateCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetQueryWordTemplateCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(SetQueryWordTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.QueryId);

        entity.WordTemplate = request.Content;
        entity.WordTemplateFileName = request.FileName;
        _unitOfWork.DynamicQueries.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(DeleteQueryWordTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.DynamicQueries.GetByIdAsync(request.QueryId, cancellationToken);
        if (entity is null)
            throw new NotFoundException(nameof(Domain.Entities.DynamicQuery), request.QueryId);

        entity.WordTemplate = null;
        entity.WordTemplateFileName = null;
        _unitOfWork.DynamicQueries.Update(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
