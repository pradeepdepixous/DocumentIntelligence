using System.Net.Http.Headers;
using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Phase 4 implementation: Executes Gemini Flash on an individual PDF page batch
/// using the configured IExtractionStrategy (schema-first or markdown-first).
/// </summary>
public class GeminiBatchExtractor : IGeminiBatchExtractor
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;
    private readonly IExtractionStrategy? _strategy;
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiBatchExtractor(
        string apiKey, 
        string modelName = "gemini-flash-lite-latest", 
        HttpClient? httpClient = null,
        IExtractionStrategy? strategy = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Gemini API key cannot be null or empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _modelName = Services.LLM.LLMClientFactory.NormalizeGeminiModel(modelName);
        _httpClient = httpClient ?? Common.FastHttpClientHelper.CreateClient(TimeSpan.FromMinutes(10));
        _strategy = strategy;
    }

    /// <inheritdoc />
    public async Task<DocumentEnvelope> ExtractBatchAsync(
        byte[] batchPdfBytes, 
        BatchDescriptor batchDescriptor, 
        ExtractionPlan plan, 
        CancellationToken cancellationToken = default)
    {
        var activeStrategy = _strategy ?? ExtractionStrategyResolver.Resolve(plan);
        var (systemInstruction, userPrompt) = activeStrategy.BuildPrompts(batchDescriptor, plan);

        // Written directly as UTF-8 JSON bytes via Utf8JsonWriter.WriteBase64String, which
        // base64-encodes batchPdfBytes straight into the output buffer. This avoids allocating
        // a separate base64 string and a separate serialized JSON string (each ~1.33x the PDF
        // batch size) on top of the raw bytes, which otherwise peaks at ~4-5x the batch size
        // for every extraction call.
        byte[] requestBytes = BuildRequestPayload(systemInstruction, userPrompt, batchPdfBytes);

        string endpoint = $"{GeminiBaseUrl}/{_modelName}:generateContent?key={_apiKey}";

        // Execute with bounded exponential backoff
        string responseJson = await ExecuteWithRetryAsync(endpoint, requestBytes, cancellationToken);
        string extractedJson = ExtractCandidateText(responseJson);

        return activeStrategy.ParseEnvelope(extractedJson, batchDescriptor, plan);
    }

    private static byte[] BuildRequestPayload(string systemInstruction, string userPrompt, byte[] pdfBytes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("systemInstruction");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", systemInstruction);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartArray("contents");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartArray("parts");

            writer.WriteStartObject();
            writer.WriteStartObject("inlineData");
            writer.WriteString("mimeType", "application/pdf");
            writer.WriteBase64String("data", pdfBytes);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteStartObject();
            writer.WriteString("text", userPrompt);
            writer.WriteEndObject();

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartObject("generationConfig");
            writer.WriteString("responseMimeType", "application/json");
            writer.WriteNumber("temperature", 0.1);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private async Task<string> ExecuteWithRetryAsync(string endpoint, byte[] requestBytes, CancellationToken cancellationToken)
    {
        int maxRetries = 4;
        int delayMs = 1500;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(requestBytes)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return responseBody;
            }

            int statusCode = (int)response.StatusCode;
            if ((statusCode == 429 || statusCode == 503 || statusCode == 404) && !_modelName.Contains("lite", StringComparison.OrdinalIgnoreCase))
            {
                // Switch endpoint to gemini-flash-lite-latest for subsequent attempts
                endpoint = $"{GeminiBaseUrl}/gemini-flash-lite-latest:generateContent?key={_apiKey}";
            }

            bool isTransient = statusCode == 429 || statusCode == 503 || statusCode == 500;

            if (!isTransient || attempt == maxRetries)
            {
                throw new HttpRequestException($"Gemini Batch Extraction failed (HTTP {statusCode}): {responseBody}");
            }

            await Task.Delay(delayMs, cancellationToken);
            delayMs *= 2; // Exponential backoff
        }

        throw new Exception("Exceeded retry attempts for batch extraction.");
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
                var textPart = parts[0];
                if (textPart.TryGetProperty("text", out var text))
                {
                    return CleanJsonFences(text.GetString() ?? string.Empty);
                }
            }
        }

        throw new JsonException($"No valid candidate text parts found in Gemini response: {responseJson}");
    }

    private static string CleanJsonFences(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }
}
