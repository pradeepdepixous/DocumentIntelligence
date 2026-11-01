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

        // Allow up to 100MB PDF uploads through Kestrel and multipart form handlers
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 104_857_600;
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 104_857_600;
            options.ValueLengthLimit = 104_857_600;
        });

        builder.Configuration
            .SetBasePath(builder.Environment.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
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

        // Ensure required physical directories exist under ContentRootPath on Windows/IIS
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "uploads"));
        Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, processingOptions.Storage.OutputBaseDirectory ?? "output"));

        DatabaseInitializer.EnsureSchema(app, storageProvider, portalAuthOptions);

        app.UseMiddleware<PixousDocIntelligence.Security.CorrelationIdMiddleware>();
        app.UseCors("AllowAll");
        app.UseAuthentication();
        app.UseAuthorization();

        // Serve React SPA static files from wwwroot (Unified Single-Site Hosting)
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pixous Document Intelligence API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Pixous Doc Intelligence API - Swagger UI";
        });

        app.MapControllers();

        // If wwwroot/index.html is deployed, handle client-side routing; otherwise redirect root to Swagger
        string wwwrootIndex = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
        if (File.Exists(wwwrootIndex))
        {
            app.MapFallbackToFile("index.html");
        }
        else
        {
            app.MapGet("/", () => Results.Redirect("/swagger"));
        }

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
