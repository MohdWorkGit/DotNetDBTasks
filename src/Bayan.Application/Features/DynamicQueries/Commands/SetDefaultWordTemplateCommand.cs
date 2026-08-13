using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Stores (or replaces) the system-wide default Word export template — used whenever a
/// query has no template of its own. Uses the same placeholders as per-query templates.
/// </summary>
public record SetDefaultWordTemplateCommand(string FileName, byte[] Content) : IRequest<Unit>;

/// <summary>Removes the system default so exports fall back to the built-in starter layout.</summary>
public record DeleteDefaultWordTemplateCommand : IRequest<Unit>;

public class SetDefaultWordTemplateCommandHandler
    : IRequestHandler<SetDefaultWordTemplateCommand, Unit>,
      IRequestHandler<DeleteDefaultWordTemplateCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public SetDefaultWordTemplateCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(SetDefaultWordTemplateCommand request, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.WordDefaultKey, cancellationToken)).FirstOrDefault();

        if (existing is null)
        {
            await _unitOfWork.SystemTemplates.AddAsync(new SystemTemplate
            {
                Id = Guid.NewGuid(),
                Key = SystemTemplate.WordDefaultKey,
                FileName = request.FileName,
                Content = request.Content,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            existing.FileName = request.FileName;
            existing.Content = request.Content;
            existing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.SystemTemplates.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Unit> Handle(DeleteDefaultWordTemplateCommand request, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.WordDefaultKey, cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            _unitOfWork.SystemTemplates.Delete(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Unit.Value;
    }
}
