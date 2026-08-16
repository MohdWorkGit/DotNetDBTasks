using Bayan.Domain.Entities;
using Bayan.Domain.Interfaces;
using MediatR;

namespace Bayan.Application.Features.Branding;

/// <summary>
/// A stored branding image. <paramref name="UpdatedAt"/> is what the client turns into a
/// cache-busting query string — without it the browser keeps serving the previous image from
/// cache after an admin uploads a replacement, since the URL never changes.
/// </summary>
public record BrandingAssetDto(string FileName, byte[] Content, DateTime UpdatedAt);

/// <summary>Fetches the stored site logo. Returns null when none has been uploaded.</summary>
public record GetBrandingLogoQuery : IRequest<BrandingAssetDto?>;

/// <summary>Fetches the stored browser-tab icon. Returns null when none has been uploaded.</summary>
public record GetBrandingFaviconQuery : IRequest<BrandingAssetDto?>;

/// <summary>
/// Reads both branding images from the same keyed store.
///
/// <para>One handler for two queries rather than two near-identical ones: the images differ
/// only in which key they live under. They stay separate request types so each keeps its own
/// route and, for the writes, its own audit action.</para>
/// </summary>
public class GetBrandingAssetQueryHandler
    : IRequestHandler<GetBrandingLogoQuery, BrandingAssetDto?>,
      IRequestHandler<GetBrandingFaviconQuery, BrandingAssetDto?>
{
    private readonly IUnitOfWork _unitOfWork;

    public GetBrandingAssetQueryHandler(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<BrandingAssetDto?> Handle(GetBrandingLogoQuery request, CancellationToken cancellationToken) =>
        ReadAsync(SystemTemplate.BrandingLogoKey, cancellationToken);

    public Task<BrandingAssetDto?> Handle(GetBrandingFaviconQuery request, CancellationToken cancellationToken) =>
        ReadAsync(SystemTemplate.BrandingFaviconKey, cancellationToken);

    private async Task<BrandingAssetDto?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var stored = (await _unitOfWork.SystemTemplates.FindAsync(
            t => t.Key == key, cancellationToken)).FirstOrDefault();

        return stored is null
            ? null
            : new BrandingAssetDto(stored.FileName, stored.Content, stored.UpdatedAt ?? stored.CreatedAt);
    }
}
