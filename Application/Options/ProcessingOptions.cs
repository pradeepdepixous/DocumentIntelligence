namespace PixousDocIntelligence.Application.Options;

public class ProcessingOptions
{
    public string DefaultProvider { get; set; } = "Gemini";
    public GeminiConfig Gemini { get; set; } = new();
    public OpenAiConfig OpenAi { get; set; } = new();
    public BatchingConfig Batching { get; set; } = new();
    public VerificationConfig Verification { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
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
    public int DefaultBatchSize { get; set; } = 4;
    public int DefaultOverlapPages { get; set; } = 1;
    public int SmallDocThreshold { get; set; } = 4;
    public int SmallDocBatchSize { get; set; } = 4;
    public int SmallDocOverlapPages { get; set; } = 0;
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
