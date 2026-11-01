using System.Threading.Channels;
using PixousDocIntelligence.Application.Models;

namespace PixousDocIntelligence.Application.Jobs;

/// <summary>
/// Encapsulates the execution context and quota information for an asynchronous document processing task.
/// </summary>
public class DocumentJobWorkItem
{
    public string JobId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public DocumentProcessingRequest Request { get; set; } = new();
    public string ClientIdentifier { get; set; } = string.Empty;
    public string? BilledAccountId { get; set; }
    public string? ClientId { get; set; }
    public int IncomingPages { get; set; }
    public int RemainingBefore { get; set; }
    public string TempFilePath { get; set; } = string.Empty;
    public string? CampaignId { get; set; }
    public string? BidId { get; set; }
    public string? SourceFileName { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of whole-job processing attempts already made for this work item (0 for the first
    /// attempt). Incremented by the background worker before each scheduled retry so the retry
    /// count travels with the in-memory work item across re-enqueues.
    /// </summary>
    public int AttemptCount { get; set; } = 0;
}

public interface IDocumentJobQueue
{
    ValueTask EnqueueAsync(DocumentJobWorkItem item, CancellationToken cancellationToken = default);
    ValueTask<DocumentJobWorkItem> DequeueAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Thread-safe in-process asynchronous queue backed by System.Threading.Channels.Channel.
/// Enables instantaneous HTTP 202 Accepted returns for 100+ page documents.
/// </summary>
public class DocumentJobQueue : IDocumentJobQueue
{
    private readonly Channel<DocumentJobWorkItem> _channel;

    public DocumentJobQueue()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateUnbounded<DocumentJobWorkItem>(options);
    }

    public ValueTask EnqueueAsync(DocumentJobWorkItem item, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(item, cancellationToken);
    }

    public ValueTask<DocumentJobWorkItem> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
