using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using MediatR;

namespace Bayan.Application.Features.Branding;

/// <summary>
/// Sets the site name shown in the top banner, per language.
///
/// <para>Both languages are written on every call, so clearing one is expressed by sending it
/// blank rather than by omitting it — a partial update would make "leave this as it was" and
/// "clear this" indistinguishable on the wire.</para>
/// </summary>
public record SetBrandingSiteNameCommand(string? En, string? Ar) : IRequest<Unit>;

public class SetBrandingSiteNameCommandHandler : IRequestHandler<SetBrandingSiteNameCommand, Unit>
{
    private readonly ISystemSettingsService _settings;

    public SetBrandingSiteNameCommandHandler(ISystemSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<Unit> Handle(SetBrandingSiteNameCommand request, CancellationToken cancellationToken)
    {
        await _settings.SetStringAsync(SystemSettingKeys.BrandingSiteNameEn, request.En, cancellationToken);
        await _settings.SetStringAsync(SystemSettingKeys.BrandingSiteNameAr, request.Ar, cancellationToken);
        return Unit.Value;
    }
}
