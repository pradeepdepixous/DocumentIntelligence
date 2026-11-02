using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Application.Orchestration;

namespace PixousDocIntelligence.Startup;

/// <summary>
/// Handles `dotnet run -- --cli <document-path> [instructions]`: runs a single document
/// through the processing pipeline from the console instead of launching the Web API.
/// </summary>
public static class CliRunner
{
    public static async Task RunAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n[Running in CLI Mode]");
        Console.ResetColor();

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new ProcessingOptions();
        config.GetSection("Gemini").Bind(options.Gemini);
        config.GetSection("Batching").Bind(options.Batching);
        config.GetSection("Verification").Bind(options.Verification);
        config.GetSection("Storage").Bind(options.Storage);

        string? envKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey)) options.Gemini.ApiKey = envKey;

        string? documentPath = args.Length > 0 ? args[0] : null;
        if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath))
        {
            Console.WriteLine("Usage: dotnet run -- --cli <document-path> [optional-instructions]");
            return;
        }

        string? userGoal = args.Length > 1 ? args[1] : null;

        var orchestrator = new DocumentProcessingOrchestrator(options);
        var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));

        var result = await orchestrator.ProcessDocumentAsync(new DocumentProcessingRequest
        {
            DocumentPath = documentPath,
            UserInstructions = userGoal
        }, progress);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✅ CLI Job Complete: {result.JobId} (Status: {result.Status})");
        Console.WriteLine($"📁 Output: {result.OutputDirectory}");
        Console.ResetColor();
    }
}
