using Jadwal.Application.Common.Interfaces;
using MediatR;

namespace Jadwal.Application.Features.Ai.Queries;

/// <summary>
/// Whether AI is on, and whether the model server answers right now. The client asks once per
/// page to decide whether to offer AI buttons at all; System Settings shows the result.
/// </summary>
public record GetAiStatusQuery : IRequest<AiStatusDto>;

public class GetAiStatusQueryHandler : IRequestHandler<GetAiStatusQuery, AiStatusDto>
{
    private readonly IAiChatClient _ai;
    private readonly ISystemSettingsService _settings;

    public GetAiStatusQueryHandler(IAiChatClient ai, ISystemSettingsService settings)
    {
        _ai = ai;
        _settings = settings;
    }

    public async Task<AiStatusDto> Handle(GetAiStatusQuery request, CancellationToken cancellationToken)
    {
        var status = new AiStatusDto
        {
            Enabled = await AiAvailability.IsEnabledAsync(_ai, _settings, cancellationToken),
            ConfiguredOnServer = _ai.IsConfigured,
            Model = _ai.Model,
            TimeoutSeconds = _ai.TimeoutSeconds
        };

        // Only probe a server that is meant to be there: an installation without AI should not
        // spend three seconds per page discovering it.
        if (!status.Enabled)
            return status;

        status.State = await _ai.GetServerStateAsync(cancellationToken) switch
        {
            AiServerState.Ready => AiStates.Ready,
            AiServerState.ModelMissing => AiStates.ModelMissing,
            _ => AiStates.Unreachable
        };
        return status;
    }
}
