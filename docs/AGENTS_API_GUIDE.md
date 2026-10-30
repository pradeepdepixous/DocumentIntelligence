# Using the Document Analyst Agents — API Guide

This guide shows you, as a developer integrating with the Pixous Document Intelligence API, how to properly integrate the **Document Processing** and **Agent** endpoints (Summarize, Ask, and Validate Rules) into your applications.

---

## 1. Quick Reference: Endpoints & Permissions

| Method | Endpoint | Permission | Purpose & Payload | Key Response Fields |
| :--- | :--- | :--- | :--- | :--- |
| `POST` | `/api/documents/process` | `process` | Uploads and processes a PDF (multipart/form-data: `File`, `Instructions`, `Tier`) | `jobId`, `status`, `stitchedEnvelope`, `verificationReport` |
| `GET` | `/api/documents/jobs/{jobId}` | `process` | Retrieves the full extraction envelope & audit report for an existing job | Full job record, `stitchedEnvelope`, `verificationReport` |
| `GET` | `/api/documents/jobs/{jobId}/result` | `process` | Polls status for asynchronous batch processing jobs | `200 OK` (completed) or `202 Accepted` + `Retry-After` header |
| `POST` | `/api/agent/jobs/{jobId}/summarize` | `summarize` | Generates an executive or focus-specific summary (JSON: `FocusArea`, `Tier`, `Model`) | `jobId`, `focusArea`, `summary`, `generatedAt` |
| `POST` | `/api/agent/jobs/{jobId}/ask` | `ask` | Grounded Q&A against verified document content (JSON: `Question`, `ApiKey`) | `answer`, `citations`, `isFoundInDocument`, `warnings` |
| `POST` | `/api/agent/jobs/{jobId}/validate-rules` | `evaluate` | Audits document compliance against custom rules & knockout criteria | `overall_verdict`, `total_score_achieved`, `rule_results` |

---

## 2. Architecture & Security: Frontend vs. Backend

> [!CAUTION]
> **API Key Security Warning**
> Never embed your Pixous API key (`X-Document-Api-Key`) or your personal Gemini/OpenAI key (`X-LLM-ApiKey`) in client-side code (browsers, React, Angular, Vue, mobile apps, or public GitHub repositories). Anyone inspecting network traffic or decompiling the client bundle can extract the key and consume your credits.

### Recommended Architecture Pattern

When building web or mobile applications, always route requests through your own backend service:

```text
┌─────────────────────────────────┐
│     User's Client / Frontend    │
│  (React / Angular / Vue / iOS)  │
└────────────────┬────────────────┘
                 │ 1. User uploads document (Authenticated via your app's session/JWT)
                 ▼
┌─────────────────────────────────┐
│      User's Backend Server      │  ◄── Securely holds PIXOUS_API_KEY
│  (Node.js / Python / .NET / Go) │      in environment variables (.env)
└────────────────┬────────────────┘
                 │ 2. Backend calls Pixous API with X-Document-Api-Key
                 ▼
┌─────────────────────────────────┐
│   Pixous Document Intelligence  │
│  (/documents, /agent/jobs/...)  │
└─────────────────────────────────┘
```

1. **Client to Backend**: Your frontend sends documents and questions to your backend using your application's existing authentication (session cookies, user JWT, etc.).
2. **Backend to Pixous**: Your backend securely attaches `X-Document-Api-Key` and relays the request to the Pixous API.
3. **Backend to Client**: Your backend returns the structured results (answers, citations, summaries) to your frontend.

For trusted **server-to-server** background workers, scheduled cron jobs, or CLI tools, your backend can call Pixous directly without an intermediary.

---

## 3. Base URL & Environment Configuration

Configure the Pixous Base URL in your application environment settings so you can switch between local development and production without modifying code.

### Standard Endpoints
- **Local Development**: `http://localhost:5000/api`
- **Production Server**: `https://<your-pixous-host>/api`

### Configuring in Code

