using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PixousDocIntelligence.Services.LLM;

/// <summary>
/// OpenAI implementation of ILLMClient.
/// Supports GPT-4o, GPT-4o-mini, and compatible OpenAI-standard models.
/// </summary>
public class OpenAiLLMClient : ILLMClient
{
    private const string OpenAiEndpoint = "https://api.openai.com/v1/chat/completions";
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly HttpClient _httpClient;

    public string ProviderName => "OpenAI";
    public string ModelName => _modelName;

    public OpenAiLLMClient(string apiKey, string? modelName = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("OpenAI API key cannot be null or empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "gpt-4o-mini" : modelName.Trim();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<string> GenerateContentAsync(
        string systemInstruction, 
        string userPrompt, 
        bool requireJson = false, 
        CancellationToken cancellationToken = default)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            messages.Add(new { role = "system", content = systemInstruction });
        }

        messages.Add(new { role = "user", content = userPrompt });

        var payload = new Dictionary<string, object>
        {
            ["model"] = _modelName,
            ["messages"] = messages,
            ["temperature"] = 0.2
        };

        if (requireJson)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        string requestJson = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, OpenAiEndpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI API error ({(int)response.StatusCode}): {responseJson}");
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message) && 
                message.TryGetProperty("content", out var contentElem))
            {
                return contentElem.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("OpenAI returned an empty choice list or missing message content.");
    }
}
