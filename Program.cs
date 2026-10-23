using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PixousDocIntelligence.Agents;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Application.Orchestration;
using PixousDocIntelligence.Application.UseCases;
using PixousDocIntelligence.Models;
using PixousDocIntelligence.Persistence.Repositories;
using PixousDocIntelligence.Pipeline.Batching;
using PixousDocIntelligence.Pipeline.Stitching;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // If explicitly invoked with --cli flag, execute the console CLI workflow
        if (args.Length > 0 && args[0].Equals("--cli", StringComparison.OrdinalIgnoreCase))
        {
            var cliArgs = args.Skip(1).ToArray();
            await RunCliAsync(cliArgs);
            return;
        }

        // Default Mode: Launch ASP.NET Core Web API with Swagger UI
        var builder = WebApplication.CreateBuilder(args);

        // 1. Build and bind configuration
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        var processingOptions = new ProcessingOptions();
        processingOptions.DefaultProvider = builder.Configuration.GetValue<string>("DefaultProvider") ?? "Gemini";
        builder.Configuration.GetSection("Gemini").Bind(processingOptions.Gemini);
        builder.Configuration.GetSection("OpenAI").Bind(processingOptions.OpenAi);
        builder.Configuration.GetSection("Batching").Bind(processingOptions.Batching);
        builder.Configuration.GetSection("Verification").Bind(processingOptions.Verification);
        builder.Configuration.GetSection("Storage").Bind(processingOptions.Storage);

        // Environment variable overrides
        string? geminiEnvKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(geminiEnvKey))
        {
            processingOptions.Gemini.ApiKey = geminiEnvKey;
        }

        string? openAiEnvKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiEnvKey))
        {
            processingOptions.OpenAi.ApiKey = openAiEnvKey;
        }

        builder.Services.Configure<ProcessingOptions>(builder.Configuration);
        builder.Services.PostConfigure<ProcessingOptions>(opts =>
        {
            string? geminiEnvKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (!string.IsNullOrWhiteSpace(geminiEnvKey))
            {
                opts.Gemini.ApiKey = geminiEnvKey;
            }

            string? openAiEnvKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(openAiEnvKey))
            {
                opts.OpenAi.ApiKey = openAiEnvKey;
            }
        });
        builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PixousDocIntelligence.Application.Options.ProcessingOptions>>().Value);

        // Customer portal auth (Google Sign-In + JWT session), separate from the ApiKeyScheme
        var portalAuthOptions = new PixousDocIntelligence.Application.Options.PortalAuthOptions();
        builder.Configuration.GetSection("PortalAuth").Bind(portalAuthOptions);
        string? googleClientIdEnv = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(googleClientIdEnv)) portalAuthOptions.GoogleClientId = googleClientIdEnv;
        string? jwtSigningKeyEnv = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY");
        if (!string.IsNullOrWhiteSpace(jwtSigningKeyEnv)) portalAuthOptions.JwtSigningKey = jwtSigningKeyEnv;
        builder.Services.AddSingleton(portalAuthOptions);
        builder.Services.AddSingleton<PixousDocIntelligence.Security.PortalJwtTokenService>();
        builder.Services.AddScoped<PixousDocIntelligence.Services.Accounts.CustomerAccountService>();

        // 2. Register core pipeline services & LLM Client Factory (10-minute timeout for multi-batch processing)
        builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        builder.Services.AddSingleton<PixousDocIntelligence.Credentials.ICredentialRegistry>(sp => 
            new PixousDocIntelligence.Credentials.JsonFileCredentialRegistry(
                processingOptions.Storage.OutputBaseDirectory,
                sp.GetService<ILogger<PixousDocIntelligence.Credentials.JsonFileCredentialRegistry>>()));
        builder.Services.AddSingleton<Services.LLM.ILLMClientFactory>(sp => 
            new Services.LLM.LLMClientFactory(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PixousDocIntelligence.Application.Options.ProcessingOptions>>(),
                sp.GetService<IHttpClientFactory>()));

        // Register PostgreSQL DbContext
        string? postgresConnection = builder.Configuration.GetConnectionString("DocumentIntelligence") 
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(postgresConnection))
        {
            builder.Services.AddDbContext<PixousDocIntelligence.Persistence.DocumentIntelligenceDbContext>(options =>
            {
                options.UseNpgsql(postgresConnection, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                    npgsqlOptions.CommandTimeout(30);
                });
            });
        }

        // Configure IDocumentProcessingRepository based on Storage:Provider ("PostgreSql" or "InMemory")
        string storageProvider = processingOptions.Storage.Provider;
        if (storageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(postgresConnection))
        {
            builder.Services.AddSingleton<IDocumentProcessingRepository, PostgresDocumentProcessingRepository>();
        }
        else
        {
            builder.Services.AddSingleton<IDocumentProcessingRepository, InMemoryDocumentProcessingRepository>();
        }
        builder.Services.AddSingleton<IBatchScheduler, BatchScheduler>();
        builder.Services.AddSingleton<IStitchingEngine, ConfidenceAwareStitcher>();
        builder.Services.AddSingleton<IEvidenceVerifier, EvidenceVerifier>();
        builder.Services.AddScoped<IDocumentProcessingOrchestrator, DocumentProcessingOrchestrator>();
        builder.Services.AddScoped<ProcessDocumentUseCase>();

        // Register secure API-Key authentication and role-based policies
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = PixousDocIntelligence.Security.ApiKeyAuthenticationSchemeOptions.DefaultScheme;
            options.DefaultChallengeScheme = PixousDocIntelligence.Security.ApiKeyAuthenticationSchemeOptions.DefaultScheme;
        })
        .AddScheme<PixousDocIntelligence.Security.ApiKeyAuthenticationSchemeOptions, PixousDocIntelligence.Security.ApiKeyAuthenticationHandler>(
            PixousDocIntelligence.Security.ApiKeyAuthenticationSchemeOptions.DefaultScheme,
            _ => { })
        .AddJwtBearer(PixousDocIntelligence.Security.PortalAuthSchemes.Bearer, options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = portalAuthOptions.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = portalAuthOptions.JwtAudience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                    string.IsNullOrWhiteSpace(portalAuthOptions.JwtSigningKey)
                        ? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") // placeholder so startup never crashes; real tokens require a configured key
                        : portalAuthOptions.JwtSigningKey)),
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("ProcessPolicy", policy => policy.RequireRole("process", "admin"));
            options.AddPolicy("SummarizePolicy", policy => policy.RequireRole("summarize", "admin"));
            options.AddPolicy("AskPolicy", policy => policy.RequireRole("ask", "admin"));
            options.AddPolicy("EvaluatePolicy", policy => policy.RequireRole("evaluate", "admin"));
            options.AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
        });

        // 3. Add controllers and configure JSON formatting
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.WriteIndented = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        // 4. Configure Swagger / OpenAPI
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Pixous Document Intelligence API",
                Version = "v1",
                Description = "Enterprise Generic Document Intelligence System in .NET 8.\n\n" +
                              "• Multi-Project Security: Consuming applications use their own dedicated access key (X-Document-Api-Key)\n" +
                              "• Centralized Credential Vault: Gemini & LLM provider secrets stay strictly on the Document Intelligence server\n" +
                              "• Multi-Provider AI: Seamlessly choose between Google Gemini & OpenAI (GPT-4o / GPT-4o-mini)\n" +
                              "• Overlapping batch scheduling with mathematical deduplication (100+ page PDFs)\n" +
                              "• Confidence-aware stitching engine & conflict reconciliation ledger\n" +
                              "• Physical evidence verification against PDF text & scanned page detection\n" +
                              "• Grounded Route B Document Analyst Agent (Summarizer, Q&A, and Compliance Rule Audit Tools)",
                Contact = new OpenApiContact
                {
                    Name = "Pixous Document Intelligence Team"
                }
            });

            // Configure API Key authentication in Swagger UI
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "Pixous Document Intelligence Project Access Key.\n\n" +
                              "Enter your consuming project's access key below (e.g. 'pixous_admin_sec_live_994a821e90b7c4d123456789' or 'pixous_inv_sec_live_38a0bc412f91d8e476210984').\n\n" +
                              "Header: X-Document-Api-Key",
                Name = "X-Document-Api-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "ApiKey"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "ApiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML comments if available
            string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        // Add CORS for web frontends
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // If PostgreSql provider is selected, ensure schema is created/verified
        if (storageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetService<PixousDocIntelligence.Persistence.DocumentIntelligenceDbContext>();
                if (db != null)
                {
                    db.Database.EnsureCreated();

                    // EnsureCreated() only creates the schema for a brand-new database; it does
                    // nothing to an already-existing one, so a newly added table/column
                    // (customer_accounts, and later its password-auth columns) would otherwise
                    // silently never get created/added on an existing deployment. Every statement
                    // here is written to be safely re-runnable on every startup.
                    db.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS customer_accounts (
                            account_id TEXT PRIMARY KEY,
                            google_subject TEXT NULL,
                            email TEXT NOT NULL,
                            name TEXT NOT NULL,
                            picture_url TEXT NULL,
                            is_admin BOOLEAN NOT NULL DEFAULT FALSE,
                            created_at TIMESTAMPTZ NOT NULL,
                            last_login_at TIMESTAMPTZ NULL,
                            password_hash TEXT NULL,
                            failed_login_attempts INT NOT NULL DEFAULT 0,
                            locked_out_until TIMESTAMPTZ NULL
                        );
                        ALTER TABLE customer_accounts ALTER COLUMN google_subject DROP NOT NULL;
                        ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS password_hash TEXT NULL;
                        ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS failed_login_attempts INT NOT NULL DEFAULT 0;
                        ALTER TABLE customer_accounts ADD COLUMN IF NOT EXISTS locked_out_until TIMESTAMPTZ NULL;
                        CREATE UNIQUE INDEX IF NOT EXISTS ix_customer_accounts_google_subject ON customer_accounts (google_subject);
                        CREATE UNIQUE INDEX IF NOT EXISTS ix_customer_accounts_email ON customer_accounts (email);
                    ");

                    Console.WriteLine("📦 PostgreSQL schema verified and active.");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️ PostgreSQL connection failed: {ex.Message}. Pipeline will retry on demand.");
                Console.ResetColor();
            }
        }

        // 5. Middleware Pipeline
        app.UseMiddleware<PixousDocIntelligence.Security.CorrelationIdMiddleware>();
        app.UseCors("AllowAll");
        app.UseAuthentication();
        app.UseAuthorization();

        // Enable Swagger UI in all environments for ease of testing
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pixous Document Intelligence API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Pixous Doc Intelligence API - Swagger UI";
        });

        // Redirect root directly to Swagger UI
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

    private static async Task RunCliAsync(string[] args)
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
