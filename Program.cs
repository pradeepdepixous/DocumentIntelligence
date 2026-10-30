using PixousDocIntelligence.Startup;

namespace PixousDocIntelligence;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // If explicitly invoked with --cli flag, execute the console CLI workflow
        if (args.Length > 0 && args[0].Equals("--cli", StringComparison.OrdinalIgnoreCase))
        {
            await CliRunner.RunAsync(args.Skip(1).ToArray());
            return;
        }

        // Default Mode: Launch ASP.NET Core Web API with Swagger UI
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        var processingOptions = builder.AddProcessingOptions();
        var portalAuthOptions = builder.AddPortalAuthOptions();

        string storageProvider = builder.Services.AddPipelineServices(builder.Configuration, processingOptions);
        builder.Services.AddDocumentGenerationServices();
        builder.Services.AddDocumentValidationServices(processingOptions, builder.Configuration);
        builder.Services.AddPortalAuthentication(portalAuthOptions);
        builder.Services.AddPixousControllers();
        builder.Services.AddSwaggerDocumentation();
        builder.Services.AddPixousCors();

        var app = builder.Build();

        DatabaseInitializer.EnsureSchema(app, storageProvider, portalAuthOptions);

        app.UseMiddleware<PixousDocIntelligence.Security.CorrelationIdMiddleware>();
        app.UseCors("AllowAll");
        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pixous Document Intelligence API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Pixous Doc Intelligence API - Swagger UI";
        });

        app.MapGet("/", () => Results.Redirect("/swagger"));
        app.MapControllers();

        PrintApiBanner();

        await app.RunAsync();
    }

    private static void PrintApiBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔════════════════════════════════════════════════════════════════════╗
║    Pixous Doc Intelligence - ASP.NET Core 8 Web API & Swagger      ║
║    Gemini 2.5 Flash VLM • Overlapping Batching • Evidence Ledger   ║
╚════════════════════════════════════════════════════════════════════╝");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🚀 Swagger UI available at: http://localhost:5000/swagger");
        Console.ResetColor();
    }
}
