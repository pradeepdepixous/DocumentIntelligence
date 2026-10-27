using System.Text;
using System.Text.Json;

namespace PixousDocIntelligence.Services.LLM;

/// <summary>
/// Google Gemini implementation of ILLMClient.
/// Supports Gemini 2.5/3.x Flash and Flash Lite models with automatic quota fallback.
/// </summary>
public class GeminiLLMClient : ILLMClient
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly HttpClient _httpClient;

    public string ProviderName => "Gemini";
    public string ModelName => _modelName;

    public GeminiLLMClient(string apiKey, string? modelName = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Gemini API key cannot be null or empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _modelName = LLMClientFactory.NormalizeGeminiModel(modelName);
        _httpClient = httpClient ?? Common.FastHttpClientHelper.CreateClient(TimeSpan.FromMinutes(10));
    }

    public async Task<string> GenerateContentAsync(
        string systemInstruction, 
        string userPrompt, 
        bool requireJson = false, 
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                responseMimeType = requireJson ? "application/json" : null
            }
        };

        string endpoint = $"{GeminiBaseUrl}/{_modelName}:generateContent?key={_apiKey}";
        string requestJson = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            int statusCode = (int)response.StatusCode;
            if ((statusCode == 429 || statusCode == 503 || statusCode == 404) && !_modelName.Contains("lite", StringComparison.OrdinalIgnoreCase))
            {
                // Fallback to flash-lite if the primary flash model is overloaded (503), rate-limited (429), or not found (404)
                string fallbackEndpoint = $"{GeminiBaseUrl}/gemini-flash-lite-latest:generateContent?key={_apiKey}";
                using var fallbackReq = new HttpRequestMessage(HttpMethod.Post, fallbackEndpoint)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };
                var fallbackResp = await _httpClient.SendAsync(fallbackReq, cancellationToken);
                if (fallbackResp.IsSuccessStatusCode)
                {
                    responseJson = await fallbackResp.Content.ReadAsStringAsync(cancellationToken);
                    goto ParseResponse;
                }
            }

            throw new HttpRequestException($"Gemini API error ({(int)response.StatusCode}): {responseJson}");
        }

    ParseResponse:
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) && 
                content.TryGetProperty("parts", out var parts) && 
                parts.GetArrayLength() > 0)
            {
                return parts[0].GetProperty("text").GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("Gemini returned an empty candidate list or unparseable content.");
    }
}
