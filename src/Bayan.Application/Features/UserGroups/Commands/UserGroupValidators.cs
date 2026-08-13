using FluentValidation;

namespace Bayan.Application.Features.UserGroups.Commands;

public class CreateUserGroupValidator : AbstractValidator<CreateUserGroupCommand>
{
    public CreateUserGroupValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Group name is required.")
            .MaximumLength(200).WithMessage("Group name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
    }
}

public class UpdateUserGroupValidator : AbstractValidator<UpdateUserGroupCommand>
{
    public UpdateUserGroupValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Group name is required.")
            .MaximumLength(200).WithMessage("Group name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");
    }
}
