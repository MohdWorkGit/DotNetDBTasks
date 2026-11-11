using System.Text.RegularExpressions;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Common.Security;
using Jadwal.Application.Features.DynamicQueries.Commands;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Enums;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Jadwal.Application.Features.Ai.Commands;

/// <summary>
/// After a failed test run, asks the model for a corrected query. The suggestion is only shown,
/// never saved or run: the admin chooses to use it, tests it, and saves through the usual
/// validators. Before it is shown at all it must pass <see cref="AiSqlGuard"/>, so a fix to a
/// SELECT can only ever be a SELECT.
/// </summary>
public class SuggestSqlFixCommand : IRequest<AiSqlFixDto>
{
    /// <summary>How much of the error is passed on. Database errors are short; stack traces are not.</summary>
    public const int ErrorMessageMaxLength = 4000;

    public string SqlQuery { get; set; } = string.Empty;

    /// <summary>What the test run reported, as the test dialog showed it.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>The connection the query is set to use, only so the model knows the dialect.</summary>
    public Guid? DatabaseUserId { get; set; }
}

public class SuggestSqlFixValidator : AbstractValidator<SuggestSqlFixCommand>
{
    public SuggestSqlFixValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        RuleFor(x => x.SqlQuery)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.SqlRequired])
            .WithinConfiguredSqlLength(settings, messages);

        RuleFor(x => x.ErrorMessage)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.AiErrorRequired]);
    }
}

public class SuggestSqlFixCommandHandler : IRequestHandler<SuggestSqlFixCommand, AiSqlFixDto>
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IAppLocalizer _messages;

    public SuggestSqlFixCommandHandler(
        IAiChatClient ai, ISystemSettingsService settings, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, IQueryExecutor queryExecutor, IAppLocalizer messages)
    {
        _ai = ai;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _queryExecutor = queryExecutor;
        _messages = messages;
    }

    public async Task<AiSqlFixDto> Handle(SuggestSqlFixCommand request, CancellationToken cancellationToken)
    {
        await AiAvailability.EnsureEnabledAsync(_ai, _settings, _messages, cancellationToken);

        // The test run refuses these before running them, so there is no failure to fix.
        var kind = QueryTypeClassifier.FromSql(request.SqlQuery);
        if (kind == QueryType.Other)
            throw new DomainException(_messages[MessageKeys.QueryTestUnsupported]);

        var serverType = await AiSqlCommandSupport.ResolveServerTypeAsync(
            request.DatabaseUserId, _unitOfWork, _currentUser, _queryExecutor, cancellationToken);

        var error = request.ErrorMessage.Length > SuggestSqlFixCommand.ErrorMessageMaxLength
            ? request.ErrorMessage[..SuggestSqlFixCommand.ErrorMessageMaxLength]
            : request.ErrorMessage;

        var answer = await _ai.CompleteJsonAsync<AiSqlFixDto>(
            new AiChatRequest(
                AiPrompts.System(serverType, AiPrompts.FixTask),
                AiPrompts.FixUser(request.SqlQuery, error, kind),
                "sql_fix",
                AiPrompts.FixSchema),
            cancellationToken);

        // Told not to, a model still likes to end a statement with a semicolon; Jadwal refuses
        // one, and dropping it changes nothing the author would care about.
        var suggested = answer.FixedSql.Trim().TrimEnd(';').TrimEnd();
        answer.Explanation = answer.Explanation.Trim();
        answer.Changed = suggested.Length > 0 && Normalize(suggested) != Normalize(request.SqlQuery);

        if (!answer.Changed)
        {
            answer.FixedSql = request.SqlQuery;
            return answer;
        }

        var problem = AiSqlGuard.FindProblem(request.SqlQuery, suggested);
        if (problem is not null)
            throw new DomainException(_messages[MessageKeys.AiUnsafeSql, problem]);

        var max = await _settings.GetIntAsync(
            SystemSettingKeys.QuerySqlMaxLength, SystemSettingKeys.QuerySqlMaxLengthDefault, cancellationToken);
        if (suggested.Length > max)
            throw new DomainException(_messages[MessageKeys.SqlTooLong, max, suggested.Length]);

        answer.FixedSql = suggested;
        return answer;
    }

    private static string Normalize(string sql) => Whitespace.Replace(sql.Trim(), " ");
}
