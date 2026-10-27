using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Models;
using PixousDocIntelligence.Persistence.Entities;

namespace PixousDocIntelligence.Clients;

/// <summary>
/// Production-grade reusable C# client for Pixous Document Intelligence.
/// Consuming projects (Invoice Management, Government Portal, Legal Processing) initialize this client
/// with their own project access key, keeping all provider credentials entirely on the service.
/// </summary>
public class DocumentIntelligenceClient : IDocumentIntelligenceClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DocumentIntelligenceClient(string baseUrl, string projectAccessKey, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Base URL cannot be empty.", nameof(baseUrl));
        if (string.IsNullOrWhiteSpace(projectAccessKey))
            throw new ArgumentException("Project access key cannot be empty.", nameof(projectAccessKey));

        _disposeClient = httpClient == null;
        _httpClient = httpClient ?? Common.FastHttpClientHelper.CreateClient(TimeSpan.FromMinutes(10));

        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Remove("X-Document-Api-Key");
        _httpClient.DefaultRequestHeaders.Add("X-Document-Api-Key", projectAccessKey.Trim());
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<DocumentProcessingResult> ProcessDocumentAsync(
        Stream documentStream, 
        string fileName, 
        string? instructions = null, 
        string? tier = "fast",
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(documentStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "File", fileName);

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            content.Add(new StringContent(instructions), "Instructions");
        }

        if (!string.IsNullOrWhiteSpace(tier))
        {
            content.Add(new StringContent(tier), "Tier");
        }

        using var response = await _httpClient.PostAsync("api/Documents/process", content, cancellationToken);
        return await HandleResponseAsync<DocumentProcessingResult>(response, cancellationToken);
    }

    public async Task<DocumentProcessingResult> ProcessDocumentFileAsync(
        string filePath, 
        string? instructions = null, 
        string? tier = "fast",
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Document file not found: {filePath}");

        await using var stream = File.OpenRead(filePath);
        return await ProcessDocumentAsync(stream, Path.GetFileName(filePath), instructions, tier, cancellationToken);
    }

    public async Task<QnAResult> AskQuestionAsync(
        string jobId, 
        string question, 
        CancellationToken cancellationToken = default)
    {
        var payload = new AgentQuestionRequest { Question = question };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"api/Agent/jobs/{jobId}/ask", stringContent, cancellationToken);
        return await HandleResponseAsync<QnAResult>(response, cancellationToken);
    }

    public async Task<AgentSummarizeResponse> SummarizeDocumentAsync(
        string jobId, 
        string? focusAngle = null, 
        CancellationToken cancellationToken = default)
    {
        var payload = new AgentSummarizeRequest { FocusArea = focusAngle };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        using var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"api/Agent/jobs/{jobId}/summarize", stringContent, cancellationToken);
        return await HandleResponseAsync<AgentSummarizeResponse>(response, cancellationToken);
    }

    public async Task<RuleValidationResponse> EvaluateRulesAsync(
        string jobId, 
        RuleValidationRequest request, 
        CancellationToken cancellationToken = default)
    {
        string json = JsonSerializer.Serialize(request, JsonOptions);
        using var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync($"api/Agent/jobs/{jobId}/validate-rules", stringContent, cancellationToken);
        return await HandleResponseAsync<RuleValidationResponse>(response, cancellationToken);
    }

    public async Task<ExtractionJobEntity?> GetJobStatusAsync(
        string jobId, 
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"api/Documents/jobs/{jobId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await HandleResponseAsync<ExtractionJobEntity>(response, cancellationToken);
    }

    public async Task<Stream> DownloadCsvAsync(
        string jobId, 
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"api/Documents/jobs/{jobId}/csv", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task<JsonDocument> GetMyUsageAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/Usage/me", cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DocumentIntelligenceApiException(
                $"Failed to retrieve usage details: {response.StatusCode}",
                (int)response.StatusCode,
                body);
        }
        return JsonDocument.Parse(body);
    }

    private static async Task<T> HandleResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new DocumentIntelligenceApiException(
                $"Pixous Document Intelligence API request failed with status {(int)response.StatusCode} ({response.ReasonPhrase})",
                (int)response.StatusCode,
                body);
        }

        var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
        if (result == null)
        {
            throw new InvalidOperationException($"Failed to deserialize response body to {typeof(T).Name}");
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Exception thrown when the Pixous Document Intelligence API returns an error status code.
/// </summary>
public class DocumentIntelligenceApiException : Exception
{
    public int StatusCode { get; }
    public string ResponseBody { get; }

    public DocumentIntelligenceApiException(string message, int statusCode, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
