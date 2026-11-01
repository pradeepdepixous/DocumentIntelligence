using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Persistence.Repositories;

namespace PixousDocIntelligence.Startup;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Binds ProcessingOptions from configuration (with env var overrides for API keys)
    /// and registers it for both IOptions&lt;T&gt; and direct injection.
    /// </summary>
    public static ProcessingOptions AddProcessingOptions(this WebApplicationBuilder builder)
    {
        var processingOptions = new ProcessingOptions();
        processingOptions.DefaultProvider = builder.Configuration.GetValue<string>("DefaultProvider") ?? "Gemini";
        builder.Configuration.GetSection("Gemini").Bind(processingOptions.Gemini);
        builder.Configuration.GetSection("OpenAI").Bind(processingOptions.OpenAi);
        builder.Configuration.GetSection("Batching").Bind(processingOptions.Batching);
        builder.Configuration.GetSection("Verification").Bind(processingOptions.Verification);
        builder.Configuration.GetSection("Storage").Bind(processingOptions.Storage);

        ApplyApiKeyEnvironmentOverrides(processingOptions);

        builder.Services.Configure<ProcessingOptions>(builder.Configuration);
        builder.Services.PostConfigure<ProcessingOptions>(ApplyApiKeyEnvironmentOverrides);
        builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProcessingOptions>>().Value);

        return processingOptions;
    }

    private static void ApplyApiKeyEnvironmentOverrides(ProcessingOptions opts)
    {
        string? defaultProvider = Environment.GetEnvironmentVariable("DEFAULT_PROVIDER");
        if (!string.IsNullOrWhiteSpace(defaultProvider))
        {
            opts.DefaultProvider = defaultProvider;
        }

        string? geminiEnvKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(geminiEnvKey))
        {
            opts.Gemini.ApiKey = geminiEnvKey;
        }

        string? geminiModel = Environment.GetEnvironmentVariable("GEMINI_MODEL");
        if (!string.IsNullOrWhiteSpace(geminiModel))
        {
            opts.Gemini.Model = geminiModel;
        }

        string? openAiEnvKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiEnvKey))
        {
            opts.OpenAi.ApiKey = openAiEnvKey;
        }

        string? openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        if (!string.IsNullOrWhiteSpace(openAiModel))
        {
            opts.OpenAi.Model = openAiModel;
        }

        string? storageProvider = Environment.GetEnvironmentVariable("STORAGE_PROVIDER");
        if (!string.IsNullOrWhiteSpace(storageProvider))
        {
            opts.Storage.Provider = storageProvider;
        }

        string? storageOutputDir = Environment.GetEnvironmentVariable("STORAGE_OUTPUT_BASE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(storageOutputDir))
        {
            opts.Storage.OutputBaseDirectory = storageOutputDir;
        }
    }

    /// <summary>
    /// Binds PortalAuthOptions (Google Sign-In + JWT session config) from configuration,
    /// applies env var overrides, and registers the portal auth services.
    /// </summary>
    public static PortalAuthOptions AddPortalAuthOptions(this WebApplicationBuilder builder)
    {
        var portalAuthOptions = new PortalAuthOptions();
        builder.Configuration.GetSection("PortalAuth").Bind(portalAuthOptions);

        string? googleClientIdEnv = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("PORTAL_AUTH_GOOGLE_CLIENT_ID");
        if (!string.IsNullOrWhiteSpace(googleClientIdEnv)) portalAuthOptions.GoogleClientId = googleClientIdEnv;

        string? jwtSigningKeyEnv = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY")
            ?? Environment.GetEnvironmentVariable("PORTAL_AUTH_JWT_SIGNING_KEY");
        if (!string.IsNullOrWhiteSpace(jwtSigningKeyEnv)) portalAuthOptions.JwtSigningKey = jwtSigningKeyEnv;

        string? jwtIssuerEnv = Environment.GetEnvironmentVariable("JWT_ISSUER");
        if (!string.IsNullOrWhiteSpace(jwtIssuerEnv)) portalAuthOptions.JwtIssuer = jwtIssuerEnv;

        string? jwtAudienceEnv = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
        if (!string.IsNullOrWhiteSpace(jwtAudienceEnv)) portalAuthOptions.JwtAudience = jwtAudienceEnv;

        string? adminEmailsEnv = Environment.GetEnvironmentVariable("ADMIN_EMAILS");
        if (!string.IsNullOrWhiteSpace(adminEmailsEnv))
        {
            portalAuthOptions.AdminEmails = adminEmailsEnv
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        builder.Services.AddSingleton(portalAuthOptions);
        builder.Services.AddSingleton<PixousDocIntelligence.Security.PortalJwtTokenService>();
        builder.Services.AddScoped<PixousDocIntelligence.Services.Accounts.CustomerAccountService>();

        return portalAuthOptions;
    }

    /// <summary>
    /// Normalizes standard postgres:// or postgresql:// URIs into ADO.NET connection strings.
    /// </summary>
    public static string NormalizePostgresConnectionString(string connectionString)
    {
        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(connectionString);
                var userInfo = uri.UserInfo.Split(':');
                var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
                var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
                var port = uri.Port > 0 ? uri.Port : 5432;
                var database = uri.AbsolutePath.TrimStart('/');
                return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};Pooling=true;";
            }
            catch
            {
                return connectionString;
            }
        }
        return connectionString;
    }

    /// <summary>
    /// Registers the document ingestion/processing pipeline: HTTP client, credential vault,
    /// LLM client factory, persistence (Postgres or in-memory), and the pipeline stages.
    /// </summary>
    public static string AddPipelineServices(this IServiceCollection services, IConfiguration configuration, ProcessingOptions processingOptions)
    {
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => PixousDocIntelligence.Common.FastHttpClientHelper.CreateFastSocketsHandler());

        services.AddSingleton<PixousDocIntelligence.Credentials.ICredentialRegistry>(sp =>
            new PixousDocIntelligence.Credentials.JsonFileCredentialRegistry(
                processingOptions.Storage.OutputBaseDirectory,
                sp.GetService<ILogger<PixousDocIntelligence.Credentials.JsonFileCredentialRegistry>>()));

        services.AddSingleton<PixousDocIntelligence.Services.LLM.ILLMClientFactory>(sp =>
            new PixousDocIntelligence.Services.LLM.LLMClientFactory(
                sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ProcessingOptions>>(),
                sp.GetService<IHttpClientFactory>()));

        string? rawConnection = configuration.GetConnectionString("DocumentIntelligence")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("CONNECTION_STRING");

        string? postgresConnection = string.IsNullOrWhiteSpace(rawConnection)
            ? null
            : NormalizePostgresConnectionString(rawConnection);

        if (!string.IsNullOrWhiteSpace(postgresConnection))
        {
            services.AddDbContext<PixousDocIntelligence.Persistence.DocumentIntelligenceDbContext>(options =>
            {
                options.UseNpgsql(postgresConnection, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                    npgsqlOptions.CommandTimeout(30);
                });
            });
        }

        string storageProvider = processingOptions.Storage.Provider;
        bool isPostgres = storageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) ||
                          storageProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
        if (isPostgres && !string.IsNullOrWhiteSpace(postgresConnection))
        {
            services.AddSingleton<IDocumentProcessingRepository, PostgresDocumentProcessingRepository>();
        }
        else
        {
            services.AddSingleton<IDocumentProcessingRepository, InMemoryDocumentProcessingRepository>();
        }

        services.AddSingleton<PixousDocIntelligence.Pipeline.Batching.IBatchScheduler, PixousDocIntelligence.Pipeline.Batching.BatchScheduler>();
        services.AddSingleton<PixousDocIntelligence.Pipeline.Stitching.IStitchingEngine, PixousDocIntelligence.Pipeline.Stitching.ConfidenceAwareStitcher>();
        services.AddSingleton<PixousDocIntelligence.Pipeline.Verification.IEvidenceVerifier, PixousDocIntelligence.Pipeline.Verification.EvidenceVerifier>();
        services.AddSingleton<PixousDocIntelligence.Application.Jobs.IDocumentJobQueue, PixousDocIntelligence.Application.Jobs.DocumentJobQueue>();
        services.AddSingleton<PixousDocIntelligence.Services.Campaigns.ICampaignStorageService, PixousDocIntelligence.Services.Campaigns.CampaignStorageService>();
        services.AddHostedService<PixousDocIntelligence.Application.Jobs.DocumentProcessingBackgroundWorker>();
        services.AddScoped<PixousDocIntelligence.Application.Orchestration.IDocumentProcessingOrchestrator, PixousDocIntelligence.Application.Orchestration.DocumentProcessingOrchestrator>();
        services.AddScoped<PixousDocIntelligence.Application.UseCases.ProcessDocumentUseCase>();

        return storageProvider;
    }

    /// <summary>
    /// Registers the template-based AI document generation pipeline (parsing, validation,
    /// AI content generation, and DOCX/PDF rendering).
    /// </summary>
    public static IServiceCollection AddDocumentGenerationServices(this IServiceCollection services)
    {
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Repositories.ITemplateRepository, PixousDocIntelligence.DocumentGeneration.Repositories.TemplateRepository>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Repositories.IDocumentDraftRepository, PixousDocIntelligence.DocumentGeneration.Repositories.DocumentDraftRepository>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Validation.ITemplateValidator, PixousDocIntelligence.DocumentGeneration.Validation.TemplateValidator>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Validation.IContentValidator, PixousDocIntelligence.DocumentGeneration.Validation.ContentValidator>();
        services.AddScoped<PixousDocIntelligence.DocumentGeneration.Services.IAiContentGenerator, PixousDocIntelligence.DocumentGeneration.Services.AiContentGenerator>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Services.IMarkdownBuilder, PixousDocIntelligence.DocumentGeneration.Services.MarkdownBuilder>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Rendering.IDocumentRenderer, PixousDocIntelligence.DocumentGeneration.Rendering.OpenXmlDocxRenderer>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Rendering.IDocumentRenderer, PixousDocIntelligence.DocumentGeneration.Rendering.QuestPdfDocumentRenderer>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Services.IDocxTemplateParser, PixousDocIntelligence.DocumentGeneration.Services.DocxTemplateParser>();
        services.AddSingleton<PixousDocIntelligence.DocumentGeneration.Services.IDocxTemplatePopulator, PixousDocIntelligence.DocumentGeneration.Services.DocxTemplatePopulator>();
        services.AddScoped<PixousDocIntelligence.DocumentGeneration.Services.IDocumentGenerationPipeline, PixousDocIntelligence.DocumentGeneration.Services.DocumentGenerationPipeline>();

        return services;
    }

    /// <summary>
    /// Registers the generic, configuration-driven Document Validation Engine services.
    /// </summary>
    public static IServiceCollection AddDocumentValidationServices(
        this IServiceCollection services,
        ProcessingOptions processingOptions,
        IConfiguration configuration)
    {
        string? postgresConnection = configuration.GetConnectionString("Postgres");
        string storageProvider = processingOptions.Storage.Provider;
        if (storageProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(postgresConnection))
        {
            services.AddSingleton<PixousDocIntelligence.Persistence.Repositories.IValidationRepository, PixousDocIntelligence.Persistence.Repositories.PostgresValidationRepository>();
        }
        else
        {
            services.AddSingleton<PixousDocIntelligence.Persistence.Repositories.IValidationRepository, PixousDocIntelligence.Persistence.Repositories.InMemoryValidationRepository>();
        }

        services.AddScoped<PixousDocIntelligence.Application.Validation.IValidationProfileService, PixousDocIntelligence.Application.Validation.ValidationProfileService>();
        services.AddSingleton<PixousDocIntelligence.Application.Validation.ValidationPromptBuilder>();
        services.AddSingleton<PixousDocIntelligence.Infrastructure.AI.Validation.IDocumentAnalyzer, PixousDocIntelligence.Infrastructure.AI.Validation.GeminiDocumentAnalyzer>();
        services.AddSingleton<PixousDocIntelligence.Application.Validation.IRequirementEvaluator, PixousDocIntelligence.Application.Validation.RequirementEvaluator>();
        services.AddSingleton<PixousDocIntelligence.Application.Validation.IValidationDecisionService, PixousDocIntelligence.Application.Validation.ValidationDecisionService>();
        services.AddScoped<PixousDocIntelligence.Application.Validation.IValidationOrchestrator, PixousDocIntelligence.Application.Validation.ValidationOrchestrator>();

        return services;
    }

    /// <summary>
    /// Registers API-Key authentication (default scheme) plus JWT Bearer for the customer
    /// portal, and the role-based authorization policies.
    /// </summary>
    public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, PortalAuthOptions portalAuthOptions)
    {
        services.AddAuthentication(options =>
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

        services.AddAuthorization(options =>
        {
            options.AddPolicy("ProcessPolicy", policy => policy.RequireRole("process", "admin"));
            options.AddPolicy("SummarizePolicy", policy => policy.RequireRole("summarize", "admin"));
            options.AddPolicy("AskPolicy", policy => policy.RequireRole("ask", "admin"));
            options.AddPolicy("EvaluatePolicy", policy => policy.RequireRole("evaluate", "admin"));
            options.AddPolicy("AdminPolicy", policy => policy.RequireRole("admin"));
        });

        return services;
    }

    /// <summary>
    /// Registers controllers with the project's JSON formatting conventions.
    /// </summary>
    public static IServiceCollection AddPixousControllers(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.WriteIndented = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        return services;
    }

    /// <summary>
    /// Registers Swagger/OpenAPI generation, including the API-Key security scheme and XML doc comments.
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
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

            string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    /// <summary>
    /// Registers the permissive CORS policy used so browser-based frontends can call the API.
    /// </summary>
    public static IServiceCollection AddPixousCors(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        return services;
    }
}
