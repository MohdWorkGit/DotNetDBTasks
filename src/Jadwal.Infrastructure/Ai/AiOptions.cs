using Microsoft.Extensions.Configuration;

namespace Jadwal.Infrastructure.Ai;

/// <summary>
/// Where the local AI model is and how to talk to it, bound from the "Ai" configuration section.
/// Off unless <c>Ai:Enabled</c> is true: an installation without a model server should not show
/// buttons that can only fail. See docs/LOCAL-AI-OPTIONS.md.
/// </summary>
public sealed class AiOptions
{
    public const string DefaultBaseUrl = "http://127.0.0.1:11434/v1";
    public const string DefaultModel = "gemma4:12b";
    public const int DefaultTimeoutSeconds = 180;

    public bool Enabled { get; init; }

    /// <summary>The OpenAI-compatible API root, ending in a slash so relative paths append to it.</summary>
    public string BaseUrl { get; init; } = DefaultBaseUrl + "/";

    public string Model { get; init; } = DefaultModel;

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    /// <summary>Sent as a bearer token when set — llama-server's <c>--api-key</c>. Ollama needs none.</summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Asks the model to answer straight away rather than reason first (<c>reasoning_effort:
    /// "none"</c>). On by default: measured on qwen3:4b, a short explanation took 279 s with
    /// reasoning and 22 s without, for no better answer.
    /// </summary>
    public bool DisableThinking { get; init; } = true;

    public static AiOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Ai");

        var baseUrl = section.GetValue<string>("BaseUrl");
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = DefaultBaseUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var model = section.GetValue<string>("Model");
        if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;

        var timeout = section.GetValue("TimeoutSeconds", DefaultTimeoutSeconds);
        if (timeout <= 0) timeout = DefaultTimeoutSeconds;

        var apiKey = section.GetValue<string>("ApiKey");

        return new AiOptions
        {
            Enabled = section.GetValue("Enabled", false),
            BaseUrl = baseUrl,
            Model = model.Trim(),
            TimeoutSeconds = timeout,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            DisableThinking = section.GetValue("DisableThinking", true)
        };
    }
}
