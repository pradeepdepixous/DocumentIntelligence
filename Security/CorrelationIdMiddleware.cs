using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PixousDocIntelligence.Security;

/// <summary>
/// Middleware that assigns and propagates request correlation IDs for end-to-end distributed tracing
/// and produces structured audit records with client project identity.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers.TryGetValue(CorrelationHeader, out var val) && !string.IsNullOrWhiteSpace(val)
            ? val.ToString()
            : $"corr_{Guid.NewGuid().ToString("N")[..12]}";

        context.Items[CorrelationHeader] = correlationId;
        context.Response.Headers[CorrelationHeader] = correlationId;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            string clientId = context.Items["ClientId"]?.ToString() ?? "Anonymous";
            string clientName = context.Items["ClientName"]?.ToString() ?? "Public";

            // Only audit API requests (filter out static /swagger asset noise)
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                _logger.LogInformation(
                    "[AUDIT] {Method} {Path} responded {StatusCode} in {ElapsedMs}ms | Client: {ClientId} ({ClientName}) | CorrelationId: {CorrelationId}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    clientId,
                    clientName,
                    correlationId);
            }
        }
    }
}
