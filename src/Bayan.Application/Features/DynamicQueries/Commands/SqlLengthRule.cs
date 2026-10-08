using Bayan.Application.Common.Interfaces;
using Bayan.Domain.Entities;
using FluentValidation;

namespace Bayan.Application.Features.DynamicQueries.Commands;

/// <summary>
/// The SQL length check shared by create and update. The limit is the administrator's
/// <see cref="SystemSettingKeys.QuerySqlMaxLength"/>, read on every save rather than fixed at
/// compile time, so a change on the settings page applies to the very next save.
///
/// <para>Async, so these validators must only ever run through <c>ValidateAsync</c> — which is
/// what the MediatR validation behavior does.</para>
/// </summary>
internal static class SqlLengthRule
{
    public static IRuleBuilderOptionsConditions<T, string> WithinConfiguredSqlLength<T>(
        this IRuleBuilder<T, string> rule,
        ISystemSettingsService settings,
        IAppLocalizer messages)
    {
        return rule.CustomAsync(async (sql, context, cancellationToken) =>
        {
            if (string.IsNullOrEmpty(sql))
                return;

            var max = await settings.GetIntAsync(
                SystemSettingKeys.QuerySqlMaxLength,
                SystemSettingKeys.QuerySqlMaxLengthDefault,
                cancellationToken);

            if (sql.Length > max)
                context.AddFailure(messages[MessageKeys.SqlTooLong, max, sql.Length]);
        });
    }
}
