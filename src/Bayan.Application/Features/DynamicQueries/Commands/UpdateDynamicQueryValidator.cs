using Bayan.Domain.Enums;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the UpdateDynamicQueryCommand.
/// </summary>
public class UpdateDynamicQueryValidator : AbstractValidator<UpdateDynamicQueryCommand>
{
    private static readonly string[] ForbiddenPatterns = new[]
    {
        "XP_", "SP_", "--", ";", "DBMS_", "UTL_"
    };

    public UpdateDynamicQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty().MaximumLength(1000);

        RuleFor(x => x.SqlQuery)
            .NotEmpty()
            .MaximumLength(4000)
            .Must(NotContainDangerousPatterns).WithMessage("Query contains forbidden SQL patterns.");

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThanOrEqualTo(0).WithMessage("Timeout must be 0 or greater (0 = no timeout).");

        RuleForEach(x => x.Parameters).ChildRules(param =>
        {
            param.RuleFor(p => p.Name)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

            param.RuleFor(p => p.DisplayName)
                .NotEmpty().MaximumLength(200);

            // Dropdown-specific validation (also covers the multi-select toggle).
            param.When(p => p.ParameterType == ParameterType.Dropdown, () =>
            {
                param.RuleFor(p => p.DropdownSourceType)
                    .NotNull().WithMessage("Dropdown source type is required for dropdown parameters.");

                param.When(p => p.DropdownSourceType == DropdownSourceType.Static, () =>
                {
                    param.RuleFor(p => p.DropdownStaticValues)
                        .NotEmpty().WithMessage("Static values are required when source type is Static.");
                });

                param.When(p => p.DropdownSourceType == DropdownSourceType.Query, () =>
                {
                    param.RuleFor(p => p.DropdownQueryId)
                        .NotNull().WithMessage("A lookup query must be selected when source type is Query.");

                    param.RuleFor(p => p.DropdownQueryValueColumn)
                        .NotEmpty().WithMessage("Value column name is required when source type is Query.")
                        .MaximumLength(100);

                    param.RuleFor(p => p.DropdownQueryLabelColumn)
                        .NotEmpty().WithMessage("Label column name is required when source type is Query.")
                        .MaximumLength(100);
                });
            });
        });
    }

    private static bool NotContainDangerousPatterns(string sql) =>
        !ForbiddenPatterns.Any(p => sql.ToUpperInvariant().Contains(p));
}
