using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Branding;

/// <summary>
/// The stored logo. <paramref name="UpdatedAt"/> is what the client turns into a cache-busting
/// query string — without it the browser keeps serving the previous logo from cache after an
/// admin uploads a replacement, since the URL never changes.
/// </summary>
public record BrandingLogoDto(string FileName, byte[] Content, DateTime UpdatedAt);

/// <summary>Fetches the stored site logo. Returns null when none has been uploaded.</summary>
public record GetBrandingLogoQuery : IRequest<BrandingLogoDto?>;

public class GetBrandingLogoQueryHandler : IRequestHandler<GetBrandingLogoQuery, BrandingLogoDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBrandingLogoQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<BrandingLogoDto?> Handle(
        GetBrandingLogoQuery request,
        CancellationToken cancellationToken)
    {
        var stored = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == SystemTemplate.BrandingLogoKey, cancellationToken)).FirstOrDefault();

        return stored is null
            ? null
            : new BrandingLogoDto(stored.FileName, stored.Content, stored.UpdatedAt ?? stored.CreatedAt);
    }
}