#### Node.js / JavaScript
```javascript
// Load from .env file or environment
const PIXOUS_BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const PIXOUS_API_KEY = process.env.PIXOUS_API_KEY;
```

#### Python
```python
import os

PIXOUS_BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
PIXOUS_API_KEY = os.environ.get("PIXOUS_API_KEY")
```

#### C# / .NET
```csharp
// In appsettings.json: { "Pixous": { "BaseUrl": "http://localhost:5000/api", "ApiKey": "pixous_..." } }
var baseUrl = builder.Configuration["Pixous:BaseUrl"] ?? "http://localhost:5000/api";
var apiKey = builder.Configuration["Pixous:ApiKey"];
```

---

## 4. Authentication & Bring-Your-Own-Key (BYOK)

### 4.1 Pixous API Key
Every request must include your Pixous API key in the `X-Document-Api-Key` header:

```http
X-Document-Api-Key: pixous_your_key_here
```

(`Authorization: Bearer <key>` is also accepted as a fallback). Obtain keys in the web portal under **API Access**.

### 4.2 Bring Your Own LLM Key (Gemini / OpenAI)
By default, the server processes documents and runs Agent queries using the server's configured LLM credentials. You can optionally supply your personal **Google Gemini** or **OpenAI** key to use your own quota directly.

You can supply your personal key in three ways:

- **Via HTTP Header (Recommended for external apps)**:
  ```http
  X-Document-Api-Key: pixous_your_key_here
  X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere
  X-LLM-Provider: Gemini
  X-LLM-Model: gemini-flash-lite-latest
  ```
- **Via Request Body (For Agent endpoints)**:
  ```json
  {
    "Question": "What is the effective date of this agreement?",
    "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
    "Provider": "Gemini",
    "Model": "gemini-flash-lite-latest"
  }
  ```
- **Server-Side Default (For self-hosted instances)**:
  Set `GEMINI_API_KEY` in environment variables or `Gemini:ApiKey` in `appsettings.Development.json`.

---

## 5. Complete End-to-End Integration Flow

Here are complete, runnable examples showing the full lifecycle: upload PDF &rarr; receive `jobId` &rarr; ask a grounded question &rarr; print answer with citations.

### Option A: Default Implementation (Server Key)

Uses the server's configured Gemini key. Requires only `X-Document-Api-Key`:

#### Python (requests)
```python
import os, requests

BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
API_KEY = os.environ.get("PIXOUS_API_KEY", "pixous_your_key_here")
headers = { "X-Document-Api-Key": API_KEY }

# 1. Upload & Process PDF
with open("contract.pdf", "rb") as f:
    resp = requests.post(
        f"{BASE_URL}/documents/process",
        headers=headers,
        files={"File": ("contract.pdf", f, "application/pdf")},
        data={"Instructions": "Extract dates, parties, and payment terms"}
    )
    resp.raise_for_status()
    job_id = resp.json()["jobId"]

# 2. Ask Grounded Question
ask_resp = requests.post(
    f"{BASE_URL}/agent/jobs/{job_id}/ask",
    headers={**headers, "Content-Type": "application/json"},
    json={"Question": "What is the agreement termination policy?"}
)
ask_resp.raise_for_status()
data = ask_resp.json()
print("ANSWER:", data["answer"])
```

#### JavaScript (Node.js fetch)
```javascript
import fs from "fs";
import path from "path";

const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const API_KEY = process.env.PIXOUS_API_KEY || "pixous_your_key_here";

const fileBuffer = fs.readFileSync("contract.pdf");
const blob = new Blob([fileBuffer], { type: "application/pdf" });

// 1. Upload & Process PDF
const form = new FormData();
form.append("File", blob, "contract.pdf");
form.append("Instructions", "Extract dates, parties, and payment terms");

const processRes = await fetch(`${BASE_URL}/documents/process`, {
  method: "POST",
  headers: { "X-Document-Api-Key": API_KEY },
  body: form,
});
const { jobId } = await processRes.json();

// 2. Ask Grounded Question
const askRes = await fetch(`${BASE_URL}/agent/jobs/${jobId}/ask`, {
  method: "POST",
  headers: { "X-Document-Api-Key": API_KEY, "Content-Type": "application/json" },
  body: JSON.stringify({ Question: "What is the agreement termination policy?" }),
});
const data = await askRes.json();
console.log("ANSWER:\n", data.answer);
```

