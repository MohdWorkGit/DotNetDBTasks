using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using MediatR;

namespace Bayan.Application.Features.Branding;

/// <summary>
/// The configured site name in each language. A null member means nothing is stored for that
/// language and the banner should fall back to the translated application name.
/// </summary>
public record BrandingSiteNameDto(string? En, string? Ar);

/// <summary>Reads the site name shown in the banner. Never null; its members may be.</summary>
public record GetBrandingSiteNameQuery : IRequest<BrandingSiteNameDto>;

public class GetBrandingSiteNameQueryHandler
    : IRequestHandler<GetBrandingSiteNameQuery, BrandingSiteNameDto>
{
    private readonly ISystemSettingsService _settings;

    public GetBrandingSiteNameQueryHandler(ISystemSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<BrandingSiteNameDto> Handle(
        GetBrandingSiteNameQuery request,
        CancellationToken cancellationToken)
    {
        var en = await _settings.GetStringAsync(SystemSettingKeys.BrandingSiteNameEn, cancellationToken);
        var ar = await _settings.GetStringAsync(SystemSettingKeys.BrandingSiteNameAr, cancellationToken);
        return new BrandingSiteNameDto(en, ar);
    }
}
