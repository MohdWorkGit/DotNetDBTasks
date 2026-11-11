namespace Jadwal.Application.Common.Interfaces;

/// <summary>
/// The local AI model, reached over the OpenAI-compatible chat API that Ollama, llama.cpp's
/// <c>llama-server</c> and Foundry Local all speak — so which of them runs, and which model it
/// serves, is configuration (the <c>Ai</c> section), not code.
///
/// <para>Transport only. What to ask, and the shape of the answer, belong to the features in
/// <c>Features/Ai</c>; this sends one request and hands back the answer parsed into that
/// shape. Every failure on the far side surfaces as an
/// <see cref="Domain.Exceptions.ExternalServiceException"/> (502) with a localized message.</para>
/// </summary>
public interface IAiChatClient
{
    /// <summary>True when the server's configuration turns AI on (<c>Ai:Enabled</c>).</summary>
    bool IsConfigured { get; }

    /// <summary>The model name requests are sent with, e.g. <c>gemma4:12b</c>.</summary>
    string Model { get; }

    /// <summary>How long one request may take before it is abandoned.</summary>
    int TimeoutSeconds { get; }

    /// <summary>
    /// Asks the model, constrained to <see cref="AiChatRequest.JsonSchema"/>, and returns the
    /// answer deserialized into <typeparamref name="T"/>.
    /// </summary>
    Task<T> CompleteJsonAsync<T>(AiChatRequest request, CancellationToken cancellationToken) where T : class;

    /// <summary>Whether the model server answers and has the configured model. Never throws.</summary>
    Task<AiServerState> GetServerStateAsync(CancellationToken cancellationToken);
}

/// <summary>One question to the model.</summary>
/// <param name="SystemPrompt">The rules: who the model is helping and how to answer.</param>
/// <param name="UserPrompt">The task itself.</param>
/// <param name="SchemaName">A short name for the answer's shape (letters, digits, underscores).</param>
/// <param name="JsonSchema">The JSON Schema the answer must match, as JSON text.</param>
/// <param name="MaxTokens">
/// The longest answer allowed. A small model can loop — asked to fix a vague error, qwen3:4b ran
/// until the six-minute timeout — and a cut-off answer fails fast as unreadable instead.
/// </param>
public sealed record AiChatRequest(
    string SystemPrompt, string UserPrompt, string SchemaName, string JsonSchema, int MaxTokens = 2000);

public enum AiServerState
{
    Ready,
    /// <summary>Nothing answered at the configured address.</summary>
    Unreachable,
    /// <summary>The server answered, but does not have the configured model.</summary>
    ModelMissing
}
