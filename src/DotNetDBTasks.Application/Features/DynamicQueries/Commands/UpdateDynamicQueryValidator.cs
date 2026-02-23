using FluentValidation;

namespace DotNetDBTasks.Application.Features.DynamicQueries.Commands;

/// <summary>
/// Validates the UpdateDynamicQueryCommand.
/// </summary>
public class UpdateDynamicQueryValidator : AbstractValidator<UpdateDynamicQueryCommand>
{
    private static readonly string[] ForbiddenPatterns = new[]
    {
        "xp_", "sp_", "--", ";", "DBMS_", "UTL_"
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
            .InclusiveBetween(1, 120);

        RuleForEach(x => x.Parameters).ChildRules(param =>
        {
            param.RuleFor(p => p.Name)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$");

            param.RuleFor(p => p.DisplayName)
                .NotEmpty().MaximumLength(200);
        });
    }

    private static bool NotContainDangerousPatterns(string sql) =>
        !ForbiddenPatterns.Any(p => sql.ToUpperInvariant().Contains(p));
}
