# Using the Document Analyst Agents — API Guide

This guide shows you, as a developer integrating with the Pixous Document Intelligence API, how
to properly access the **Agent** endpoints: Summarize, Ask, and Validate Rules. These agents run
*after* a document has been processed — they answer questions, summarize, and audit a document
that's already been analyzed, grounded in the document's own content rather than general
knowledge.

If you just want to click around and try this without writing code, use the **Playground** in the
web portal instead — it does all of this for you with no API key needed. This guide is for calling
the API directly from your own application.

> Looking to *author* a new document (NDA, SOW, offer letter, etc.) from a template instead of
> analyzing an existing one? See the [Document Generation API Guide](DOCUMENT_GENERATION_API_GUIDE.md).

---

## 1. Get an API key

1. Sign in to the portal (Google or email/password).
2. Go to **API Access** and create a key. Give it a name and leave the default permissions
   (`process`, `summarize`, `ask`, `evaluate`) checked unless you specifically want to restrict it.
3. **Copy the key immediately** — it's shown once and can't be recovered. If you lose it, rotate it
   to get a new one.

Every key you create draws from your account's shared credit pool (visible on the **Overview**
page) — the same pool your own Playground testing uses.

## 2. Authenticate every request

Send your key in the `X-Document-Api-Key` header on every request:

```
X-Document-Api-Key: pixous_your_key_here
```

(`Authorization: Bearer <key>` also works as a fallback, but the dedicated header is preferred.)

## 3. The workflow: process a document, then use the agents

The agents operate on a **job** — you can't ask a question or get a summary until a document has
finished processing and produced a `jobId`.

```
┌─────────────────────┐     ┌──────────────────────┐     ┌───────────────────────────┐
│ 1. Upload & process  │ --> │ 2. Get the jobId back │ --> │ 3. Call the agents on it  │
│ POST /documents/     │     │  (from step 1's       │     │  /agent/jobs/{jobId}/...  │
│      process         │     │   response)           │     │                            │
└─────────────────────┘     └──────────────────────┘     └───────────────────────────┘
```

### Step 1 — Process a document

```
POST /api/documents/process
Content-Type: multipart/form-data
```

| Field          | Required | Description                                                              |
| -------------- | -------- | ------------------------------------------------------------------------- |
| `File`         | Yes      | The PDF file to analyze                                                   |
| `Instructions` | No       | Natural-language hint, e.g. `"Extract parties, dates, and payment terms"` |
| `Tier`         | No       | `"fast"` (default) or `"premium"`                                         |
| `Provider`     | No       | `"Gemini"` or `"OpenAI"` — defaults to server configuration               |

```bash
curl -X POST https://your-server/api/documents/process \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -F "File=@contract.pdf" \
  -F "Instructions=Extract parties, dates, and payment terms"
```

The response is a large JSON object. The two fields you need for the next step:

```json
{
  "jobId": "JOB-918B0C27",
  "status": "Succeeded",
  "stitchedEnvelope": {
    "total_pages": 10,
    "document_summary": "...",
    "document_keywords": ["..."],
    "full_markdown": "..."
  },
  "verificationReport": { "pages_with_no_extracted_content": [] }
}
```

`status` will be `"Succeeded"` or `"NeedsReview"` (check `verificationReport.review_tasks` for why
if it's the latter — e.g. a page didn't transcribe). Either way, a `jobId` is returned and the
agents below will work against whatever was extracted.

Response headers also tell you what this call cost:

| Header               | Meaning                                  |
| --------------------- | ----------------------------------------- |
| `X-Pages-Processed`  | Pages billed for this call                |
| `X-Quota-Remaining`  | Your account's remaining credit after this call |

### Step 2 (optional) — Check job status later

If you need to look up a job again later (e.g. from a different process), you can refetch it:

```bash
curl https://your-server/api/documents/jobs/JOB-918B0C27 \
  -H "X-Document-Api-Key: pixous_your_key_here"
```

---

## 4. The Agents

All three agent endpoints require the `jobId` from step 1, and all are grounded in that
document's own content — not general knowledge.

### 4.1 Summarize

```
POST /api/agent/jobs/{jobId}/summarize
```

