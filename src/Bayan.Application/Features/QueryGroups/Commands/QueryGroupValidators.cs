using FluentValidation;

namespace Bayan.Application.Features.QueryGroups.Commands;

public class CreateQueryGroupValidator : AbstractValidator<CreateQueryGroupCommand>
{
    public CreateQueryGroupValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Group name is required.")
            .MaximumLength(200).WithMessage("Group name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
    }
}

public class UpdateQueryGroupValidator : AbstractValidator<UpdateQueryGroupCommand>
{
    public UpdateQueryGroupValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty().MaximumLength(1000);
    }
}
