using Bayan.Application.Common.Interfaces;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the UpdateDynamicQueryCommand.
/// </summary>
public class UpdateDynamicQueryValidator : AbstractValidator<UpdateDynamicQueryCommand>
{
    public UpdateDynamicQueryValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        RuleFor(x => x.Id).NotEmpty();

        // Every field rule is shared with create, so the two cannot drift apart.
        Include(new DynamicQueryFieldsValidator(settings, messages));
    }
}