Generates an executive summary, optionally focused on one topic.

**Request body** (all fields optional):

```json
{
  "FocusArea": "payment terms",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest",
  "Tier": "fast"
}
```

> Field names here are shown in **PascalCase**, matching the underlying model exactly — this is
> the form verified to work from this project's own Playground integration. (Response bodies below
> are always camelCase, and Validate Rules' request explicitly accepts both `snake_case` and
> `camelCase` via declared aliases — this endpoint doesn't declare any, so stick to the exact names
> shown here.)

Leave the body empty (`{}`) for a general summary of the whole document.

**Response:**

```json
{
  "jobId": "JOB-918B0C27",
  "focusArea": "payment terms",
  "summary": "The agreement specifies a fixed implementation fee of USD 8,400,000 payable via milestones, plus a managed-services fee of USD 360,000 per quarter...",
  "generatedAt": "2026-10-26T10:15:00Z"
}
```

```bash
curl -X POST https://your-server/api/agent/jobs/JOB-918B0C27/summarize \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"FocusArea": "payment terms"}'
```

### 4.2 Ask

```
POST /api/agent/jobs/{jobId}/ask
```

Ask a specific question and get a grounded answer with citations.

**Request body:**

```json
{
  "Question": "What is the effective date of this agreement?"
}
```

`Question` is required; everything else (`Provider`, `Model`, `ApiKey`) is optional. Field names
are PascalCase here, same as Summarize above.

**Response:**

```json
{
  "answer": "The agreement's effective date is May 15, 2026.",
  "citations": [
    { "fieldName": "Effective Date", "pageNumber": 1, "confidence": 0.95, "evidence": "Effective Date May 15, 2026" }
  ],
  "warnings": [],
  "isFoundInDocument": true
}
```

If `isFoundInDocument` is `false`, treat the answer with caution — the agent is telling you it
couldn't confidently ground the response in the document. Always check this field before trusting
an answer in an automated pipeline.

```bash
curl -X POST https://your-server/api/agent/jobs/JOB-918B0C27/ask \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"Question": "What is the effective date of this agreement?"}'
```

### 4.3 Validate Rules

```
POST /api/agent/jobs/{jobId}/validate-rules
```

Audits the document against a checklist you define — pass/fail rules, knockout conditions, and
weighted scoring. Useful for compliance checks, vendor scorecards, or contract review rubrics.

**Request body:**

```json
{
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
    },
    {
      "rule_id": "FIN-01",
      "category": "Financial Terms",
      "name": "Payment terms defined",
      "rule_description": "The agreement must specify a clear payment schedule and currency.",
      "is_knockout": false,
      "max_marks": 15
    }
  ]
}
```

Rules support both `snake_case` and `camelCase` field names interchangeably (`rule_id`/`ruleId`,
`max_marks`/`maxMarks`, etc.) — use whichever matches your own codebase's convention. The sum of
all `max_marks` must not exceed `total_marks_cap`, or the request is rejected with `400`.

**Response:**

```json
{
  "job_id": "JOB-918B0C27",
  "document_id": "DOC-44D8B4AD",
  "overall_verdict": "PASS",
  "total_marks_cap": 100,
  "total_score_achieved": 85,
  "total_max_score": 35,
  "percentage": 85,
  "passing_threshold": 70,
  "knockout_passed": true,
  "summary": "The document satisfies all knockout requirements and scores above the passing threshold.",
  "evaluated_at": "2026-10-26T10:20:00Z",
  "rule_results": [
    {
      "rule_id": "SEC-01",
      "category": "Security & Compliance",
      "status": "Pass",
      "is_knockout": true,
      "marks_awarded": 20,
      "max_marks": 20,
      "finding": "AES-256 encryption at rest is explicitly required in Section 5.2.",
      "decision_reason": "Explicit requirement found matching the rule.",
      "marks_reason": "Full marks awarded — requirement is unambiguous.",
      "page_citations": [5],
      "evidence_quote": "AES-256 encryption at rest for all databases, object storage, backups and archives.",
      "confidence": 0.97
    }
  ]
}
```

