using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Branding;

/// <summary>
/// Stores (or replaces) the site logo shown in the top banner in place of the app name.
/// </summary>
public record SetBrandingLogoCommand(string FileName, byte[] Content) : IRequest<Unit>;

/// <summary>Removes the logo so the banner falls back to the site name.</summary>
public record DeleteBrandingLogoCommand : IRequest<Unit>;

/// <summary>Stores (or replaces) the icon browsers show on the tab and in bookmarks.</summary>
public record SetBrandingFaviconCommand(string FileName, byte[] Content) : IRequest<Unit>;

/// <summary>Removes the tab icon so browsers fall back to their own default.</summary>
public record DeleteBrandingFaviconCommand : IRequest<Unit>;

/// <summary>
/// Writes both branding images to the same keyed store.
///
/// <para>The four commands stay distinct types even though two bodies would do, because
/// <c>AuditLoggingBehavior</c> derives the recorded action from the command's type name — a
/// shared "set a branding asset" command would file a replaced logo and a replaced tab icon
/// under one indistinguishable entry.</para>
/// </summary>
public class BrandingAssetCommandHandler
    : IRequestHandler<SetBrandingLogoCommand, Unit>,
      IRequestHandler<DeleteBrandingLogoCommand, Unit>,
      IRequestHandler<SetBrandingFaviconCommand, Unit>,
      IRequestHandler<DeleteBrandingFaviconCommand, Unit>
{
    private readonly IUnitOfWork _unitOfWork;

    public BrandingAssetCommandHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Unit> Handle(SetBrandingLogoCommand request, CancellationToken cancellationToken) =>
        StoreAsync(SystemTemplate.BrandingLogoKey, request.FileName, request.Content, cancellationToken);

    public Task<Unit> Handle(DeleteBrandingLogoCommand request, CancellationToken cancellationToken) =>
        RemoveAsync(SystemTemplate.BrandingLogoKey, cancellationToken);

    public Task<Unit> Handle(SetBrandingFaviconCommand request, CancellationToken cancellationToken) =>
        StoreAsync(SystemTemplate.BrandingFaviconKey, request.FileName, request.Content, cancellationToken);

    public Task<Unit> Handle(DeleteBrandingFaviconCommand request, CancellationToken cancellationToken) =>
        RemoveAsync(SystemTemplate.BrandingFaviconKey, cancellationToken);

    private async Task<Unit> StoreAsync(
        string key, string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == key, cancellationToken)).FirstOrDefault();

        if (existing is null)
        {
            await _unitOfWork.SystemTemplates.AddAsync(new SystemTemplate
            {
                Id = Guid.NewGuid(),
                Key = key,
                FileName = fileName,
                Content = content,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }
        else
        {
            existing.FileName = fileName;
            existing.Content = content;
            existing.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.SystemTemplates.Update(existing);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private async Task<Unit> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var existing = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == key, cancellationToken)).FirstOrDefault();
        if (existing is not null)
        {
            _unitOfWork.SystemTemplates.Delete(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Unit.Value;
    }
}
