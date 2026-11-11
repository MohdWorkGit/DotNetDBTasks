using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Features.Reports.Commands;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Jadwal.Application.Features.Ai.Commands;

/// <summary>
/// Drafts a report from saved queries the admin picks: one section per query, charts on the ones
/// that are already summaries, and the parameters the queries need. As with a dashboard draft,
/// each query is run once to learn the shape of its result — names, kinds, counts, never values —
/// and <see cref="ReportDraftBuilder"/> checks the model's proposal against it. Nothing is saved.
/// </summary>
public class DraftReportCommand : IRequest<AiReportDraftDto>
{
    public List<Guid> QueryIds { get; set; } = new();

    /// <summary>What the report is for, in the admin's words. Optional.</summary>
    public string? Goal { get; set; }
}

public class DraftReportValidator : AbstractValidator<DraftReportCommand>
{
    public DraftReportValidator(IAppLocalizer messages)
    {
        // The same limits as a dashboard draft: they bound the same work.
        RuleFor(x => x.QueryIds)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.AiQueriesRequired])
            .Must(ids => ids.Distinct().Count() <= DraftDashboardCommand.MaxQueries)
            .WithMessage(_ => messages[MessageKeys.AiDraftTooManyQueries, DraftDashboardCommand.MaxQueries]);

        RuleFor(x => x.Goal)
            .MaximumLength(DraftDashboardCommand.GoalMaxLength)
            .WithMessage(_ => messages[MessageKeys.AiGoalTooLong, DraftDashboardCommand.GoalMaxLength]);
    }
}

public class DraftReportCommandHandler : IRequestHandler<DraftReportCommand, AiReportDraftDto>
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly IAppLocalizer _messages;

    public DraftReportCommandHandler(
        IAiChatClient ai, ISystemSettingsService settings, IUnitOfWork unitOfWork,
        IMediator mediator, IAppLocalizer messages)
    {
        _ai = ai;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _messages = messages;
    }

    public async Task<AiReportDraftDto> Handle(DraftReportCommand request, CancellationToken cancellationToken)
    {
        await AiAvailability.EnsureEnabledAsync(_ai, _settings, _messages, cancellationToken);

        var notes = new List<string>();
        var profiles = new List<QueryProfile>();
        foreach (var id in request.QueryIds.Distinct())
        {
            try
            {
                var profile = await QueryProfiler.ProfileAsync(id, _unitOfWork, _mediator, _messages, cancellationToken);
                if (profile.Columns.Count > 0)
                    profiles.Add(profile);
            }
            catch (Exception ex) when (ex is DomainException or ForbiddenAccessException or NotFoundException)
            {
                var name = (await _unitOfWork.DynamicQueries.GetByIdAsync(id, cancellationToken))?.Name ?? id.ToString();
                notes.Add(_messages[MessageKeys.AiDraftQuerySkipped, name, ex.Message]);
            }
        }

        if (profiles.Count == 0)
            throw new DomainException(notes.Count > 0 ? string.Join(" ", notes) : _messages[MessageKeys.AiDraftEmpty]);

        var answer = await _ai.CompleteJsonAsync<AiReportAnswer>(
            new AiChatRequest(
                AiPrompts.ReportSystem(),
                AiPrompts.ReportUser(request.Goal, profiles),
                "report_draft",
                AiPrompts.ReportSchema(profiles.SelectMany(p => p.Columns).Select(c => c.Name)),
                MaxTokens: 2500),
            cancellationToken);

        var builder = new ReportDraftBuilder(profiles, _messages, DateTime.Today);
        var (draft, reasons) = builder.Build(answer);
        notes.AddRange(builder.Notes);

        // The same rules a save applies, so the builder is never handed a draft it cannot save.
        SaveReportCommandHandler.Validate(draft);

        return new AiReportDraftDto { Draft = draft, ChartReasons = reasons, Notes = notes };
    }
}