#### C# (.NET HttpClient)
```csharp
using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = Environment.GetEnvironmentVariable("PIXOUS_BASE_URL") ?? "http://localhost:5000/api/";
var apiKey = Environment.GetEnvironmentVariable("PIXOUS_API_KEY") ?? "pixous_your_key_here";

using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", apiKey);

// 1. Upload & Process PDF
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract dates, parties, and payment terms"), "Instructions");

var processRes = await client.PostAsync("documents/process", form);
processRes.EnsureSuccessStatusCode();
var processDoc = await processRes.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processDoc.GetProperty("jobId").GetString()!;

// 2. Ask Grounded Question
var askBody = JsonContent.Create(new { Question = "What is the agreement termination policy?" });
var askRes = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askRes.EnsureSuccessStatusCode();
var askDoc = await askRes.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine("ANSWER:\n" + askDoc.GetProperty("answer").GetString());
```

#### cURL (Bash)
```bash
# 1. Upload and process PDF
JOB_ID=$(curl -s -X POST https://your-server/api/documents/process \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -F "File=@contract.pdf" \
  -F "Instructions=Extract dates, parties, and payment terms" | jq -r '.jobId')

echo "Processed! Job ID: $JOB_ID"

# 2. Ask Grounded Question
curl -X POST "https://your-server/api/agent/jobs/$JOB_ID/ask" \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"Question": "What is the agreement termination policy?"}' | jq
```

---

### Option B: Own Key Implementation (BYOK Gemini)

Passes your personal Google Gemini API key via `X-LLM-ApiKey` alongside `X-Document-Api-Key`:

#### Python (requests)
```python
import os, requests

BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
PIXOUS_API_KEY = os.environ.get("PIXOUS_API_KEY", "pixous_your_key_here")
GEMINI_KEY = os.environ.get("GEMINI_API_KEY", "AIzaSyYourPersonalGeminiKeyHere")

headers = {
    "X-Document-Api-Key": PIXOUS_API_KEY,
    "X-LLM-ApiKey": GEMINI_KEY,
    "X-LLM-Provider": "Gemini",
    "X-LLM-Model": "gemini-flash-lite-latest"
}

# 1. Upload & Process PDF (Executed on your personal Gemini quota)
with open("contract.pdf", "rb") as f:
    resp = requests.post(
        f"{BASE_URL}/documents/process",
        headers=headers,
        files={"File": ("contract.pdf", f, "application/pdf")},
        data={"Instructions": "Extract dates, parties, and payment terms"}
    )
    resp.raise_for_status()
    job_id = resp.json()["jobId"]

# 2. Ask Grounded Question (Executed on your personal Gemini quota)
ask_resp = requests.post(
    f"{BASE_URL}/agent/jobs/{job_id}/ask",
    headers={**headers, "Content-Type": "application/json"},
    json={"Question": "What is the agreement termination policy?"}
)
ask_resp.raise_for_status()
data = ask_resp.json()
print("ANSWER:", data["answer"])
```

#### JavaScript (Node.js fetch)
```javascript
import fs from "fs";
import path from "path";

const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const API_KEY = process.env.PIXOUS_API_KEY || "pixous_your_key_here";
const GEMINI_KEY = process.env.GEMINI_API_KEY || "AIzaSyYourPersonalGeminiKeyHere";

const headers = {
  "X-Document-Api-Key": API_KEY,
  "X-LLM-ApiKey": GEMINI_KEY,
  "X-LLM-Provider": "Gemini",
  "X-LLM-Model": "gemini-flash-lite-latest",
};

const fileBuffer = fs.readFileSync("contract.pdf");
const blob = new Blob([fileBuffer], { type: "application/pdf" });

// 1. Upload & Process PDF with personal Gemini key
const form = new FormData();
form.append("File", blob, "contract.pdf");
form.append("Instructions", "Extract dates, parties, and payment terms");

const processRes = await fetch(`${BASE_URL}/documents/process`, {
  method: "POST",
  headers,
  body: form,
});
const { jobId } = await processRes.json();

// 2. Ask Grounded Question with personal Gemini key
const askRes = await fetch(`${BASE_URL}/agent/jobs/${jobId}/ask`, {
  method: "POST",
  headers: { ...headers, "Content-Type": "application/json" },
  body: JSON.stringify({ Question: "What is the agreement termination policy?" }),
});
const data = await askRes.json();
console.log("ANSWER:\n", data.answer);
```

