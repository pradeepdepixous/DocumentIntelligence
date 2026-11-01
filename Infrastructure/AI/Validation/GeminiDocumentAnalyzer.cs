using System.Text;
using System.Text.Json;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Common;
using PixousDocIntelligence.Infrastructure.AI.Validation.Models;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.Infrastructure.AI.Validation;

public class GeminiDocumentAnalyzer : IDocumentAnalyzer
{
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private readonly HttpClient _httpClient;
    private readonly ProcessingOptions _options;

    public GeminiDocumentAnalyzer(ProcessingOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? FastHttpClientHelper.CreateClient(TimeSpan.FromMinutes(2));
    }

    public async Task<GeminiDocumentObservation> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        string systemInstruction,
        string userPrompt,
        string? apiKey = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes == null || fileBytes.Length == 0)
        {
            throw new ArgumentException("Document bytes cannot be null or empty.", nameof(fileBytes));
        }

        string activeApiKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : _options.Gemini.ApiKey;
        if (string.IsNullOrWhiteSpace(activeApiKey))
        {
            throw new InvalidOperationException("No Gemini API key available. Provide a BYOK header (X-LLM-ApiKey) or configure Gemini:ApiKey.");
        }

        bool isExplicitModel = !string.IsNullOrWhiteSpace(model);
        string activeModel = isExplicitModel 
            ? LLMClientFactory.NormalizeGeminiModel(model) 
            : LLMClientFactory.NormalizeGeminiModel(_options.Gemini.Model ?? "gemini-flash-latest");

        string normalizedMimeType = NormalizeMimeType(mimeType);

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
                    parts = new object[]
                    {
                        new
                        {
                            inlineData = new
                            {
                                mimeType = normalizedMimeType,
                                data = Convert.ToBase64String(fileBytes)
                            }
                        },
                        new
                        {
                            text = userPrompt
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json"
            }
        };

        string endpoint = $"{GeminiBaseUrl}/{activeModel}:generateContent?key={activeApiKey}";
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

            // Only fallback to flash-lite if the user did NOT explicitly pin a custom model
            if (!isExplicitModel && (statusCode == 429 || statusCode == 503 || statusCode == 404) && !activeModel.Contains("lite", StringComparison.OrdinalIgnoreCase))
            {
                string fallbackEndpoint = $"{GeminiBaseUrl}/gemini-flash-lite-latest:generateContent?key={activeApiKey}";
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

            throw new HttpRequestException($"Gemini document analysis failed (HTTP {statusCode}): {responseJson}");
        }

    ParseResponse:
        string candidateText = ExtractCandidateText(responseJson);
        string cleanedJson = CleanJsonFences(candidateText);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var observation = JsonSerializer.Deserialize<GeminiDocumentObservation>(cleanedJson, options);
            if (observation != null)
            {
                return observation;
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Failed to deserialize Gemini observation response: {ex.Message}. Raw text: {cleanedJson}", ex);
        }

        return new GeminiDocumentObservation();
    }

    private static string NormalizeMimeType(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType)) return "application/pdf";
        mimeType = mimeType.Trim().ToLowerInvariant();
        return mimeType switch
        {
            ".pdf" or "application/pdf" => "application/pdf",
            ".png" or "image/png" => "image/png",
            ".jpg" or ".jpeg" or "image/jpeg" => "image/jpeg",
            ".webp" or "image/webp" => "image/webp",
            _ => mimeType
        };
    }

    private static string ExtractCandidateText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0)
            {
                var textProp = parts[0];
                if (textProp.TryGetProperty("text", out var textVal))
                {
                    return textVal.GetString() ?? string.Empty;
                }
            }
        }

        return responseJson;
    }

    private static string CleanJsonFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "{}";
        string trimmed = text.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }
        return trimmed.Trim();
    }
}

