import CodeBlock from "./CodeBlock";
import DocsShell, { DocsCallout, DocsSectionCard, EndpointPath, InlineCode } from "./docs/DocsShell";

const SECTIONS = [
  { id: "get-key", label: "Get an API key" },
  { id: "authenticate", label: "Authenticate" },
  { id: "workflow", label: "The workflow" },
  { id: "summarize", label: "Summarize" },
  { id: "ask", label: "Ask" },
  { id: "validate-rules", label: "Validate Rules" },
  { id: "code-examples", label: "Code examples" },
  { id: "errors", label: "Error handling" },
  { id: "good-practices", label: "Good practices" },
];

const PROCESS_CURL = `curl -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -F "File=@contract.pdf" \\
  -F "Instructions=Extract parties, dates, and payment terms"`;

const PROCESS_RESPONSE = `{
  "jobId": "JOB-918B0C27",
  "status": "Succeeded",
  "stitchedEnvelope": {
    "total_pages": 10,
    "document_summary": "...",
    "document_keywords": ["..."],
    "full_markdown": "..."
  },
  "verificationReport": { "pages_with_no_extracted_content": [] }
}`;

const SUMMARIZE_REQUEST = `{
  "FocusArea": "payment terms",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest",
  "Tier": "fast"
}`;

const SUMMARIZE_RESPONSE = `{
  "jobId": "JOB-918B0C27",
  "focusArea": "payment terms",
  "summary": "The agreement specifies a fixed implementation fee of USD 8,400,000 payable via milestones...",
  "generatedAt": "2026-10-26T10:15:00Z"
}`;

const ASK_REQUEST = `{
  "Question": "What is the effective date of this agreement?"
}`;

const ASK_RESPONSE = `{
  "answer": "The agreement's effective date is May 15, 2026.",
  "citations": [
    { "fieldName": "Effective Date", "pageNumber": 1, "confidence": 0.95, "evidence": "Effective Date May 15, 2026" }
  ],
  "warnings": [],
  "isFoundInDocument": true
}`;

const RULES_REQUEST = `{
  "total_marks_cap": 100,
  "passing_threshold": 70,
  "rules": [
    {
      "rule_id": "SEC-01",
      "category": "Security & Compliance",
      "name": "Data encrypted at rest",
      "rule_description": "The agreement must require AES-256 encryption at rest for all stored data.",
      "is_knockout": true,
      "max_marks": 20
    }
  ]
}`;

const RULES_RESPONSE = `{
  "job_id": "JOB-918B0C27",
  "document_id": "DOC-44D8B4AD",
  "overall_verdict": "PASS",
  "total_marks_cap": 100,
  "total_score_achieved": 20,
  "total_max_score": 20,
  "percentage": 100,
  "passing_threshold": 70,
  "knockout_passed": true,
  "summary": "The document satisfies all knockout requirements.",
  "evaluated_at": "2026-10-26T10:20:00Z",
  "rule_results": [
    {
      "rule_id": "SEC-01",
      "status": "Pass",
      "is_knockout": true,
      "marks_awarded": 20,
      "max_marks": 20,
      "finding": "AES-256 encryption at rest is explicitly required in Section 5.2.",
      "page_citations": [5],
      "evidence_quote": "AES-256 encryption at rest for all databases, object storage, backups and archives.",
      "confidence": 0.97
    }
  ]
}`;

const JS_EXAMPLE = `const API_KEY = "pixous_your_key_here";
const BASE_URL = "https://your-server/api";

async function processDocument(file, instructions) {
  const form = new FormData();
  form.append("File", file);
  if (instructions) form.append("Instructions", instructions);

  const res = await fetch(\`\${BASE_URL}/documents/process\`, {
    method: "POST",
    headers: { "X-Document-Api-Key": API_KEY },
    body: form,
  });
  if (!res.ok) throw new Error(\`Processing failed: \${res.status}\`);
  return res.json();
}

async function askQuestion(jobId, question) {
  const res = await fetch(\`\${BASE_URL}/agent/jobs/\${jobId}/ask\`, {
    method: "POST",
    headers: { "X-Document-Api-Key": API_KEY, "Content-Type": "application/json" },
    body: JSON.stringify({ Question: question }),
  });
  if (!res.ok) throw new Error(\`Ask failed: \${res.status}\`);
  return res.json();
}

const result = await processDocument(myFile, "Extract parties and dates");
const answer = await askQuestion(result.jobId, "What is the effective date?");
console.log(answer.answer);`;

