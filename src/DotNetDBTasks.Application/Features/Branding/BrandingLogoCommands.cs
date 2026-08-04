using DotNetDBTasks.Domain.Entities;
using DotNetDBTasks.Domain.Interfaces;
using MediatR;

namespace DotNetDBTasks.Application.Features.Branding;

/// <summary>
/// Stores (or replaces) the site logo shown in the top banner in place of the app name.
/// </summary>
public record SetBrandingLogoCommand(string FileName, byte[] Content) : IRequest<Unit>;

/// <summary>Removes the logo so the banner falls back to the app name.</summary>
public record DeleteBrandingLogoCommand : IRequest<Unit>;

public class BrandingLogoCommandHandler
    : IRequestHandler<SetBrandingLogoCommand, Unit>,
      IRequestHandler<DeleteBrandingLogoCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public BrandingLogoCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(SetBrandingLogoCommand request, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.BrandingLogoKey, cancellationToken)).FirstOrDefault();

        if (existing is null)
        {
            await _unitOfWork.SystemTemplates.AddAsync(new SystemTemplate
            {
                Id = Guid.NewGuid(),
                Key = SystemTemplate.BrandingLogoKey,
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

    public async Task<Unit> Handle(DeleteBrandingLogoCommand request, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.BrandingLogoKey, cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            _unitOfWork.SystemTemplates.Delete(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Unit.Value;
    }
}
