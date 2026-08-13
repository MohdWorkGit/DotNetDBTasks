using Bayan.Application;
using Bayan.Application.Common.Interfaces;
using Microsoft.Extensions.Localization;

namespace Bayan.API.Extensions;

/// <summary>
/// Wraps the framework localizer behind the Application layer's own abstraction, alongside
/// <see cref="CurrentUserService"/> — both adapt an ASP.NET concept for handlers that must not
/// reference ASP.NET themselves.
///
/// <para>ASP.NET returns the key itself when a resource is missing, which would put a bare
/// identifier like <c>UsernameTaken</c> in front of a user. Reporting that case explicitly keeps
/// the failure obvious in review rather than shipping it as if it were a sentence.</para>
/// </summary>
public sealed class AppLocalizer : IAppLocalizer
{
    private readonly IStringLocalizer<Messages> _localizer;
    private readonly ILogger<AppLocalizer> _logger;

    public AppLocalizer(IStringLocalizer<Messages> localizer, ILogger<AppLocalizer> logger)
    {
        _localizer = localizer;
        _logger = logger;
    }

    public string this[string key, params object[] args]
    {
        get
        {
            var localized = args.Length == 0 ? _localizer[key] : _localizer[key, args];

            if (localized.ResourceNotFound)
            {
                _logger.LogWarning(
                    "No resource for message key '{Key}' — the raw key was returned to the caller.", key);
                return key;
            }

            return localized.Value;
        }
    }
}
