using Bayan.Application.Common.Interfaces;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the CreateDynamicQueryCommand ensuring SQL safety and data integrity.
/// </summary>
public partial class CreateDynamicQueryValidator : AbstractValidator<CreateDynamicQueryCommand>
{
    public CreateDynamicQueryValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        // Every field rule is shared with update, so the two cannot drift apart.
        Include(new DynamicQueryFieldsValidator(settings, messages));
    }
}