const PY_EXAMPLE = `import requests

API_KEY = "pixous_your_key_here"
BASE_URL = "https://your-server/api"
HEADERS = {"X-Document-Api-Key": API_KEY}

def process_document(file_path, instructions=None):
    with open(file_path, "rb") as f:
        files = {"File": f}
        data = {"Instructions": instructions} if instructions else {}
        resp = requests.post(f"{BASE_URL}/documents/process", headers=HEADERS, files=files, data=data)
    resp.raise_for_status()
    return resp.json()

def ask_question(job_id, question):
    resp = requests.post(
        f"{BASE_URL}/agent/jobs/{job_id}/ask",
        headers={**HEADERS, "Content-Type": "application/json"},
        json={"Question": question},
    )
    resp.raise_for_status()
    return resp.json()

result = process_document("contract.pdf", "Extract parties and dates")
answer = ask_question(result["jobId"], "What is the effective date?")
print(answer["answer"])`;

const CSHARP_EXAMPLE = `using var client = new HttpClient { BaseAddress = new Uri("https://your-server/api/") };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", "pixous_your_key_here");

using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract parties and dates"), "Instructions");

var processResponse = await client.PostAsync("documents/process", form);
processResponse.EnsureSuccessStatusCode();
var processResult = await processResponse.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processResult.GetProperty("jobId").GetString()!;

var askBody = JsonContent.Create(new { Question = "What is the effective date?" });
var askResponse = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askResponse.EnsureSuccessStatusCode();
var askResult = await askResponse.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine(askResult.GetProperty("answer").GetString());`;

const ERROR_ROWS = [
  { status: "400", meaning: "Bad request (missing file, empty question, rules exceed marks cap)", action: "Fix the request body/fields per the error message" },
  { status: "401", meaning: "Missing or invalid API key", action: "Check the X-Document-Api-Key header and that the key hasn't been revoked" },
  { status: "403", meaning: "Key doesn't have the required permission", action: "Check the key's permissions in API Access include process/summarize/ask/evaluate" },
  { status: "404", meaning: "Job/document envelope not found", action: "Confirm the jobId is correct and documents/process completed" },
  { status: "429", meaning: "Monthly credit exhausted, or provider rate-limited you", action: "Check Overview for remaining credit, or wait/retry" },
  { status: "500", meaning: "Unexpected server error", action: "Retry; check the details field for the underlying cause" },
];

