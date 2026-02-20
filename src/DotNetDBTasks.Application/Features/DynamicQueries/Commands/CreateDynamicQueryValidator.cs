using System.Text.RegularExpressions;
using FluentValidation;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the CreateDynamicQueryCommand ensuring SQL safety and data integrity.
/// </summary>
public partial class CreateDynamicQueryValidator : AbstractValidator<CreateDynamicQueryCommand>
{
    /// <summary>
    /// Dangerous SQL patterns that indicate non-parameterized or destructive queries.
    /// </summary>
    private static readonly string[] ForbiddenPatterns = new[]
    {
        "INSERT ", "UPDATE ", "DELETE ", "DROP ", "ALTER ", "CREATE ",
        "TRUNCATE ", "EXEC ", "EXECUTE ", "xp_", "sp_", "--", ";",
        "GRANT ", "REVOKE ", "DENY "
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
            .Must(BeSelectOnly).WithMessage("Only SELECT queries are allowed.")
            .Must(NotContainDangerousPatterns).WithMessage("Query contains forbidden SQL patterns.");

        RuleFor(x => x.TimeoutSeconds)
            .InclusiveBetween(1, 120).WithMessage("Timeout must be between 1 and 120 seconds.");

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
        });
    }

    private static bool BeSelectOnly(string sql)
    {
        var trimmed = sql.Trim();
        return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NotContainDangerousPatterns(string sql)
    {
        var upper = sql.ToUpperInvariant();
        return !ForbiddenPatterns.Any(p => upper.Contains(p));
    }
}
