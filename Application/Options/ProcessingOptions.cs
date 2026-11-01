namespace PixousDocIntelligence.Application.Options;

public class ProcessingOptions
{
    public string DefaultProvider { get; set; } = "Gemini";
    public GeminiConfig Gemini { get; set; } = new();
    public OpenAiConfig OpenAi { get; set; } = new();
    public BatchingConfig Batching { get; set; } = new();
    public VerificationConfig Verification { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public JobRetryConfig JobRetry { get; set; } = new();
}

public class OpenAiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

public class GeminiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-flash-latest";
}

public class BatchingConfig
{
    public int DefaultBatchSize { get; set; } = 15;
    public int DefaultOverlapPages { get; set; } = 1;
    public int SmallDocThreshold { get; set; } = 4;
    public int SmallDocBatchSize { get; set; } = 4;
    public int SmallDocOverlapPages { get; set; } = 0;

    /// <summary>
    /// Caps the total number of extra extraction calls (same-size retries plus split-induced
    /// sub-batch extractions) spent recovering incomplete markdown transcripts across an entire
    /// document. Once exhausted, any batch still incomplete is accepted as-is and left for the
    /// usual missing/unclear-page review flagging, instead of continuing to retry or split.
    /// Bounds worst-case API cost/latency on a document where many batches are persistently bad.
    /// </summary>
    public int MaxRecoveryCallsPerDocument { get; set; } = 40;
}

/// <summary>
/// Controls whole-job retry behavior in the background worker: when a job throws a transient
/// error (as opposed to returning an incomplete-but-successful extraction, which the batching
/// recovery budget handles separately), it is re-queued with exponential backoff instead of
/// being marked Failed immediately.
/// </summary>
public class JobRetryConfig
{
    public int MaxRetries { get; set; } = 3;
    public int BaseDelaySeconds { get; set; } = 5;
    public int MaxDelaySeconds { get; set; } = 120;
}

public class VerificationConfig
{
    public double ReviewThreshold { get; set; } = 0.70;
    public double UnverifiedPenalty { get; set; } = 0.15;
}

public class StorageConfig
{
    public string Provider { get; set; } = "PostgreSql"; // "PostgreSql", "InMemory"
    public string OutputBaseDirectory { get; set; } = "output";
}
