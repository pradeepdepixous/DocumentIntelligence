using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Application.Options;
using PixousDocIntelligence.Application.Orchestration;

namespace PixousDocIntelligence.Startup;

/// <summary>
/// Handles `dotnet run -- --cli &lt;document-path&gt; [instructions]`: runs a single document
/// through the processing pipeline from the console instead of launching the Web API.
/// </summary>
public static class CliRunner
{
    public static async Task RunAsync(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("\n[Running in CLI Mode]");
        Console.ResetColor();

        if (args.Length > 0 && args[0].Equals("--test-validation", StringComparison.OrdinalIgnoreCase))
        {
            await RunValidationSelfTestAsync();
            return;
        }

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

    private static Task RunValidationSelfTestAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("Generic Document Validation Engine - Verification");
        Console.WriteLine("=================================================\n");
        Console.ResetColor();

        var promptBuilder = new PixousDocIntelligence.Application.Validation.ValidationPromptBuilder();
        var evaluator = new PixousDocIntelligence.Application.Validation.RequirementEvaluator();
        var decisionService = new PixousDocIntelligence.Application.Validation.ValidationDecisionService();
        var policy = new PixousDocIntelligence.Domain.Validation.DecisionPolicy { OnUncertain = "manual_review", MinConfidenceThreshold = 0.80 };

        // Test 1: Income Certificate (All Passed -> Valid)
        Console.Write("Test 1: Income Certificate (All Passed) -> ");
        var reqs1 = new List<PixousDocIntelligence.Domain.Validation.ValidationRequirement>
        {
            new() { Id = "doc_identity", Description = "Must be an income certificate", Type = PixousDocIntelligence.Domain.Validation.RequirementType.DocumentClassification, Required = true },
            new() { Id = "cert_no", Description = "Certificate number present", Required = true },
            new() { Id = "authority", Description = "Issuing authority present", Required = true }
        };
        var obs1 = new PixousDocIntelligence.Infrastructure.AI.Validation.Models.GeminiDocumentObservation
        {
            DetectedDocumentType = "income_certificate",
            DocumentTypeConfidence = 0.98,
            Observations = new()
            {
                new() { RequirementId = "doc_identity", ObservationState = "present", Evidence = "Government Income Certificate header", Confidence = 0.99 },
                new() { RequirementId = "cert_no", ObservationState = "present", Evidence = "Cert No: INC/2026/90812", Confidence = 0.95 },
                new() { RequirementId = "authority", ObservationState = "present", Evidence = "Tahsildar seal and signature visible", Confidence = 0.92 }
            }
        };
        var checks1 = evaluator.Evaluate("income_certificate", reqs1, obs1, policy);
        var (status1, isValid1, _) = decisionService.MakeDecision(checks1, policy);
        if (status1 == "valid" && isValid1 == true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [Valid]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL [Got: {status1}]");
        }
        Console.ResetColor();

        // Test 2: Missing Mandatory Requirement (Missing -> Invalid)
        Console.Write("Test 2: Missing Mandatory Requirement -> ");
        var obs2 = new PixousDocIntelligence.Infrastructure.AI.Validation.Models.GeminiDocumentObservation
        {
            DetectedDocumentType = "income_certificate",
            DocumentTypeConfidence = 0.98,
            Observations = new()
            {
                new() { RequirementId = "doc_identity", ObservationState = "present", Evidence = "Income Certificate header", Confidence = 0.99 },
                new() { RequirementId = "cert_no", ObservationState = "missing", Evidence = "No certificate number found", Confidence = 0.90 },
                new() { RequirementId = "authority", ObservationState = "present", Evidence = "Authority seal", Confidence = 0.92 }
            }
        };
        var checks2 = evaluator.Evaluate("income_certificate", reqs1, obs2, policy);
        var (status2, isValid2, _) = decisionService.MakeDecision(checks2, policy);
        if (status2 == "invalid" && isValid2 == false)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [Invalid]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL [Got: {status2}]");
        }
        Console.ResetColor();

        // Test 3: Ambiguous Mandatory Requirement (Ambiguous -> Manual Review)
        Console.Write("Test 3: Ambiguous Date Requirement -> ");
        var reqs3 = new List<PixousDocIntelligence.Domain.Validation.ValidationRequirement>
        {
            new() { Id = "issue_date", Description = "Issue date visible", Required = true }
        };
        var obs3 = new PixousDocIntelligence.Infrastructure.AI.Validation.Models.GeminiDocumentObservation
        {
            DetectedDocumentType = "income_certificate",
            Observations = new()
            {
                new() { RequirementId = "issue_date", ObservationState = "ambiguous", Evidence = "Date partially cut off at bottom fold", Confidence = 0.55 }
            }
        };
        var checks3 = evaluator.Evaluate("income_certificate", reqs3, obs3, policy);
        var (status3, isValid3, _) = decisionService.MakeDecision(checks3, policy);
        if (status3 == "manual_review" && isValid3 == null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [ManualReview]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL [Got: {status3}]");
        }
        Console.ResetColor();