#### C# (.NET HttpClient)
```csharp
using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = Environment.GetEnvironmentVariable("PIXOUS_BASE_URL") ?? "http://localhost:5000/api/";
var apiKey = Environment.GetEnvironmentVariable("PIXOUS_API_KEY") ?? "pixous_your_key_here";
var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "AIzaSyYourPersonalGeminiKeyHere";

using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", apiKey);
client.DefaultRequestHeaders.Add("X-LLM-ApiKey", geminiKey);
client.DefaultRequestHeaders.Add("X-LLM-Provider", "Gemini");

// 1. Upload & Process PDF with personal Gemini key
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract dates, parties, and payment terms"), "Instructions");

var processRes = await client.PostAsync("documents/process", form);
processRes.EnsureSuccessStatusCode();
var processDoc = await processRes.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processDoc.GetProperty("jobId").GetString()!;

// 2. Ask Grounded Question with personal Gemini key
var askBody = JsonContent.Create(new { Question = "What is the agreement termination policy?" });
var askRes = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askRes.EnsureSuccessStatusCode();
var askDoc = await askRes.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine("ANSWER:\n" + askDoc.GetProperty("answer").GetString());
```

#### cURL (Bash)
```bash
# 1. Upload and process PDF using personal Gemini key
JOB_ID=$(curl -s -X POST https://your-server/api/documents/process \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "X-LLM-Provider: Gemini" \
  -H "X-LLM-Model: gemini-flash-lite-latest" \
  -F "File=@contract.pdf" \
  -F "Instructions=Extract dates, parties, and payment terms" | jq -r '.jobId')

echo "Processed! Job ID: $JOB_ID"

# 2. Ask Grounded Question using personal Gemini key
curl -X POST "https://your-server/api/agent/jobs/$JOB_ID/ask" \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "Content-Type: application/json" \
  -d '{"Question": "What is the agreement termination policy?"}' | jq
```

---

## 6. The API Workflow: Step-by-Step

### Step 1: Upload & Process Document

```http
POST /api/documents/process
Content-Type: multipart/form-data
```

| Parameter | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `File` | File binary | Yes | The PDF document to analyze |
| `Instructions` | String | No | Natural-language guidance (e.g. `"Extract insurance limits"`) |
| `Tier` | String | No | `"fast"` (default) or `"premium"` |
| `Provider` | String | No | `"Gemini"` (default) or `"OpenAI"` |
| `ApiKey` | String | No | Optional per-request personal LLM key override |
| `Async` | Boolean | No | Set to `true` (or header `Prefer: respond-async`) for background processing |

#### Implementation Mode A: Default (Server Key)
```bash
curl -X POST https://your-server/api/documents/process \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -F "File=@contract.pdf" \
  -F "Instructions=Extract parties, dates, and payment terms"
```

#### Implementation Mode B: Own Key (BYOK Gemini)
```bash
curl -X POST https://your-server/api/documents/process \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "X-LLM-Provider: Gemini" \
  -H "X-LLM-Model: gemini-flash-lite-latest" \
  -F "File=@contract.pdf" \
  -F "Instructions=Extract parties, dates, and payment terms"
```

