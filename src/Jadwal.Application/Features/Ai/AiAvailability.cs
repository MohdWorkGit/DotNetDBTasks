using Jadwal.Application.Common.Interfaces;
using Jadwal.Domain.Entities;
using Jadwal.Domain.Exceptions;

namespace Jadwal.Application.Features.Ai;

/// <summary>
/// AI is on only when both switches agree: the server's configuration points at a model
/// (<c>Ai:Enabled</c>, the operator's call) and System Settings has not turned it off (the
/// administrator's).
/// </summary>
internal static class AiAvailability
{
    public static async Task<bool> IsEnabledAsync(
        IAiChatClient ai, ISystemSettingsService settings, CancellationToken cancellationToken) =>
        ai.IsConfigured
        && await settings.GetBoolAsync(SystemSettingKeys.AiEnabled, SystemSettingKeys.AiEnabledDefault, cancellationToken);

    public static async Task EnsureEnabledAsync(
        IAiChatClient ai, ISystemSettingsService settings, IAppLocalizer messages, CancellationToken cancellationToken)
    {
        if (!await IsEnabledAsync(ai, settings, cancellationToken))
            throw new DomainException(messages[MessageKeys.AiDisabled]);
    }
}
