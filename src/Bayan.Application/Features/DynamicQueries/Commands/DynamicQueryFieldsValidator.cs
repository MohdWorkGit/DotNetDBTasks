using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Security;
using Bayan.Application.Features.DynamicQueries.Queries;
using Bayan.Domain.Enums;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// What a query runs with: its SQL, timeout and parameter definitions. Saving checks these, and
/// so does a test run from the query editor, which has no name or description to check.
/// </summary>
public interface IQuerySqlFields
{
    string SqlQuery { get; }
    int TimeoutSeconds { get; }
    List<QueryParameterDto> Parameters { get; }
}

/// <summary>
/// The fields a query is saved with, shared by create and update so the two cannot disagree.
/// </summary>
public interface IDynamicQueryFields : IQuerySqlFields
{
    string Name { get; }
    string Description { get; }
}

/// <summary>
/// The rules for <see cref="IDynamicQueryFields"/>, included by both
/// <see cref="CreateDynamicQueryValidator"/> and <see cref="UpdateDynamicQueryValidator"/>.
///
/// <para>One copy on purpose: the two used to be written out separately and had drifted — create
/// carried English messages, update carried none and fell back to FluentValidation's generic
/// "'Name' is not in the correct format." Every message now comes from the Messages resources,
/// so the query form can show the server's reasons in the reader's language.</para>
///
/// <para>Property names in the failures stay as FluentValidation writes them
/// ("Parameters[1].Name"); the query form turns those into "Parameter 2 (status):".</para>
/// </summary>
public class DynamicQueryFieldsValidator : AbstractValidator<IDynamicQueryFields>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;

    public DynamicQueryFieldsValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.QueryNameRequired])
            .MaximumLength(NameMaxLength).WithMessage(_ => messages[MessageKeys.QueryNameTooLong, NameMaxLength]);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.QueryDescriptionRequired])
            .MaximumLength(DescriptionMaxLength)
            .WithMessage(_ => messages[MessageKeys.QueryDescriptionTooLong, DescriptionMaxLength]);

        // SQL, timeout and parameters: the part a test run checks too.
        Include(new QuerySqlFieldsValidator(settings, messages));
    }
}

/// <summary>
/// The rules for <see cref="IQuerySqlFields"/>: included by <see cref="DynamicQueryFieldsValidator"/>
/// for a save, and used on its own by a test run, so a test refuses exactly what a save would.
/// </summary>
public class QuerySqlFieldsValidator : AbstractValidator<IQuerySqlFields>
{
    public const int ParameterNameMaxLength = 100;
    public const int ParameterDisplayNameMaxLength = 200;
    public const int ColumnNameMaxLength = 100;

    public QuerySqlFieldsValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        RuleFor(x => x.SqlQuery)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.SqlRequired])
            .WithinConfiguredSqlLength(settings, messages)
            .Custom((sql, context) =>
            {
                // Say which rule was broken: "forbidden SQL patterns" left the author to guess,
                // and the two that catch people are ordinary habits — a trailing semicolon and
                // a -- comment.
                var found = SqlSafetyRules.FindForbiddenPattern(sql);
                if (found is null)
                    return;
                context.AddFailure(found switch
                {
                    "--" => messages[MessageKeys.SqlForbiddenComment],
                    ";" => messages[MessageKeys.SqlForbiddenSemicolon],
                    _ => messages[MessageKeys.SqlForbiddenSystemCall, found.ToUpperInvariant()]
                });
            });

        RuleFor(x => x.TimeoutSeconds)
            .GreaterThanOrEqualTo(0).WithMessage(_ => messages[MessageKeys.QueryTimeoutNegative]);

        RuleForEach(x => x.Parameters).ChildRules(param =>
        {
            param.RuleFor(p => p.Name)
                .NotEmpty().WithMessage(_ => messages[MessageKeys.ParameterNameRequired])
                .MaximumLength(ParameterNameMaxLength)
                .WithMessage(_ => messages[MessageKeys.ParameterNameTooLong, ParameterNameMaxLength])
                .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]*$")
                .WithMessage(_ => messages[MessageKeys.ParameterNameInvalid]);

            param.RuleFor(p => p.DisplayName)
                .NotEmpty().WithMessage(_ => messages[MessageKeys.ParameterDisplayNameRequired])
                .MaximumLength(ParameterDisplayNameMaxLength)
                .WithMessage(_ => messages[MessageKeys.ParameterDisplayNameTooLong, ParameterDisplayNameMaxLength]);

            // Dropdown-specific validation (also covers the multi-select toggle).
            param.When(p => p.ParameterType == ParameterType.Dropdown, () =>
            {
                param.RuleFor(p => p.DropdownSourceType)
                    .NotNull().WithMessage(_ => messages[MessageKeys.DropdownSourceRequired]);

                param.When(p => p.DropdownSourceType == DropdownSourceType.Static, () =>
                {
                    param.RuleFor(p => p.DropdownStaticValues)
                        .NotEmpty().WithMessage(_ => messages[MessageKeys.DropdownStaticValuesRequired]);
                });

                param.When(p => p.DropdownSourceType == DropdownSourceType.Query, () =>
                {
                    param.RuleFor(p => p.DropdownQueryId)
                        .NotNull().WithMessage(_ => messages[MessageKeys.DropdownLookupQueryRequired]);

                    param.RuleFor(p => p.DropdownQueryValueColumn)
                        .NotEmpty().WithMessage(_ => messages[MessageKeys.DropdownValueColumnRequired])
                        .MaximumLength(ColumnNameMaxLength)
                        .WithMessage(_ => messages[MessageKeys.DropdownColumnTooLong, ColumnNameMaxLength]);

                    param.RuleFor(p => p.DropdownQueryLabelColumn)
                        .NotEmpty().WithMessage(_ => messages[MessageKeys.DropdownLabelColumnRequired])
                        .MaximumLength(ColumnNameMaxLength)
                        .WithMessage(_ => messages[MessageKeys.DropdownColumnTooLong, ColumnNameMaxLength]);
                });
            });
        });
    }
}