#### Synchronous Response (`200 OK`)
```json
{
  "jobId": "JOB-918B0C27",
  "status": "Succeeded",
  "stitchedEnvelope": {
    "total_pages": 10,
    "document_summary": "Master Services Agreement between...",
    "document_keywords": ["liability", "payment", "governing law"],
    "full_markdown": "# Master Services Agreement\n\n..."
  },
  "verificationReport": {
    "pages_with_no_extracted_content": [],
    "review_tasks": []
  }
}
```

Response headers indicate quota impact:
- `X-Pages-Processed`: Number of pages counted and billed.
- `X-Quota-Remaining`: Account balance remaining after this call.

---

## 7. The Analyst Agents

All Agent endpoints operate against an existing `jobId` from Step 1. Calling agents does **not** re-parse the PDF and incurs no additional page quota cost.

### 7.1 Summarize
```http
POST /api/agent/jobs/{jobId}/summarize
Content-Type: application/json
```

#### Mode A: Default Implementation (Server Key)
```json
{
  "FocusArea": "payment terms",
  "Tier": "fast"
}
```
cURL:
```bash
curl -X POST https://your-server/api/agent/jobs/{jobId}/summarize \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"FocusArea": "payment terms", "Tier": "fast"}'
```

#### Mode B: Own Key Implementation (BYOK Gemini)
```json
{
  "FocusArea": "payment terms",
  "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest"
}
```
cURL:
```bash
curl -X POST https://your-server/api/agent/jobs/{jobId}/summarize \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "Content-Type: application/json" \
  -d '{"FocusArea": "payment terms", "ApiKey": "AIzaSyYourPersonalGeminiKeyHere", "Provider": "Gemini"}'
```

**Response:**
```json
{
  "jobId": "JOB-918B0C27",
  "focusArea": "payment terms",
  "summary": "The agreement specifies net-30 payment terms with 1.5% interest per month on late invoices...",
  "generatedAt": "2026-10-26T10:15:00Z"
}
```

### 7.2 Ask (Grounded Q&A)
```http
POST /api/agent/jobs/{jobId}/ask
Content-Type: application/json
```

#### Mode A: Default Implementation (Server Key)
```json
{
  "Question": "What is the liability cap?"
}
```
cURL:
```bash
curl -X POST https://your-server/api/agent/jobs/{jobId}/ask \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"Question": "What is the liability cap?"}'
```

#### Mode B: Own Key Implementation (BYOK Gemini)
```json
{
  "Question": "What is the liability cap?",
  "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest"
}
```
cURL:
```bash
curl -X POST https://your-server/api/agent/jobs/{jobId}/ask \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "Content-Type: application/json" \
  -d '{"Question": "What is the liability cap?", "ApiKey": "AIzaSyYourPersonalGeminiKeyHere", "Provider": "Gemini"}'
```

**Response:**
```json
{
  "answer": "Liability is capped at the total amount paid by the customer in the preceding 12 months.",
  "citations": [
    {
      "fieldName": "Limitation of Liability",
      "pageNumber": 7,
      "confidence": 0.98,
      "evidence": "In no event shall either party's aggregate liability exceed the fees paid in the prior twelve (12) months."
    }
  ],
  "warnings": [],
  "isFoundInDocument": true
}
```

### 7.3 Validate Rules (Compliance Audit)
```http
POST /api/agent/jobs/{jobId}/validate-rules
Content-Type: application/json
```

**Request:**
```json
{
  "total_marks_cap": 100,
  "passing_threshold": 70,
  "rules": [
    {
      "rule_id": "SEC-01",
      "category": "Security & Compliance",
      "name": "Data encryption at rest",
      "rule_description": "The agreement must require AES-256 encryption at rest for all stored data.",
      "is_knockout": true,
      "max_marks": 20
    }
  ]
}
```

**Response:**
```json
{
  "job_id": "JOB-918B0C27",
  "overall_verdict": "PASS",
  "total_score_achieved": 20,
  "total_marks_cap": 100,
  "knockout_passed": true,
  "rule_results": [
    {
      "rule_id": "SEC-01",
      "status": "Pass",
      "marks_awarded": 20,
      "finding": "AES-256 encryption is mandated in Section 8.1.",
      "page_citations": [8],
      "confidence": 0.97
    }
  ]
}
```

