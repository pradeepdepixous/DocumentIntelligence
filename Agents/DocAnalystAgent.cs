using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Models;
using PixousDocIntelligence.Services.LLM;

namespace PixousDocIntelligence.Agents;

/// <summary>
/// Route B: Lightweight, dependency-free Document Analyst Agent.
/// Operates on schema-independent structured document JSON with built-in
/// Summarizer, Grounded Q&amp;A, and Rule Validation tools using any configured LLM provider.
/// </summary>
public class DocAnalystAgent : IDocAnalystAgent
{
    private readonly ILLMClient _llmClient;

    private DocumentAnalysisResult? _currentDocument;
    private string _documentJson = string.Empty;
    private string? _documentText;

    public bool HasDocumentLoaded => _currentDocument != null;
    public DocumentAnalysisResult? CurrentDocument => _currentDocument;

    public DocAnalystAgent(ILLMClient llmClient)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    }

    public DocAnalystAgent(
        string apiKey, 
        string modelName = "gemini-flash-lite-latest", 
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        }

        _llmClient = new GeminiLLMClient(apiKey, modelName, httpClient);
    }

    /// <inheritdoc />
    public void LoadDocument(DocumentAnalysisResult document, string? rawJson = null, string? rawDocumentText = null)
    {
        _currentDocument = document ?? throw new ArgumentNullException(nameof(document));
        _documentJson = !string.IsNullOrWhiteSpace(rawJson) 
            ? rawJson 
            : JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        _documentText = rawDocumentText;
    }

    /// <inheritdoc />
    public async Task<string> SummarizeAsync(string? focusArea = null, CancellationToken cancellationToken = default)
    {
        EnsureDocumentLoaded();

        string systemInstruction = 
            "You are an expert Document Intelligence Analyst Agent. " +
            "Your task is to synthesize a high-impact executive summary from the provided structured document data.\n" +
            "GUIDELINES:\n" +
            "1. Highlight the core purpose, parties involved, key offerings/values, and primary conclusions.\n" +
            "2. Note the page numbers where key insights originate (e.g. 'Found on Page 3').\n" +
            "3. If overall confidence or specific key fields have confidence < 0.90, include a brief data quality note.\n" +
            "4. Format the output cleanly with markdown bullet points and clear sections.";

        string userPrompt = string.IsNullOrWhiteSpace(focusArea)
            ? $"Analyze this {_currentDocument!.DocumentType} and provide a comprehensive executive summary.\n\n" +
              (!string.IsNullOrWhiteSpace(_documentText) ? $"FULL DOCUMENT CONTENT (BY PAGE):\n{_documentText}\n\n" : "") +
              $"DOCUMENT DATA:\n{_documentJson}"
            : $"Analyze this {_currentDocument!.DocumentType} and provide an executive summary focusing specifically on '{focusArea}'.\n\n" +
              (!string.IsNullOrWhiteSpace(_documentText) ? $"FULL DOCUMENT CONTENT (BY PAGE):\n{_documentText}\n\n" : "") +
              $"DOCUMENT DATA:\n{_documentJson}";

        return await CallGeminiAsync(systemInstruction, userPrompt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<QnAResult> AskQuestionAsync(string question, CancellationToken cancellationToken = default)
    {
        EnsureDocumentLoaded();

        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question cannot be null or empty.", nameof(question));
        }

        string systemInstruction = 
            "You are an expert Document Intelligence and Grounded Q&A Analyst.\n" +
            "Your role is to provide clear, accurate, and structured answers strictly grounded in the provided document data (fields, tables, rules, conflicts, and full document text).\n\n" +
            "GROUNDING & ANSWERING INSTRUCTIONS:\n" +
            "1. GROUNDING: Answer using ONLY the facts present in the DOCUMENT DATA and FULL TEXT. Do NOT invent external facts.\n" +
            "2. SYNTHESIS FOR BROAD QUESTIONS: When asked broad, explanatory, or conceptual questions (e.g., 'explain the requirements', 'what is this system', 'list the key features', 'summarize the workflow', 'total contract financial value'), SYNTHESIZE the facts, specifications, and tables into a clear, comprehensive breakdown with descriptive headings, bullet points, and exact amounts.\n" +
            "3. SPECIFIC LOOKUPS: For factual lookups, provide the direct value, cite the Page Number and Confidence score (e.g. '[Page 3, 98% Conf]').\n" +
            "4. DATA QUALITY WARNINGS: If any referenced field has confidence under 0.90, include a warning: '[Quality Note: Low confidence extraction (<90%)]'.\n" +
            "5. ABSENT DATA: Only state that information is absent if the requested topic is completely unrelated or missing from both the extracted data and the text.\n" +
            "6. CITATIONS: Conclude with a '### Key Page References' section listing the relevant page numbers.";

        string userPrompt = 
            $"DOCUMENT DATA (JSON):\n{_documentJson}\n\n" +
            (!string.IsNullOrWhiteSpace(_documentText) ? $"ORIGINAL DOCUMENT FULL TEXT (BY PAGE):\n{_documentText}\n\n" : "") +
            $"USER QUESTION:\n{question}";

        string responseText = await CallGeminiAsync(systemInstruction, userPrompt, cancellationToken);

        bool isTrulyAbsent = responseText.StartsWith("The specific detail", StringComparison.OrdinalIgnoreCase) ||
                             (responseText.Contains("not found in the document", StringComparison.OrdinalIgnoreCase) && 
                              !responseText.Contains("Page ", StringComparison.OrdinalIgnoreCase));

        // Build structured QnAResult
        var result = new QnAResult
        {
            Answer = responseText,
            IsFoundInDocument = !isTrulyAbsent
        };

        // Extract citations from the loaded document matching any mentioned fields
        if (_currentDocument?.Fields != null)
        {
            foreach (var (key, field) in _currentDocument.Fields)
            {
                if (responseText.Contains(key, StringComparison.OrdinalIgnoreCase) || 
                    responseText.Contains(field.GetDisplayValue(), StringComparison.OrdinalIgnoreCase))
                {
                    result.Citations.Add(new FieldCitation
                    {
                        FieldName = key,
                        PageNumber = field.PageNumber,
                        Confidence = field.Confidence,
                        Evidence = field.EvidenceContext
                    });

                    if (field.Confidence < 0.90)
                    {
                        result.Warnings.Add($"Field '{key}' has confidence {field.Confidence:P0} (< 90%). Manual verification recommended.");
                    }
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<RuleValidationResponse> ValidateRulesAsync(RuleValidationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureDocumentLoaded();

        if (request == null || request.Rules == null || request.Rules.Count == 0)
        {
            throw new ArgumentException("Rule validation request must contain at least one rule.", nameof(request));
        }

        string rulesJson = JsonSerializer.Serialize(request.Rules, new JsonSerializerOptions { WriteIndented = true });

        string systemInstruction =
            "You are an expert Document Compliance & Quality Auditor Agent.\n" +
            "Your task is to strictly audit and validate a checklist of business rules against the provided document data.\n\n" +
            "EVALUATION INSTRUCTIONS FOR EACH RULE:\n" +
            "1. Examine all fields, functional requirements, text, and tables to determine if the rule requirement is satisfied.\n" +
            "2. Status: Assign one of:\n" +
            "   - \"Pass\": The document completely satisfies the requirement.\n" +
            "   - \"Partial\": The document partially addresses the requirement but has minor gaps, caveats, or ambiguities.\n" +
            "   - \"Fail\": The document violates the requirement or omits required information.\n" +
            "   - \"NotApplicable\": The rule is not relevant to this document type.\n" +
            "3. Marks Awarded:\n" +
            "   - Award points between 0.0 and max_marks based on the degree of compliance.\n" +
            "   - For \"Pass\": award full max_marks.\n" +
            "   - For \"Partial\": award fair partial marks proportional to compliance.\n" +
            "   - For \"Fail\": award 0.0 marks.\n" +
            "   - For \"NotApplicable\": award full max_marks.\n" +
            "4. Decision Reason: Clearly explain WHY the rule passed, partially passed, or failed.\n" +
            "5. Marks Reason: Explicitly explain the scoring breakdown and justification for the marks awarded or deducted (e.g. 'Awarded 35/40 marks. Deducted 5 marks due to lack of explicit 24/7 coverage timeline').\n" +
            "6. Finding: Provide a clear, fact-based summary finding.\n" +
            "7. Page Citations: List the exact integer page numbers where supporting evidence was found.\n" +
            "8. Evidence Quote: Quote the exact sentence or phrase supporting the decision.\n\n" +
            "RESPONSE FORMAT:\n" +
            "You MUST respond ONLY with a JSON array of evaluated rule objects with schema:\n" +
            "[\n" +
            "  {\n" +
            "    \"rule_id\": \"string\",\n" +
            "    \"category\": \"string\",\n" +
            "    \"status\": \"Pass\" | \"Fail\" | \"Partial\" | \"NotApplicable\",\n" +
            "    \"marks_awarded\": number,\n" +
            "    \"max_marks\": number,\n" +
            "    \"decision_reason\": \"string\",\n" +
            "    \"marks_reason\": \"string\",\n" +
            "    \"finding\": \"string\",\n" +
            "    \"page_citations\": [1, 2],\n" +
            "    \"evidence_quote\": \"string\",\n" +
            "    \"confidence\": number\n" +
            "  }\n" +
            "]";

        string userPrompt =
            $"DOCUMENT DATA (JSON):\n{_documentJson}\n\n" +
            (!string.IsNullOrWhiteSpace(_documentText) ? $"ORIGINAL DOCUMENT FULL TEXT (BY PAGE):\n{_documentText}\n\n" : "") +
            $"RULES TO VALIDATE:\n{rulesJson}";

        string responseText = await CallGeminiAsync(systemInstruction, userPrompt, cancellationToken, requireJson: true);
        string cleanedJson = CleanJsonFences(responseText);

        var evaluations = new List<RuleEvaluationResult>();
        try
        {
            var parsed = JsonSerializer.Deserialize<List<RuleEvaluationResult>>(cleanedJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });
            if (parsed != null)
            {
                evaluations.AddRange(parsed);
            }
        }
        catch (JsonException)
        {
            // If the model wrapped the array in an object like {"rules": [...]}
            try
            {
                using var doc = JsonDocument.Parse(cleanedJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var inner = JsonSerializer.Deserialize<List<RuleEvaluationResult>>(prop.Value.GetRawText(), new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            Converters = { new JsonStringEnumConverter() }
                        });
                        if (inner != null) evaluations.AddRange(inner);
                        break;
                    }
                }
            }
            catch { /* Ignore parsing fallback */ }
        }

        // Align each evaluation with original RuleDefinition (ensuring is_knockout and correct max_marks)
        bool knockoutPassed = true;
        string? knockoutFailedReason = null;
        double totalScore = 0;
        double totalMax = 0;

        for (int i = 0; i < request.Rules.Count; i++)
        {
            var ruleDef = request.Rules[i];

            // 1. Try to match by RuleId if not empty
            RuleEvaluationResult? match = null;
            if (!string.IsNullOrWhiteSpace(ruleDef.RuleId))
            {
                match = evaluations.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.RuleId) && 
                    e.RuleId.Equals(ruleDef.RuleId, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Fallback to index match if evaluations returned from Gemini align in order
            if (match == null && i < evaluations.Count)
            {
                match = evaluations[i];
                if (string.IsNullOrWhiteSpace(match.RuleId))
                {
                    match.RuleId = !string.IsNullOrWhiteSpace(ruleDef.RuleId) ? ruleDef.RuleId : $"RULE-{i + 1:D3}";
                }
            }

            if (match == null)
            {
                match = new RuleEvaluationResult
                {
                    RuleId = !string.IsNullOrWhiteSpace(ruleDef.RuleId) ? ruleDef.RuleId : $"RULE-{i + 1:D3}",
                    Category = ruleDef.Category,
                    Status = RuleStatus.Fail,
                    IsKnockout = ruleDef.IsKnockout,
                    MaxMarks = ruleDef.MaxMarks,
                    MarksAwarded = 0,
                    DecisionReason = "Rule was not addressed or omitted in the document evaluation.",
                    MarksReason = "0 marks awarded due to missing documentation.",
                    Finding = "Rule was not addressed or omitted in the document evaluation."
                };
                evaluations.Add(match);
            }

            match.IsKnockout = ruleDef.IsKnockout;
            if (match.MaxMarks <= 0) match.MaxMarks = ruleDef.MaxMarks;
            if (string.IsNullOrWhiteSpace(match.Category)) match.Category = ruleDef.Category;

            // Ensure marks awarded does not exceed max marks
            if (match.MarksAwarded > match.MaxMarks)
            {
                match.MarksAwarded = match.MaxMarks;
            }
            if (match.MarksAwarded < 0)
            {
                match.MarksAwarded = 0;
            }

            // Ensure decision reason and marks reason are populated
            if (string.IsNullOrWhiteSpace(match.DecisionReason))
            {
                match.DecisionReason = match.Finding;
            }
            if (string.IsNullOrWhiteSpace(match.MarksReason))
            {
                match.MarksReason = $"Awarded {match.MarksAwarded:F1} out of {match.MaxMarks:F1} marks based on evaluation.";
            }

            if (match.IsKnockout && match.Status == RuleStatus.Fail)
            {
                knockoutPassed = false;
                knockoutFailedReason ??= $"Failed critical knockout rule '{match.RuleId}': {ruleDef.RuleDescription}";
            }

            totalScore += match.MarksAwarded;
            totalMax += match.MaxMarks;
        }

        double effectiveCap = (request.TotalMarksCap.HasValue && request.TotalMarksCap.Value > 0)
            ? request.TotalMarksCap.Value
            : totalMax;

        double percentage = effectiveCap > 0 ? (totalScore / effectiveCap) * 100.0 : 0.0;
        bool scorePassed = percentage >= request.PassingThreshold;

        string overallVerdict;
        string summary;

        if (!knockoutPassed)
        {
            overallVerdict = "FAIL";
            summary = $"Document failed due to critical knockout rule violation. ({knockoutFailedReason}). Total score: {totalScore:F1}/{effectiveCap:F1} ({percentage:F1}%).";
        }
        else if (scorePassed)
        {
            overallVerdict = "PASS";
            summary = $"Document scored {totalScore:F1}/{effectiveCap:F1} ({percentage:F1}%), meeting the passing threshold of {request.PassingThreshold:F0}%.";
        }
        else
        {
            overallVerdict = "FAIL";
            summary = $"Document scored {totalScore:F1}/{effectiveCap:F1} ({percentage:F1}%), which is below the required {request.PassingThreshold:F0}% threshold.";
        }

        return new RuleValidationResponse
        {
            JobId = _currentDocument?.DocumentType ?? "JOB-EVAL",
            DocumentId = _currentDocument?.OverallSummary ?? "DOC-EVAL",
            OverallVerdict = overallVerdict,
            TotalMarksCap = request.TotalMarksCap,
            TotalScoreAchieved = Math.Round(totalScore, 2),
            TotalMaxScore = Math.Round(effectiveCap, 2),
            Percentage = Math.Round(percentage, 1),
            PassingThreshold = request.PassingThreshold,
            KnockoutPassed = knockoutPassed,
            Summary = summary,
            EvaluatedAt = DateTime.UtcNow,
            RuleResults = evaluations
        };
    }

    private static string CleanJsonFences(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..];
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed[3..];
        }
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
    }

    private async Task<string> CallGeminiAsync(string systemInstruction, string userPrompt, CancellationToken cancellationToken, bool requireJson = false)
    {
        return await _llmClient.GenerateContentAsync(systemInstruction, userPrompt, requireJson, cancellationToken);
    }

    private void EnsureDocumentLoaded()
    {
        if (_currentDocument == null || string.IsNullOrWhiteSpace(_documentJson))
        {
            throw new InvalidOperationException("No document has been loaded into the agent. Call LoadDocument() first.");
        }
    }
}
