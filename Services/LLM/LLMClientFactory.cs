using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PixousDocIntelligence.Application.Options;

namespace PixousDocIntelligence.Services.LLM;

/// <summary>
/// Default implementation of ILLMClientFactory.
/// Dynamically routes to GeminiLLMClient or OpenAiLLMClient, injecting per-request overrides.
/// </summary>
public class LLMClientFactory : ILLMClientFactory
{
    private readonly IOptionsMonitor<ProcessingOptions> _optionsMonitor;
    private readonly IHttpClientFactory? _httpClientFactory;

    [ActivatorUtilitiesConstructor]
    public LLMClientFactory(
        IOptionsMonitor<ProcessingOptions> optionsMonitor, 
        IHttpClientFactory? httpClientFactory = null)
    {
        _optionsMonitor = optionsMonitor;
        _httpClientFactory = httpClientFactory;
    }

    public LLMClientFactory(
        IOptions<ProcessingOptions> options, 
        IHttpClientFactory? httpClientFactory = null)
        : this(new StaticOptionsMonitor<ProcessingOptions>(options.Value), httpClientFactory)
    {
    }

    public ILLMClient GetClient(string? provider = null, string? apiKey = null, string? model = null, string? tier = null)
    {
        var options = _optionsMonitor.CurrentValue;
        string? cleanProvider = CleanInput(provider);
        string? cleanApiKey = CleanInput(apiKey);
        string? cleanModel = CleanInput(model);
        string? cleanTier = CleanInput(tier);

        string selectedProvider;
        string? selectedModel = cleanModel;

        if (!string.IsNullOrWhiteSpace(cleanProvider))
        {
            selectedProvider = cleanProvider;
        }
        else if (!string.IsNullOrWhiteSpace(cleanTier))
        {
            if (cleanTier.Equals("premium", StringComparison.OrdinalIgnoreCase))
            {
                selectedProvider = "OpenAI";
                selectedModel ??= "gpt-4o";
            }
            else // 'fast' or standard
            {
                selectedProvider = "Gemini";
                selectedModel ??= "gemini-flash-lite-latest";
            }
        }
        else
        {
            selectedProvider = !string.IsNullOrWhiteSpace(options.DefaultProvider) ? options.DefaultProvider.Trim() : "Gemini";
        }

        HttpClient? httpClient = _httpClientFactory?.CreateClient();

        if (selectedProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            string key = !string.IsNullOrWhiteSpace(apiKey) 
                ? apiKey.Trim() 
                : options.OpenAi.ApiKey;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "OpenAI API key was not supplied. Please set 'OpenAI:ApiKey' in server configuration or provide 'apiKey' in request override.");
            }

            string modelName = !string.IsNullOrWhiteSpace(selectedModel) 
                ? selectedModel.Trim() 
                : (!string.IsNullOrWhiteSpace(options.OpenAi.Model) ? options.OpenAi.Model : "gpt-4o-mini");

            return new OpenAiLLMClient(key, modelName, httpClient);
        }
        else if (selectedProvider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            string key = !string.IsNullOrWhiteSpace(apiKey) 
                ? apiKey.Trim() 
                : options.Gemini.ApiKey;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "Gemini API key was not supplied. Please provide 'apiKey' in the Swagger request payload/header or set 'Gemini:ApiKey' in configuration.");
            }

            string modelName = !string.IsNullOrWhiteSpace(selectedModel) 
                ? selectedModel.Trim() 
                : (!string.IsNullOrWhiteSpace(options.Gemini.Model) ? options.Gemini.Model : "gemini-flash-lite-latest");

            modelName = NormalizeGeminiModel(modelName);

            return new GeminiLLMClient(key, modelName, httpClient);
        }
        else
        {
            throw new NotSupportedException($"LLM Provider '{selectedProvider}' is not supported. Supported providers are: 'Gemini', 'OpenAI'.");
        }
    }

    /// <summary>
    /// Normalizes user or configuration model aliases (e.g. 'gemini-2.5-flash-lite', 'gemini-flash-lite', 'gemini-flash-latest')
    /// to Google's active, high-throughput endpoint 'gemini-flash-lite-latest'.
    /// </summary>
    public static string NormalizeGeminiModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "gemini-flash-lite-latest";
        string m = model.Trim();
        if (m.Equals("gemini-2.5-flash-lite", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("gemini-flash-lite", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("gemini-3.5-flash-lite", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("gemini-flash-latest", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase) ||
            m.Equals("gemini-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "gemini-flash-lite-latest";
        }
        return m;
    }

    private static string? CleanInput(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return null;
        string trimmed = val.Trim();
        return trimmed.Equals("string", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }
}

internal class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T currentValue) => CurrentValue = currentValue;
    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

