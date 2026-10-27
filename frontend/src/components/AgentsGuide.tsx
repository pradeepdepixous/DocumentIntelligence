import CodeBlock from "./CodeBlock";
import MethodBadge from "./MethodBadge";

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

function Section({ id, title, children }: { id: string; title: string; children: React.ReactNode }) {
  return (
    <section id={id} className="scroll-mt-6 rounded-xl border border-gray-200 bg-white p-6">
      <h3 className="text-lg font-semibold text-gray-900">{title}</h3>
      <div className="mt-3 space-y-3 text-sm text-gray-700">{children}</div>
    </section>
  );
}

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
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-[180px_1fr]">
      <nav className="hidden lg:block">
        <div className="sticky top-6 space-y-1">
          {SECTIONS.map((s) => (
            <a
              key={s.id}
              href={`#${s.id}`}
              className="block rounded-md px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-100 hover:text-gray-900"
            >
              {s.label}
            </a>
          ))}
        </div>
      </nav>

      <div className="space-y-6">
        <div className="rounded-xl border border-gray-200 bg-white p-6">
          <h2 className="text-xl font-semibold text-gray-900">Using the Document Analyst Agents</h2>
          <p className="mt-2 text-sm text-gray-600">
            This guide shows you how to call the <strong>Agent</strong> endpoints — Summarize, Ask, and Validate Rules —
            from your own application. These agents run <em>after</em> a document has been processed; they answer
            questions and audit a document grounded in its own content, not general knowledge.
          </p>
          <p className="mt-2 text-sm text-gray-500">
            Just want to try this without writing code? Use the <strong>Playground</strong> instead — no API key needed.
          </p>
        </div>

        <Section id="get-key" title="1. Get an API key">
          <ol className="list-decimal space-y-1 pl-5">
            <li>Sign in to the portal (Google or email/password).</li>
            <li>
              Go to <strong>API Access</strong> and create a key, leaving the default permissions checked unless you
              want to restrict it.
            </li>
            <li>Copy the key immediately — it's shown once and can't be recovered.</li>
          </ol>
          <p>
            Every key you create draws from your account's shared credit pool (visible on <strong>Overview</strong>) —
            the same pool your own Playground testing uses.
          </p>
        </Section>

        <Section id="authenticate" title="2. Authenticate every request">
          <p>Send your key in the following header on every request:</p>
          <CodeBlock code="X-Document-Api-Key: pixous_your_key_here" />
          <p className="text-xs text-gray-500">
            <code className="rounded bg-gray-100 px-1 py-0.5">Authorization: Bearer &lt;key&gt;</code> also works as a
            fallback, but the dedicated header is preferred.
          </p>
        </Section>

        <Section id="workflow" title="3. The workflow: process a document, then use the agents">
          <p>
            The agents operate on a <strong>job</strong> — you can't ask a question or get a summary until a document
            has finished processing and produced a <code className="rounded bg-gray-100 px-1 py-0.5">jobId</code>.
          </p>
          <div className="flex items-center gap-2">
            <MethodBadge method="POST" />
            <code className="text-xs">/api/documents/process</code>
          </div>
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
          <p>The response includes the <code className="rounded bg-gray-100 px-1 py-0.5">jobId</code> you'll need next:</p>
          <CodeBlock language="json" code={PROCESS_RESPONSE} />
          <p className="text-xs text-gray-500">
            Response headers <code className="rounded bg-gray-100 px-1 py-0.5">X-Pages-Processed</code> and{" "}
            <code className="rounded bg-gray-100 px-1 py-0.5">X-Quota-Remaining</code> tell you what this call cost.
          </p>
        </Section>

        <Section id="summarize" title="4. Summarize">
          <div className="flex items-center gap-2">
            <MethodBadge method="POST" />
            <code className="text-xs">/api/agent/jobs/&#123;jobId&#125;/summarize</code>
          </div>
          <p>Generates an executive summary, optionally focused on one topic. All request fields are optional.</p>
          <p className="text-xs font-medium text-gray-500">Request</p>
          <CodeBlock language="json" code={SUMMARIZE_REQUEST} />
          <p className="text-xs font-medium text-gray-500">Response</p>
          <CodeBlock language="json" code={SUMMARIZE_RESPONSE} />
        </Section>

        <Section id="ask" title="5. Ask">
          <div className="flex items-center gap-2">
            <MethodBadge method="POST" />
            <code className="text-xs">/api/agent/jobs/&#123;jobId&#125;/ask</code>
          </div>
          <p>
            Ask a specific question and get a grounded answer with citations.{" "}
            <code className="rounded bg-gray-100 px-1 py-0.5">Question</code> is required (PascalCase — this
            endpoint has no camelCase alias).
          </p>
          <p className="text-xs font-medium text-gray-500">Request</p>
          <CodeBlock language="json" code={ASK_REQUEST} />
          <p className="text-xs font-medium text-gray-500">Response</p>
          <CodeBlock language="json" code={ASK_RESPONSE} />
          <p className="rounded-md bg-amber-50 p-3 text-xs text-amber-800">
            If <code className="rounded bg-white px-1 py-0.5">isFoundInDocument</code> is <code>false</code>, treat the
            answer with caution — always check this field before trusting an answer in an automated pipeline.
          </p>
        </Section>

        <Section id="validate-rules" title="6. Validate Rules">
          <div className="flex items-center gap-2">
            <MethodBadge method="POST" />
            <code className="text-xs">/api/agent/jobs/&#123;jobId&#125;/validate-rules</code>
          </div>
          <p>
            Audits the document against a checklist you define — pass/fail rules, knockout conditions, and weighted
            scoring. Useful for compliance checks, vendor scorecards, or contract review rubrics.
          </p>
          <p className="text-xs text-gray-500">
            Rules support both <code className="rounded bg-gray-100 px-1 py-0.5">snake_case</code> and{" "}
            <code className="rounded bg-gray-100 px-1 py-0.5">camelCase</code> field names interchangeably. The sum of
            all <code className="rounded bg-gray-100 px-1 py-0.5">max_marks</code> must not exceed{" "}
            <code className="rounded bg-gray-100 px-1 py-0.5">total_marks_cap</code>, or the request is rejected with 400.
          </p>
          <p className="text-xs font-medium text-gray-500">Request</p>
          <CodeBlock language="json" code={RULES_REQUEST} />
          <p className="text-xs font-medium text-gray-500">Response</p>
          <CodeBlock language="json" code={RULES_RESPONSE} />
          <p className="text-xs text-gray-500">
            <code className="rounded bg-gray-100 px-1 py-0.5">status</code> is one of Pass, Fail, Partial,
            NotApplicable. If any knockout rule fails, <code className="rounded bg-gray-100 px-1 py-0.5">knockout_passed</code>{" "}
            is false regardless of the numeric score.
          </p>
        </Section>

        <Section id="code-examples" title="7. Code examples">
          <p className="text-xs font-medium text-gray-500">JavaScript (fetch)</p>
          <CodeBlock language="javascript" code={JS_EXAMPLE} />
          <p className="text-xs font-medium text-gray-500">Python (requests)</p>
          <CodeBlock language="python" code={PY_EXAMPLE} />
          <p className="text-xs font-medium text-gray-500">C#</p>
          <CodeBlock language="csharp" code={CSHARP_EXAMPLE} />
        </Section>

        <Section id="errors" title="8. Error handling">
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
            Every error response includes <code className="rounded bg-gray-100 px-1 py-0.5">{"{ error, message/details }"}</code> —
            always surface that text rather than just the status code.
          </p>
        </Section>

        <Section id="good-practices" title="9. Good practices">
          <ul className="list-disc space-y-1 pl-5">
            <li>
              <strong>Reuse the jobId.</strong> Processing a document is the expensive, credit-consuming step. Once you
              have a jobId, call Summarize/Ask/Validate Rules on it as many times as you want at no additional page cost.
            </li>
            <li>
              <strong>Check isFoundInDocument</strong> on Ask responses before using the answer in any automated decision.
            </li>
            <li>
              <strong>Check pages_with_no_extracted_content</strong> on the processing response — if pages are listed
              there, summaries/answers may be missing information from those pages.
            </li>
            <li>
              <strong>Don't hardcode a model/provider</strong> unless you need to — omitting them lets the server use
              its configured defaults, tunable centrally without you redeploying.
            </li>
          </ul>
        </Section>
      </div>
    </div>
  );
}