---

## 8. Handling Failures, Timeouts & Async Processing

### 8.1 Understanding Document Processing Statuses
When you call `/api/documents/process`, the `status` field in the response indicates how cleanly the document was extracted:

| Status | Meaning | Action to take |
| :--- | :--- | :--- |
| `"Succeeded"` | All pages parsed and verified with high confidence. | Proceed directly to Agent calls. |
| `"NeedsReview"` | Document extracted, but one or more pages/fields had low OCR confidence or missing text layers. | Check `verificationReport.pages_with_no_extracted_content` and `review_tasks`. The agents can still be called, but ground truth should be verified. |
| `"Failed"` | Document could not be processed (e.g. corrupt PDF, zero pages, unsupported format). | Fix file and retry; inspect `error` message. |

### 8.2 Incomplete Page Coverage
If a document contains scanned image pages without OCR or blank pages, inspect:
```json
"verificationReport": {
  "pages_with_no_extracted_content": [3, 7]
}
```
If pages are listed, content on those pages was unreachable. In automated pipelines, check this array before relying on complete coverage.

### 8.3 Asynchronous Processing (For large or 10+ page PDFs)
For large documents, avoid keeping HTTP connections open by using asynchronous mode:

1. Add the header `Prefer: respond-async` (or form field `Async=true`):
   ```bash
   curl -X POST https://your-server/api/documents/process \
     -H "X-Document-Api-Key: pixous_your_key_here" \
     -H "Prefer: respond-async" \
     -F "File=@huge_doc.pdf"
   ```
2. The server responds immediately with **`202 Accepted`**:
   ```json
   {
     "jobId": "JOB-918B0C27",
     "status": "Processing",
     "pollUrl": "/api/documents/jobs/JOB-918B0C27/result"
   }
   ```
   Headers will include:
   - `Location: /api/documents/jobs/JOB-918B0C27/result`
   - `Retry-After: 5`
3. Poll the `Location` endpoint every 5 seconds until it returns **`200 OK`** with the completed envelope.

---

## 9. Error Handling & HTTP Status Codes

Every error returns a structured JSON payload:
```json
{
  "error": "Brief description of error",
  "details": "Technical diagnostics or validation errors"
}
```

| HTTP Status | Meaning | Recovery Action |
| :--- | :--- | :--- |
| `400 Bad Request` | Missing file, empty question, or invalid rule definition. | Inspect `error` details and fix request payload. |
| `401 Unauthorized` | Missing or invalid `X-Document-Api-Key`. | Verify API key is present and active in API Access. |
| `403 Forbidden` | API key lacks required permission (`process`, `summarize`, `ask`, `evaluate`). | Enable the required permission on the key in the portal. |
| `404 Not Found` | Specified `jobId` does not exist or document processing failed. | Confirm `jobId` matches the ID returned during processing. |
| `429 Too Many Requests` | Monthly page credit pool exhausted, or upstream LLM provider rate-limited. | Check remaining quota on Overview, or implement exponential backoff retry. |
| `500 Server Error` | Unexpected backend error. | Retry request; if persistent, check server logs. |

---

## 10. Production Best Practices

1. **Keep Secrets Server-Side**: Never include `X-Document-Api-Key` or `X-LLM-ApiKey` in client-side bundles.
2. **Reuse the `jobId`**: Process once, query many times. Processing is the only operation that consumes document page credits; calling Summarize, Ask, or Validate Rules on an existing `jobId` costs no page quota.
3. **Verify `isFoundInDocument`**: On Ask responses, always verify that `isFoundInDocument === true` before taking automated actions.
4. **Use Asynchronous Processing for Large Files**: Use `Prefer: respond-async` on files over 10 pages to avoid gateway timeouts (504s).
5. **Implement Retry with Exponential Backoff**: Wrap API calls in retry loops that handle transient `429` and `503` errors with jittered backoff (1s, 2s, 4s, 8s).
