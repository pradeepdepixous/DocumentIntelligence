using System.Text;
using System.Text.Json;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;
using PixousDocIntelligence.Pipeline.Batching;

namespace PixousDocIntelligence.Pipeline.Extraction;

/// <summary>
/// Phase 4 implementation: Executes Gemini 2.5 Flash on an individual PDF page batch
/// adhering strictly to the job's versioned ExtractionPlan and emitting DocumentEnvelope items.
/// </summary>
public class GeminiBatchExtractor : IGeminiBatchExtractor
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;
    private const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public GeminiBatchExtractor(
        string apiKey, 
        string modelName = "gemini-flash-lite-latest", 
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Gemini API key cannot be null or empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _modelName = Services.LLM.LLMClientFactory.NormalizeGeminiModel(modelName);
        _httpClient = httpClient ?? Common.FastHttpClientHelper.CreateClient(TimeSpan.FromMinutes(10));
    }

    /// <inheritdoc />
    public async Task<DocumentEnvelope> ExtractBatchAsync(
        byte[] batchPdfBytes, 
        BatchDescriptor batchDescriptor, 
        ExtractionPlan plan, 
        CancellationToken cancellationToken = default)
    {
        string base64Data = Convert.ToBase64String(batchPdfBytes);

        // Build dynamic instructions for layout-preserving verbatim Markdown transcription
        string systemInstruction = 
            $"You are an expert Document Intelligence and Markdown Reconstruction Engine (Gemini 2.5 Flash).\n" +
            $"You are analyzing Batch {batchDescriptor.BatchId} of a larger document. " +
            $"The batch covers original document pages {batchDescriptor.StartPage} through {batchDescriptor.EndPage}.\n\n" +
            "CORE MISSION:\n" +
            "1. Transcribe each page into structured GitHub Flavored Markdown (headings #, ##, bold text, bullet points, numbered lists).\n" +
            "2. Preserve ALL tables using standard Markdown table syntax (| Header 1 | Header 2 |) with full cell fidelity.\n" +
            "3. Extract high-level keywords/topics that summarize this batch.\n" +
            "4. Provide a concise executive summary of the content in this batch.\n\n" +
            "STRICT JSON OUTPUT FORMAT:\n" +
            "{\n" +
            "  \"batch_summary\": \"Concise summary of pages in this batch...\",\n" +
            "  \"batch_keywords\": [\"keyword1\", \"keyword2\", \"keyword3\"],\n" +
            "  \"pages\": [\n" +
            "    {\n" +
            $"      \"page_number\": {batchDescriptor.StartPage},\n" +
            "      \"has_tables\": false,\n" +
            "      \"markdown\": \"# Page Heading\\n\\nVerbatim page text in markdown format...\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"items\": [],\n" +
            "  \"tables\": []\n" +
            "}\n\n" +
            $"CRITICAL REQUIREMENT: For every single page from {batchDescriptor.StartPage} to {batchDescriptor.EndPage}, " +
            "there MUST be a corresponding entry in the 'pages' array. Capture all text completely without skipping.";

        string userPrompt =
            $"Process pages {batchDescriptor.StartPage} through {batchDescriptor.EndPage}. " +
            "Transcribe each page into structured markdown, extract key tables, keywords, and provide a batch summary. " +
            "Output strictly conforming JSON.";

        var requestPayload = new
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
                                mimeType = "application/pdf",
                                data = base64Data
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
                responseMimeType = "application/json",
                temperature = 0.1
            }
        };

        string endpoint = $"{GeminiBaseUrl}/{_modelName}:generateContent?key={_apiKey}";
        string requestJson = JsonSerializer.Serialize(requestPayload);

        // Execute with bounded exponential backoff
        string responseJson = await ExecuteWithRetryAsync(endpoint, requestJson, cancellationToken);
        string extractedJson = ExtractCandidateText(responseJson);

        return ParseEnvelope(extractedJson, batchDescriptor);
    }

    private async Task<string> ExecuteWithRetryAsync(string endpoint, string requestJson, CancellationToken cancellationToken)
    {
        int maxRetries = 4;
        int delayMs = 1500;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

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

    private static DocumentEnvelope ParseEnvelope(string rawJson, BatchDescriptor batch)
    {
        var envelope = new DocumentEnvelope
        {
            DocumentId = batch.DocumentId,
            JobId = batch.JobId,
            BatchId = batch.BatchId,
            SchemaVersion = 2,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // 1. Parse per-page Markdown transcripts
            var pages = EnvelopeJsonParsing.ParseMarkdownPages(root, batch);
            envelope.Pages = pages;

            // 2. Parse batch summary and keywords
            string summary = EnvelopeJsonParsing.ParseBatchSummary(root);
            var keywords = EnvelopeJsonParsing.ParseBatchKeywords(root);
            envelope.DocumentSummary = summary;
            envelope.DocumentKeywords = keywords;

            envelope.Batches.Add(new BatchMarkdownResult
            {
                BatchId = batch.BatchId,
                StartPage = batch.StartPage,
                EndPage = batch.EndPage,
                BatchSummary = summary,
                BatchKeywords = keywords,
                Pages = pages
            });

            // Items/Tables are intentionally left empty: this prompt asks for markdown transcription,
            // not structured field extraction, so there is nothing meaningful to parse into them.
        }
        catch (Exception ex)
        {
            throw new JsonException($"Failed to parse Gemini batch envelope: {rawJson}", ex);
        }

        return envelope;
    }

    private static string ExtractCandidateText(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var first = candidates[0];
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0)
            {
                string text = parts[0].GetProperty("text").GetString() ?? string.Empty;
                return CleanJsonFences(text);
            }
        }

        throw new Exception("Gemini batch response contained no text parts.");
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
