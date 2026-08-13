using Bayan.Domain.Enums;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the CreateDynamicQueryCommand ensuring SQL safety and data integrity.
/// </summary>
public partial class CreateDynamicQueryValidator : AbstractValidator<CreateDynamicQueryCommand>
{
    /// <summary>
    /// Dangerous SQL patterns that could indicate injection attacks or unsafe operations.
    /// These are kept for security even though all query types are allowed.
    /// </summary>
    private static readonly string[] ForbiddenPatterns = new[]
    {
        "XP_", "SP_", "--", ";", "DBMS_", "UTL_"
    };

    public CreateDynamicQueryValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Query name is required.")
            .MaximumLength(200).WithMessage("Query name must not exceed 200 characters.");

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Description is required.")
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters.");

        RuleFor(x => x.SqlQuery)
            .NotEmpty().WithMessage("SQL query is required.")
            .MaximumLength(4000).WithMessage("SQL query must not exceed 4000 characters.")
            .Must(NotContainDangerousPatterns).WithMessage("Query contains forbidden SQL patterns.");

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThanOrEqualTo(0).WithMessage("Timeout must be 0 or greater (0 = no timeout).");

        RuleForEach(x => x.Parameters).ChildRules(param =>
        {
            param.RuleFor(p => p.Name)
                .NotEmpty().WithMessage("Parameter name is required.")
                .MaximumLength(100)
                .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
                .WithMessage("Parameter name must be a valid SQL parameter name.");

            param.RuleFor(p => p.DisplayName)
                .NotEmpty().WithMessage("Display name is required.")
                .MaximumLength(200);

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

    private static bool NotContainDangerousPatterns(string sql)
    {
        var upper = sql.ToUpperInvariant();
        return !ForbiddenPatterns.Any(p => upper.Contains(p));
    }
}
