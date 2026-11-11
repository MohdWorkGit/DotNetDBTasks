using Jadwal.Application.Common.Interfaces;
using Jadwal.Application.Features.Dashboards.Commands;
using Jadwal.Domain.Exceptions;
using Jadwal.Domain.Interfaces;
using FluentValidation;
using MediatR;

namespace Jadwal.Application.Features.Ai.Commands;

/// <summary>
/// Drafts a dashboard from saved queries the admin picks. Each query is run once, as the builder's
/// column probe runs it, to learn the shape of its result; the model sees that shape — names,
/// kinds, counts, never values — and proposes tiles, which <see cref="DashboardDraftBuilder"/>
/// checks against the real columns before anything is offered. Nothing is saved: the builder
/// loads the draft, and the admin saves it as any other dashboard.
/// </summary>
public class DraftDashboardCommand : IRequest<AiDashboardDraftDto>
{
    public const int MaxQueries = 6;
    public const int GoalMaxLength = 500;

    public List<Guid> QueryIds { get; set; } = new();

    /// <summary>What the dashboard is for, in the admin's words. Optional.</summary>
    public string? Goal { get; set; }
}

public class DraftDashboardValidator : AbstractValidator<DraftDashboardCommand>
{
    public DraftDashboardValidator(IAppLocalizer messages)
    {
        RuleFor(x => x.QueryIds)
            .NotEmpty().WithMessage(_ => messages[MessageKeys.AiQueriesRequired])
            .Must(ids => ids.Distinct().Count() <= DraftDashboardCommand.MaxQueries)
            .WithMessage(_ => messages[MessageKeys.AiDraftTooManyQueries, DraftDashboardCommand.MaxQueries]);

        RuleFor(x => x.Goal)
            .MaximumLength(DraftDashboardCommand.GoalMaxLength)
            .WithMessage(_ => messages[MessageKeys.AiGoalTooLong, DraftDashboardCommand.GoalMaxLength]);
    }
}

public class DraftDashboardCommandHandler : IRequestHandler<DraftDashboardCommand, AiDashboardDraftDto>
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly IAppLocalizer _messages;

    public DraftDashboardCommandHandler(
        IAiChatClient ai, ISystemSettingsService settings, IUnitOfWork unitOfWork,
        IMediator mediator, IAppLocalizer messages)
    {
        _ai = ai;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _messages = messages;
    }

    public async Task<AiDashboardDraftDto> Handle(DraftDashboardCommand request, CancellationToken cancellationToken)
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
                // A query that will not run is left out, not fatal: the others can still make a
                // dashboard, and the note says which one and why.
                var name = (await _unitOfWork.DynamicQueries.GetByIdAsync(id, cancellationToken))?.Name ?? id.ToString();
                notes.Add(_messages[MessageKeys.AiDraftQuerySkipped, name, ex.Message]);
            }
        }

        if (profiles.Count == 0)
            throw new DomainException(notes.Count > 0 ? string.Join(" ", notes) : _messages[MessageKeys.AiDraftEmpty]);

        var answer = await _ai.CompleteJsonAsync<AiDashboardAnswer>(
            new AiChatRequest(
                AiPrompts.DashboardSystem(),
                AiPrompts.DashboardUser(request.Goal, profiles),
                "dashboard_draft",
                AiPrompts.DashboardSchema(profiles.SelectMany(p => p.Columns).Select(c => c.Name))),
            cancellationToken);

        var builder = new DashboardDraftBuilder(profiles, _messages);
        var (draft, reasons) = builder.Build(answer);
        notes.AddRange(builder.Notes);

        if (draft.Tiles.Count == 0)
            throw new DomainException(_messages[MessageKeys.AiDraftEmpty]);

        // The same rules a save applies, so the builder is never handed a draft it cannot save.
        SaveDashboardCommandHandler.Validate(draft);

        return new AiDashboardDraftDto { Draft = draft, Reasons = reasons, Notes = notes };
    }
}
