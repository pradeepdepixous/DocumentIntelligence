namespace PixousDocIntelligence.Services.LLM;

/// <summary>
/// Factory abstraction for resolving the appropriate ILLMClient (Gemini, OpenAI, or future providers)
/// based on workflow-specific overrides (API key, model, provider) or fallback configuration.
/// </summary>
public interface ILLMClientFactory
{
    /// <summary>
    /// Creates or resolves an ILLMClient.
    /// If apiKey, model, provider, or tier ('fast' | 'premium') are provided, they override the global defaults.
    /// </summary>
    ILLMClient GetClient(string? provider = null, string? apiKey = null, string? model = null, string? tier = null);
}
