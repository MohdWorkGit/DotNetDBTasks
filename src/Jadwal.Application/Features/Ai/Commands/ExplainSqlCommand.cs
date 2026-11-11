using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Features.DynamicQueries.Commands;
using Jadwal.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Jadwal.Application.Features.Ai.Commands;

/// <summary>
/// Explains the SQL an admin is editing, in plain words and the page's language. Nothing runs
/// against the database and nothing is stored; the request goes in the audit trail as
/// <c>ai.explainSql</c>.
/// </summary>
public class ExplainSqlCommand : IRequest<AiSqlExplanationDto>
{
    public string SqlQuery { get; set; } = string.Empty;

    /// <summary>The connection the query is set to use, only so the model knows the dialect.</summary>
    public Guid? DatabaseUserId { get; set; }
}

public class ExplainSqlValidator : AbstractValidator<ExplainSqlCommand>
{
    public ExplainSqlValidator(ISystemSettingsService settings, IAppLocalizer messages)
    {
        RuleFor(x => x.SqlQuery)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.SqlRequired])
            .WithinConfiguredSqlLength(settings, messages);
    }
}

public class ExplainSqlCommandHandler : IRequestHandler<ExplainSqlCommand, AiSqlExplanationDto>
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IQueryExecutor _queryExecutor;
    private readonly IAppLocalizer _messages;

    public ExplainSqlCommandHandler(
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

    public async Task<AiSqlExplanationDto> Handle(ExplainSqlCommand request, CancellationToken cancellationToken)
    {
        await AiAvailability.EnsureEnabledAsync(_ai, _settings, _messages, cancellationToken);

        var serverType = await AiSqlCommandSupport.ResolveServerTypeAsync(
            request.DatabaseUserId, _unitOfWork, _currentUser, _queryExecutor, cancellationToken);

        var answer = await _ai.CompleteJsonAsync<AiSqlExplanationDto>(
            new AiChatRequest(
                AiPrompts.System(serverType, AiPrompts.ExplainTask),
                AiPrompts.ExplainUser(request.SqlQuery),
                "sql_explanation",
                AiPrompts.ExplainSchema),
            cancellationToken);

        // Held to a schema, but a small model can still return blanks inside it.
        answer.Steps = answer.Steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        answer.Cautions = answer.Cautions.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        answer.Summary = answer.Summary.Trim();
        return answer;
    }
}