export default function AgentsGuide() {
  return (
    <DocsShell
      eyebrow="Document Analyst Agents"
      title="Summarize, ask, and audit a processed document"
      description="Call the Agent endpoints — Summarize, Ask, and Validate Rules — from your own application. These agents run after a document has been processed; they answer questions and audit a document grounded in its own content, not general knowledge."
      note="Just want to try this without writing code? Use the Playground — no API key needed."
      meta={[
        { label: "Base path", value: "/api/documents · /api/agent" },
        { label: "Auth header", value: "X-Document-Api-Key" },
      ]}
      sections={SECTIONS}
    >
      <DocsSectionCard id="get-key" index={1} title="Get an API key">
        <ol className="list-decimal space-y-1 pl-5">
          <li>Sign in to the portal (Google or email/password).</li>
          <li>
            Go to <strong>API Access</strong> and create a key, leaving the default permissions
            checked unless you want to restrict it.
          </li>
          <li>Copy the key immediately — it's shown once and can't be recovered.</li>
        </ol>
        <p>
          Every key you create draws from your account's shared credit pool (visible on{" "}
          <strong>Overview</strong>) — the same pool your own Playground testing uses.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="authenticate" index={2} title="Authenticate every request">
        <p>Send your key in the following header on every request:</p>
        <CodeBlock code="X-Document-Api-Key: pixous_your_key_here" />
        <p className="text-xs text-gray-500">
          <InlineCode>Authorization: Bearer &lt;key&gt;</InlineCode> also works as a fallback, but
          the dedicated header is preferred.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="workflow" index={3} title="The workflow: process a document, then use the agents">
        <p>
          The agents operate on a <strong>job</strong> — you can't ask a question or get a summary
          until a document has finished processing and produced a <InlineCode>jobId</InlineCode>.
        </p>
        <EndpointPath method="POST" path="/api/documents/process" />
        <table className="w-full text-left text-xs">
          <thead className="text-gray-500">
            <tr>
              <th className="py-1 pr-4">Field</th>
              <th className="py-1 pr-4">Required</th>
              <th className="py-1">Description</th>
            </tr>
          </thead>
          <tbody>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">File</td>
              <td className="py-1 pr-4">Yes</td>
              <td className="py-1">The PDF file to analyze</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">Instructions</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Natural-language hint, e.g. "Extract parties, dates, and payment terms"</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">Tier</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">"fast" (default) or "premium"</td>
            </tr>
          </tbody>
        </table>
        <CodeBlock language="bash" code={PROCESS_CURL} />
        <p>The response includes the <InlineCode>jobId</InlineCode> you'll need next:</p>
        <CodeBlock language="json" code={PROCESS_RESPONSE} />
        <p className="text-xs text-gray-500">
          Response headers <InlineCode>X-Pages-Processed</InlineCode> and{" "}
          <InlineCode>X-Quota-Remaining</InlineCode> tell you what this call cost.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="summarize" index={4} title="Summarize">
        <EndpointPath method="POST" path="/api/agent/jobs/{jobId}/summarize" />
        <p>Generates an executive summary, optionally focused on one topic. All request fields are optional.</p>
        <p className="text-xs font-medium text-gray-500">Request</p>
        <CodeBlock language="json" code={SUMMARIZE_REQUEST} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={SUMMARIZE_RESPONSE} />
      </DocsSectionCard>

      <DocsSectionCard id="ask" index={5} title="Ask">
        <EndpointPath method="POST" path="/api/agent/jobs/{jobId}/ask" />
        <p>
          Ask a specific question and get a grounded answer with citations.{" "}
          <InlineCode>Question</InlineCode> is required (PascalCase — this endpoint has no
          camelCase alias).
        </p>
        <p className="text-xs font-medium text-gray-500">Request</p>
        <CodeBlock language="json" code={ASK_REQUEST} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={ASK_RESPONSE} />
        <DocsCallout tone="warn">
          If <InlineCode>isFoundInDocument</InlineCode> is <code>false</code>, treat the answer
          with caution — always check this field before trusting an answer in an automated
          pipeline.
        </DocsCallout>
      </DocsSectionCard>

      <DocsSectionCard id="validate-rules" index={6} title="Validate Rules">
        <EndpointPath method="POST" path="/api/agent/jobs/{jobId}/validate-rules" />
        <p>
          Audits the document against a checklist you define — pass/fail rules, knockout
          conditions, and weighted scoring. Useful for compliance checks, vendor scorecards, or
          contract review rubrics.
        </p>
        <p className="text-xs text-gray-500">
          Rules support both <InlineCode>snake_case</InlineCode> and <InlineCode>camelCase</InlineCode>{" "}
          field names interchangeably. The sum of all <InlineCode>max_marks</InlineCode> must not
          exceed <InlineCode>total_marks_cap</InlineCode>, or the request is rejected with 400.
        </p>
        <p className="text-xs font-medium text-gray-500">Request</p>
        <CodeBlock language="json" code={RULES_REQUEST} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={RULES_RESPONSE} />
        <p className="text-xs text-gray-500">
          <InlineCode>status</InlineCode> is one of Pass, Fail, Partial, NotApplicable. If any
          knockout rule fails, <InlineCode>knockout_passed</InlineCode> is false regardless of the
          numeric score.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="code-examples" index={7} title="Code examples">
        <p className="text-xs font-medium text-gray-500">JavaScript (fetch)</p>
        <CodeBlock language="javascript" code={JS_EXAMPLE} />
        <p className="text-xs font-medium text-gray-500">Python (requests)</p>
        <CodeBlock language="python" code={PY_EXAMPLE} />
        <p className="text-xs font-medium text-gray-500">C#</p>
        <CodeBlock language="csharp" code={CSHARP_EXAMPLE} />
      </DocsSectionCard>

      <DocsSectionCard id="errors" index={8} title="Error handling">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs">
            <thead className="text-gray-500">
              <tr>
                <th className="py-1 pr-4">Status</th>
                <th className="py-1 pr-4">Meaning</th>
                <th className="py-1">What to do</th>
              </tr>
            </thead>
            <tbody>
              {ERROR_ROWS.map((row) => (
                <tr key={row.status} className="border-t border-gray-100">
                  <td className="py-1.5 pr-4 font-mono font-semibold">{row.status}</td>
                  <td className="py-1.5 pr-4">{row.meaning}</td>
                  <td className="py-1.5">{row.action}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="text-xs text-gray-500">
          Every error response includes <InlineCode>{"{ error, message/details }"}</InlineCode> —
          always surface that text rather than just the status code.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="good-practices" index={9} title="Good practices">
        <ul className="list-disc space-y-1 pl-5">
          <li>
            <strong>Reuse the jobId.</strong> Processing a document is the expensive,
            credit-consuming step. Once you have a jobId, call Summarize/Ask/Validate Rules on it
            as many times as you want at no additional page cost.
          </li>
          <li>
            <strong>Check isFoundInDocument</strong> on Ask responses before using the answer in
            any automated decision.
          </li>
          <li>
            <strong>Check pages_with_no_extracted_content</strong> on the processing response — if
            pages are listed there, summaries/answers may be missing information from those pages.
          </li>
          <li>
            <strong>Don't hardcode a model/provider</strong> unless you need to — omitting them
            lets the server use its configured defaults, tunable centrally without you
            redeploying.
          </li>
        </ul>
      </DocsSectionCard>
    </DocsShell>
  );
}