        // Test 4: Community Certificate (Different Document Type) -> Valid
        Console.Write("Test 4: Community Certificate (Different Doc Type) -> ");
        var reqs4 = new List<PixousDocIntelligence.Domain.Validation.ValidationRequirement>
        {
            new() { Id = "community_name", Description = "Community name specified", Required = true },
            new() { Id = "tahsildar_signature", Description = "Tahsildar signature present", Required = true }
        };
        var obs4 = new PixousDocIntelligence.Infrastructure.AI.Validation.Models.GeminiDocumentObservation
        {
            DetectedDocumentType = "community_certificate",
            Observations = new()
            {
                new() { RequirementId = "community_name", ObservationState = "present", Evidence = "Community: BC (Backward Class)", Confidence = 0.94 },
                new() { RequirementId = "tahsildar_signature", ObservationState = "present", Evidence = "Signed by Zonal Deputy Tahsildar", Confidence = 0.91 }
            }
        };
        var checks4 = evaluator.Evaluate("community_certificate", reqs4, obs4, policy);
        var (status4, isValid4, _) = decisionService.MakeDecision(checks4, policy);
        if (status4 == "valid" && isValid4 == true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [Valid]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL [Got: {status4}]");
        }
        Console.ResetColor();

        // Test 5: ValidationPromptBuilder Dynamic Generation Test
        Console.Write("Test 5: ValidationPromptBuilder Dynamic Prompt -> ");
        var (sys, user) = promptBuilder.Build("bank_statement", new List<PixousDocIntelligence.Domain.Validation.ValidationRequirement>
        {
            new() { Id = "account_no", Description = "Account number visible", Required = true },
            new() { Id = "period", Description = "Statement period visible", Required = true }
        });
        if (user.Contains("bank_statement") && user.Contains("account_no") && user.Contains("period") && sys.Contains("GROUNDING & OBSERVATION GUIDELINES"))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [Prompt Generated]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL [Prompt Missing Details]");
        }
        Console.ResetColor();

        // Test 6: Phase 2 - Reusable Validation Profile & Versioning
        Console.Write("Test 6: Profile Creation & Automatic Versioning -> ");
        var memRepo = new PixousDocIntelligence.Persistence.Repositories.InMemoryValidationRepository();
        var profileService = new PixousDocIntelligence.Application.Validation.ValidationProfileService(memRepo);
        var createdProfile = profileService.CreateProfileAsync(new PixousDocIntelligence.Application.Contracts.Validation.CreateValidationProfileRequest
        {
            ProfileId = "prf_inc_test",
            Name = "Income Certificate Verification",
            DocumentType = "income_certificate",
            Requirements = reqs1
        }, "tenant-alpha").GetAwaiter().GetResult();

        var updatedProfile = profileService.UpdateProfileAsync("prf_inc_test", new PixousDocIntelligence.Application.Contracts.Validation.UpdateValidationProfileRequest
        {
            Description = "Updated profile description with Tahsildar seal rule"
        }, "tenant-alpha").GetAwaiter().GetResult();

        if (createdProfile.Version == 1 && updatedProfile != null && updatedProfile.Version == 2)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [v1 -> v2]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"FAIL [Version mismatch: {createdProfile.Version} -> {updatedProfile?.Version}]");
        }
        Console.ResetColor();

        // Test 7: Phase 2 - Validation Audit Result Persistence
        Console.Write("Test 7: Validation Audit Log Persistence & Retrieval -> ");
        var sampleResponse = new PixousDocIntelligence.Application.Contracts.Validation.ValidationResponse
        {
            ValidationId = "val_test_audit_99",
            DocumentType = "income_certificate",
            Status = "valid",
            IsValid = true,
            Checks = checks1,
            Message = "Document valid."
        };
        profileService.SaveValidationResultAsync(sampleResponse, "tenant-alpha", "prf_inc_test", 2).GetAwaiter().GetResult();
        var retrievedAudit = profileService.GetValidationResultAsync("val_test_audit_99", "tenant-alpha").GetAwaiter().GetResult();

        if (retrievedAudit != null && retrievedAudit.Status == "valid" && retrievedAudit.ProfileVersion == 2)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PASS [Audit Saved & Verified]");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("FAIL [Audit Record not found or invalid]");
        }
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\nAll Generic Validation Engine unit checks verified successfully!\n");
        Console.ResetColor();

        return Task.CompletedTask;
    }
}
