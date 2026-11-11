using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jadwal.Application.Common.Interfaces;
using Jadwal.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Jadwal.Infrastructure.Ai;

/// <summary>
/// <see cref="IAiChatClient"/> over <c>POST {BaseUrl}chat/completions</c>, the OpenAI-compatible
/// endpoint. The answer is constrained with <c>response_format: json_schema</c>, which Ollama
/// (0.5+) and llama-server both enforce while generating, so a well-formed reply is the norm
/// rather than something to coax out of the prompt.
///
/// <para>A plain typed <see cref="HttpClient"/> on purpose: no SDK means no new package to audit
/// or carry in the offline bundle.</para>
/// </summary>
public sealed class OpenAiCompatibleChatClient : IAiChatClient
{
    /// <summary>The status probe only asks for the model list; it should never keep a page waiting.</summary>
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions AnswerJson = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly IAppLocalizer _messages;
    private readonly ILogger<OpenAiCompatibleChatClient> _logger;

    public OpenAiCompatibleChatClient(
        HttpClient http, AiOptions options, IAppLocalizer messages, ILogger<OpenAiCompatibleChatClient> logger)
    {
        _http = http;
        _options = options;
        _messages = messages;
        _logger = logger;

        _http.BaseAddress = new Uri(options.BaseUrl);
        // The per-request token below enforces the limit; this only stops HttpClient's own
        // 100-second default from cutting a long answer short first.
        _http.Timeout = Timeout.InfiniteTimeSpan;
        if (options.ApiKey is not null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
    }

    public bool IsConfigured => _options.Enabled;
    public string Model => _options.Model;
    public int TimeoutSeconds => _options.TimeoutSeconds;

    public async Task<T> CompleteJsonAsync<T>(AiChatRequest request, CancellationToken cancellationToken) where T : class
    {
        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["stream"] = false,
            ["max_tokens"] = request.MaxTokens,
            // Low, not zero: explanations read better with a little variety, and SQL stays put.
            ["temperature"] = 0.1,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JsonObject { ["role"] = "user", ["content"] = request.UserPrompt }
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.SchemaName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(request.JsonSchema)
                }
            }
        };
        if (_options.DisableThinking)
            body["reasoning_effort"] = "none";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("chat/completions", body, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalServiceException(_messages[MessageKeys.AiTimedOut, _options.TimeoutSeconds]);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceException(_messages[MessageKeys.AiUnreachable, _options.BaseUrl], ex);
        }

        using (response)
        {
            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ExternalServiceException(_messages[MessageKeys.AiTimedOut, _options.TimeoutSeconds]);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ExternalServiceException(_messages[MessageKeys.AiModelMissing, _options.Model]);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI model server answered {Status}: {Body}", (int)response.StatusCode, Shorten(text));
                throw new ExternalServiceException(
                    _messages[MessageKeys.AiFailed, $"{(int)response.StatusCode} {Shorten(ErrorMessage(text), 200)}"]);
            }

            return ParseAnswer<T>(text);
        }
    }

    public async Task<AiServerState> GetServerStateAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StatusTimeout);
        try
        {
            using var response = await _http.GetAsync("models", timeout.Token);
            if (!response.IsSuccessStatusCode)
                return AiServerState.Unreachable;

            var list = await response.Content.ReadFromJsonAsync<JsonObject>(timeout.Token);
            var ids = (list?["data"] as JsonArray ?? new JsonArray())
                .Select(m => m?["id"]?.GetValue<string>())
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            return ids.Any(id => SameModel(id!, _options.Model)) ? AiServerState.Ready : AiServerState.ModelMissing;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                       or InvalidOperationException)
        {
            return AiServerState.Unreachable;
        }
    }

    /// <summary>
    /// Ollama lists <c>name:tag</c>, and a name configured without a tag means <c>:latest</c>.
    /// </summary>
    private static bool SameModel(string listed, string configured) =>
        string.Equals(listed, configured, StringComparison.OrdinalIgnoreCase)
        || (!configured.Contains(':') && string.Equals(listed, configured + ":latest", StringComparison.OrdinalIgnoreCase));

    private T ParseAnswer<T>(string responseText) where T : class
    {
        try
        {
            var content = JsonNode.Parse(responseText)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var answer = JsonSerializer.Deserialize<T>(StripCodeFence(content), AnswerJson);
                if (answer is not null)
                    return answer;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "AI model answer could not be read: {Body}", Shorten(responseText));
        }
        throw new ExternalServiceException(_messages[MessageKeys.AiBadAnswer]);
    }

    /// <summary>Some models wrap JSON in a ```json fence even when held to a schema.</summary>
    private static string StripCodeFence(string content)
    {
        var text = content.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstLineEnd = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd > 0 && lastFence > firstLineEnd ? text[(firstLineEnd + 1)..lastFence].Trim() : text;
    }

    /// <summary>The <c>error.message</c> of an OpenAI-style error body, or the body itself.</summary>
    private static string ErrorMessage(string body)
    {
        try
        {
            var node = JsonNode.Parse(body)?["error"];
            return node is JsonObject ? node["message"]?.GetValue<string>() ?? body : node?.GetValue<string>() ?? body;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return body;
        }
    }

    private static string Shorten(string text, int max = 1000) =>
        text.Length <= max ? text : text[..max] + "…";
}