`status` is one of `Pass`, `Fail`, `Partial`, `NotApplicable`. If any knockout rule fails,
`knockout_passed` is `false` and `overall_verdict` reflects that regardless of the numeric score.

```bash
curl -X POST https://your-server/api/agent/jobs/JOB-918B0C27/validate-rules \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "total_marks_cap": 100,
    "passing_threshold": 70,
    "rules": [
      { "rule_id": "SEC-01", "category": "Security", "rule_description": "Requires AES-256 encryption at rest.", "is_knockout": true, "max_marks": 20 }
    ]
  }'
```

---

## 5. Code examples

### JavaScript (fetch)

```javascript
const API_KEY = "pixous_your_key_here";
const BASE_URL = "https://your-server/api";

async function processDocument(file, instructions) {
  const form = new FormData();
  form.append("File", file);
  if (instructions) form.append("Instructions", instructions);

  const res = await fetch(`${BASE_URL}/documents/process`, {
    method: "POST",
    headers: { "X-Document-Api-Key": API_KEY },
    body: form,
  });
  if (!res.ok) throw new Error(`Processing failed: ${res.status}`);
  return res.json();
}

async function askQuestion(jobId, question) {
  const res = await fetch(`${BASE_URL}/agent/jobs/${jobId}/ask`, {
    method: "POST",
    headers: {
      "X-Document-Api-Key": API_KEY,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ Question: question }),
  });
  if (!res.ok) throw new Error(`Ask failed: ${res.status}`);
  return res.json();
}

// Usage
const result = await processDocument(myFile, "Extract parties and dates");
const answer = await askQuestion(result.jobId, "What is the effective date?");
console.log(answer.answer);
```

### Python (requests)

```python
import requests

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
print(answer["answer"])
```

### C#

```csharp
using var client = new HttpClient { BaseAddress = new Uri("https://your-server/api/") };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", "pixous_your_key_here");

// 1. Process a document
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract parties and dates"), "Instructions");

var processResponse = await client.PostAsync("documents/process", form);
processResponse.EnsureSuccessStatusCode();
var processResult = await processResponse.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processResult.GetProperty("jobId").GetString()!;

// 2. Ask a question
var askBody = JsonContent.Create(new { Question = "What is the effective date?" });
var askResponse = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askResponse.EnsureSuccessStatusCode();
var askResult = await askResponse.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine(askResult.GetProperty("answer").GetString());
```

---

## 6. Error handling

| Status | Meaning                                              | What to do                                                                 |
| ------ | ----------------------------------------------------- | --------------------------------------------------------------------------- |
| `400`  | Bad request (missing file, empty question, rules exceed marks cap) | Fix the request body/fields per the `error` message                        |
| `401`  | Missing or invalid API key                           | Check the `X-Document-Api-Key` header is present and the key hasn't been revoked |
| `403`  | Key doesn't have the required permission             | Check the key's permissions in API Access include `process`/`summarize`/`ask`/`evaluate` as needed |
| `404`  | Job/document envelope not found                      | Confirm the `jobId` is correct and that `documents/process` actually completed |
| `429`  | Monthly credit exhausted, or the underlying LLM provider rate-limited you | Check `Overview` for remaining credit, or wait/retry for provider-side limits |
| `500`  | Unexpected server error                              | Retry; if persistent, check the `details` field in the response for the underlying cause |

Every error response includes `{ "error": "...", "message"/"details": "..." }` — always surface
the `error`/`message` text rather than just the status code, since it tells you exactly what went
wrong.

## 7. Good practices

- **Reuse the `jobId`.** Processing a document is the expensive, credit-consuming step. Once you
  have a `jobId`, call Summarize/Ask/Validate Rules on it as many times as you want at no
  additional page cost.
- **Check `isFoundInDocument` on Ask responses** before using the answer in any automated
  decision — don't treat a fluent-sounding answer as ground truth without checking this flag.
- **Check `verificationReport.pages_with_no_extracted_content`** on the processing response — if
  pages are listed there, the document may not have transcribed completely, and summaries/answers
  may be missing information from those pages.
- **Don't hardcode a model/provider** unless you need to — omitting `provider`/`model`/`tier` lets
  the server use its configured defaults, which can be tuned centrally without you redeploying.
