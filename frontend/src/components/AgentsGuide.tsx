import { useState } from "react";
import CodeBlock from "./CodeBlock";
import DocsShell, {
  DocsCallout,
  DocsSectionCard,
  EndpointPath,
  InlineCode,
  KeyModeNavbar,
  LanguageNavbar,
  type KeyMode,
  type Lang,
} from "./docs/DocsShell";

const SECTIONS = [
  { id: "quick-ref", label: "Quick reference" },
  { id: "architecture", label: "Architecture & Security" },
  { id: "base-url", label: "Base URL config" },
  { id: "get-key", label: "Get an API key" },
  { id: "authenticate", label: "Authenticate" },
  { id: "byok", label: "Bring your own LLM key" },
  { id: "e2e-flow", label: "End-to-end integration" },
  { id: "workflow", label: "The workflow" },
  { id: "summarize", label: "Summarize" },
  { id: "ask", label: "Ask" },
  { id: "validate-rules", label: "Validate Rules" },
  { id: "failures-async", label: "Failures & Async" },
  { id: "code-examples", label: "Code examples" },
  { id: "errors", label: "Error handling" },
  { id: "good-practices", label: "Good practices" },
];

const BYOK_HEADER = `X-Document-Api-Key: pixous_your_key_here
X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere
X-LLM-Provider: Gemini
X-LLM-Model: gemini-flash-lite-latest`;

const BYOK_BODY = `{
  "Question": "What is the effective date of this agreement?",
  "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest"
}`;

const BYOK_ENV = `# PowerShell
$env:GEMINI_API_KEY = "AIzaSyYourPersonalGeminiKeyHere"

# Bash / Linux
export GEMINI_API_KEY="AIzaSyYourPersonalGeminiKeyHere"`;

const BASE_URL_CONFIG = `// Node.js (Express / Next.js)
const PIXOUS_BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const PIXOUS_API_KEY = process.env.PIXOUS_API_KEY;

# Python
PIXOUS_BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
PIXOUS_API_KEY = os.environ.get("PIXOUS_API_KEY")

// C# (.NET appsettings.json)
var baseUrl = builder.Configuration["Pixous:BaseUrl"] ?? "http://localhost:5000/api";
var apiKey = builder.Configuration["Pixous:ApiKey"];`;

// End-to-End: Default
const E2E_PYTHON_DEFAULT = `import os
import requests

BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
API_KEY = os.environ.get("PIXOUS_API_KEY", "pixous_your_key_here")

# Default: Uses server-configured Gemini key
headers = { "X-Document-Api-Key": API_KEY }

# 1. Upload & Process PDF
print("Uploading document...")
with open("contract.pdf", "rb") as f:
    resp = requests.post(
        f"{BASE_URL}/documents/process",
        headers=headers,
        files={"File": ("contract.pdf", f, "application/pdf")},
        data={"Instructions": "Extract dates, parties, and payment terms"}
    )
    resp.raise_for_status()
    job_id = resp.json()["jobId"]
    print(f"Processed! Job ID: {job_id}")

# 2. Ask Grounded Question
print("Querying Agent...")
ask_resp = requests.post(
    f"{BASE_URL}/agent/jobs/{job_id}/ask",
    headers={**headers, "Content-Type": "application/json"},
    json={"Question": "What is the agreement termination policy?"}
)
ask_resp.raise_for_status()
data = ask_resp.json()

print("\\nANSWER:\\n", data["answer"])
if data.get("citations"):
    print("\\nCITATIONS:")
    for c in data["citations"]:
        print(f"- Page {c['pageNumber']} ({c['confidence'] * 100:.0f}%): \\"{c['evidence']}\\"")`;

// End-to-End: Own Key (BYOK)
const E2E_PYTHON_OWN_KEY = `import os
import requests

BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
PIXOUS_API_KEY = os.environ.get("PIXOUS_API_KEY", "pixous_your_key_here")
GEMINI_KEY = os.environ.get("GEMINI_API_KEY", "AIzaSyYourPersonalGeminiKeyHere")

# Own Key: Authenticate with Pixous, run AI operations on your Gemini key
headers = {
    "X-Document-Api-Key": PIXOUS_API_KEY,
    "X-LLM-ApiKey": GEMINI_KEY,
    "X-LLM-Provider": "Gemini",
    "X-LLM-Model": "gemini-flash-lite-latest"
}

# 1. Upload & Process PDF using your personal Gemini quota
print("Uploading document with personal Gemini key...")
with open("contract.pdf", "rb") as f:
    resp = requests.post(
        f"{BASE_URL}/documents/process",
        headers=headers,
        files={"File": ("contract.pdf", f, "application/pdf")},
        data={"Instructions": "Extract dates, parties, and payment terms"}
    )
    resp.raise_for_status()
    job_id = resp.json()["jobId"]
    print(f"Processed! Job ID: {job_id}")

# 2. Ask Grounded Question using your personal Gemini quota
print("Querying Agent...")
ask_resp = requests.post(
    f"{BASE_URL}/agent/jobs/{job_id}/ask",
    headers={**headers, "Content-Type": "application/json"},
    json={"Question": "What is the agreement termination policy?"}
)
ask_resp.raise_for_status()
data = ask_resp.json()

print("\\nANSWER:\\n", data["answer"])
if data.get("citations"):
    print("\\nCITATIONS:")
    for c in data["citations"]:
        print(f"- Page {c['pageNumber']} ({c['confidence'] * 100:.0f}%): \\"{c['evidence']}\\"")`;

const E2E_JS_DEFAULT = `import fs from "fs";
import path from "path";

const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const API_KEY = process.env.PIXOUS_API_KEY || "pixous_your_key_here";

async function main() {
  const filePath = "contract.pdf";
  const fileBuffer = fs.readFileSync(filePath);
  const blob = new Blob([fileBuffer], { type: "application/pdf" });

  // 1. Upload & Process PDF
  console.log("Uploading document...");
  const form = new FormData();
  form.append("File", blob, path.basename(filePath));
  form.append("Instructions", "Extract dates, parties, and payment terms");

  const processRes = await fetch(\`\${BASE_URL}/documents/process\`, {
    method: "POST",
    headers: { "X-Document-Api-Key": API_KEY },
    body: form,
  });

  if (!processRes.ok) throw new Error(\`Processing failed: \${processRes.status}\`);
  const { jobId, status } = await processRes.json();
  console.log(\`Document processed! Job ID: \${jobId} (Status: \${status})\`);

  // 2. Ask Grounded Question
  console.log("Querying Agent...");
  const askRes = await fetch(\`\${BASE_URL}/agent/jobs/\${jobId}/ask\`, {
    method: "POST",
    headers: {
      "X-Document-Api-Key": API_KEY,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ Question: "What is the agreement termination policy?" }),
  });

  const data = await askRes.json();
  console.log("\\nANSWER:\\n", data.answer);
  if (data.citations?.length) {
    console.log("\\nCITATIONS:");
    data.citations.forEach((c) => {
      console.log(\`- Page \${c.pageNumber} (\${Math.round(c.confidence * 100)}%): "\${c.evidence}"\`);
    });
  }
}

main().catch(console.error);`;

const E2E_JS_OWN_KEY = `import fs from "fs";
import path from "path";

const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const API_KEY = process.env.PIXOUS_API_KEY || "pixous_your_key_here";
const GEMINI_KEY = process.env.GEMINI_API_KEY || "AIzaSyYourPersonalGeminiKeyHere";

const authHeaders = {
  "X-Document-Api-Key": API_KEY,
  "X-LLM-ApiKey": GEMINI_KEY,
  "X-LLM-Provider": "Gemini",
  "X-LLM-Model": "gemini-flash-lite-latest",
};

async function main() {
  const filePath = "contract.pdf";
  const fileBuffer = fs.readFileSync(filePath);
  const blob = new Blob([fileBuffer], { type: "application/pdf" });

  // 1. Upload & Process PDF (using personal Gemini quota)
  console.log("Uploading document with personal Gemini key...");
  const form = new FormData();
  form.append("File", blob, path.basename(filePath));
  form.append("Instructions", "Extract dates, parties, and payment terms");

  const processRes = await fetch(\`\${BASE_URL}/documents/process\`, {
    method: "POST",
    headers: authHeaders,
    body: form,
  });

  if (!processRes.ok) throw new Error(\`Processing failed: \${processRes.status}\`);
  const { jobId, status } = await processRes.json();
  console.log(\`Document processed! Job ID: \${jobId} (Status: \${status})\`);

  // 2. Ask Grounded Question (using personal Gemini quota)
  console.log("Querying Agent...");
  const askRes = await fetch(\`\${BASE_URL}/agent/jobs/\${jobId}/ask\`, {
    method: "POST",
    headers: { ...authHeaders, "Content-Type": "application/json" },
    body: JSON.stringify({ Question: "What is the agreement termination policy?" }),
  });

  const data = await askRes.json();
  console.log("\\nANSWER:\\n", data.answer);
  if (data.citations?.length) {
    console.log("\\nCITATIONS:");
    data.citations.forEach((c) => {
      console.log(\`- Page \${c.pageNumber} (\${Math.round(c.confidence * 100)}%): "\${c.evidence}"\`);
    });
  }
}

main().catch(console.error);`;

const E2E_CSHARP_DEFAULT = `using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = Environment.GetEnvironmentVariable("PIXOUS_BASE_URL") ?? "http://localhost:5000/api/";
var apiKey = Environment.GetEnvironmentVariable("PIXOUS_API_KEY") ?? "pixous_your_key_here";

using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", apiKey);

// 1. Upload & Process PDF
Console.WriteLine("Uploading document...");
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract dates, parties, and payment terms"), "Instructions");

var processRes = await client.PostAsync("documents/process", form);
processRes.EnsureSuccessStatusCode();
var processDoc = await processRes.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processDoc.GetProperty("jobId").GetString()!;
Console.WriteLine($"Processed! Job ID: {jobId}");

// 2. Ask Grounded Question
Console.WriteLine("Querying Agent...");
var askBody = JsonContent.Create(new { Question = "What is the agreement termination policy?" });
var askRes = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askRes.EnsureSuccessStatusCode();
var askDoc = await askRes.Content.ReadFromJsonAsync<JsonElement>();

Console.WriteLine("\\nANSWER:\\n" + askDoc.GetProperty("answer").GetString());`;

const E2E_CSHARP_OWN_KEY = `using System.Net.Http.Json;
using System.Text.Json;

var baseUrl = Environment.GetEnvironmentVariable("PIXOUS_BASE_URL") ?? "http://localhost:5000/api/";
var apiKey = Environment.GetEnvironmentVariable("PIXOUS_API_KEY") ?? "pixous_your_key_here";
var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "AIzaSyYourPersonalGeminiKeyHere";

using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", apiKey);
client.DefaultRequestHeaders.Add("X-LLM-ApiKey", geminiKey);
client.DefaultRequestHeaders.Add("X-LLM-Provider", "Gemini");
client.DefaultRequestHeaders.Add("X-LLM-Model", "gemini-flash-lite-latest");

// 1. Upload & Process PDF (using personal Gemini quota)
Console.WriteLine("Uploading document with personal Gemini key...");
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract dates, parties, and payment terms"), "Instructions");

var processRes = await client.PostAsync("documents/process", form);
processRes.EnsureSuccessStatusCode();
var processDoc = await processRes.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processDoc.GetProperty("jobId").GetString()!;
Console.WriteLine($"Processed! Job ID: {jobId}");

// 2. Ask Grounded Question (using personal Gemini quota)
Console.WriteLine("Querying Agent...");
var askBody = JsonContent.Create(new { Question = "What is the agreement termination policy?" });
var askRes = await client.PostAsync($"agent/jobs/{jobId}/ask", askBody);
askRes.EnsureSuccessStatusCode();
var askDoc = await askRes.Content.ReadFromJsonAsync<JsonElement>();

Console.WriteLine("\\nANSWER:\\n" + askDoc.GetProperty("answer").GetString());`;

const E2E_CURL_DEFAULT = `# 1. Upload and process PDF
JOB_ID=$(curl -s -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -F "File=@contract.pdf" \\
  -F "Instructions=Extract dates, parties, and payment terms" | jq -r '.jobId')

echo "Processed! Job ID: $JOB_ID"

# 2. Ask Grounded Question
curl -X POST "https://your-server/api/agent/jobs/$JOB_ID/ask" \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Content-Type: application/json" \\
  -d '{"Question": "What is the agreement termination policy?"}' | jq`;

const E2E_CURL_OWN_KEY = `# 1. Upload and process PDF (using personal Gemini key)
JOB_ID=$(curl -s -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "X-LLM-Provider: Gemini" \\
  -H "X-LLM-Model: gemini-flash-lite-latest" \\
  -F "File=@contract.pdf" \\
  -F "Instructions=Extract dates, parties, and payment terms" | jq -r '.jobId')

echo "Processed! Job ID: $JOB_ID"

# 2. Ask Grounded Question (using personal Gemini key)
curl -X POST "https://your-server/api/agent/jobs/$JOB_ID/ask" \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "Content-Type: application/json" \\
  -d '{"Question": "What is the agreement termination policy?"}' | jq`;

const ASYNC_CURL = `curl -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Prefer: respond-async" \\
  -F "File=@large_document.pdf"`;

const ASYNC_RESPONSE = `{
  "jobId": "JOB-918B0C27",
  "status": "Processing",
  "pollUrl": "/api/documents/jobs/JOB-918B0C27/result"
}`;

// Process: Default vs Own Key
const PROCESS_CURL_DEFAULT = `curl -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -F "File=@contract.pdf" \\
  -F "Instructions=Extract parties, dates, and payment terms"`;

const PROCESS_CURL_OWN_KEY = `curl -X POST https://your-server/api/documents/process \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "X-LLM-Provider: Gemini" \\
  -H "X-LLM-Model: gemini-flash-lite-latest" \\
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

// Summarize: Default vs Own Key
const SUMMARIZE_REQUEST_DEFAULT = `{
  "FocusArea": "payment terms",
  "Tier": "fast"
}`;

const SUMMARIZE_CURL_DEFAULT = `curl -X POST https://your-server/api/agent/jobs/{jobId}/summarize \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Content-Type: application/json" \\
  -d '{"FocusArea": "payment terms", "Tier": "fast"}'`;

const SUMMARIZE_REQUEST_OWN_KEY = `{
  "FocusArea": "payment terms",
  "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest"
}`;

const SUMMARIZE_CURL_OWN_KEY = `curl -X POST https://your-server/api/agent/jobs/{jobId}/summarize \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "Content-Type: application/json" \\
  -d '{"FocusArea": "payment terms", "ApiKey": "AIzaSyYourPersonalGeminiKeyHere", "Provider": "Gemini"}'`;

const SUMMARIZE_RESPONSE = `{
  "jobId": "JOB-918B0C27",
  "focusArea": "payment terms",
  "summary": "The agreement specifies a fixed implementation fee of USD 8,400,000 payable via milestones...",
  "generatedAt": "2026-10-26T10:15:00Z"
}`;

// Ask: Default vs Own Key
const ASK_REQUEST_DEFAULT = `{
  "Question": "What is the effective date of this agreement?"
}`;

const ASK_CURL_DEFAULT = `curl -X POST https://your-server/api/agent/jobs/{jobId}/ask \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Content-Type: application/json" \\
  -d '{"Question": "What is the effective date of this agreement?"}'`;

const ASK_REQUEST_OWN_KEY = `{
  "Question": "What is the effective date of this agreement?",
  "ApiKey": "AIzaSyYourPersonalGeminiKeyHere",
  "Provider": "Gemini",
  "Model": "gemini-flash-lite-latest"
}`;

const ASK_CURL_OWN_KEY = `curl -X POST https://your-server/api/agent/jobs/{jobId}/ask \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "Content-Type: application/json" \\
  -d '{"Question": "What is the effective date?", "ApiKey": "AIzaSyYourPersonalGeminiKeyHere", "Provider": "Gemini"}'`;

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

// Server Code Examples: Default
const JS_EXAMPLE_DEFAULT = `// Server-side Node.js (Default: Server Key)
const API_KEY = process.env.PIXOUS_API_KEY;
const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";

async function processDocument(fileBlob, instructions) {
  const form = new FormData();
  form.append("File", fileBlob, "contract.pdf");
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
}`;

// Server Code Examples: Own Key
const JS_EXAMPLE_OWN_KEY = `// Server-side Node.js (Own Key: Personal Gemini Key)
const API_KEY = process.env.PIXOUS_API_KEY;
const GEMINI_KEY = process.env.GEMINI_API_KEY;
const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";

const authHeaders = {
  "X-Document-Api-Key": API_KEY,
  "X-LLM-ApiKey": GEMINI_KEY,
  "X-LLM-Provider": "Gemini",
  "X-LLM-Model": "gemini-flash-lite-latest",
};

async function processDocument(fileBlob, instructions) {
  const form = new FormData();
  form.append("File", fileBlob, "contract.pdf");
  if (instructions) form.append("Instructions", instructions);

  const res = await fetch(\`\${BASE_URL}/documents/process\`, {
    method: "POST",
    headers: authHeaders,
    body: form,
  });
  if (!res.ok) throw new Error(\`Processing failed: \${res.status}\`);
  return res.json();
}

async function askQuestion(jobId, question) {
  const res = await fetch(\`\${BASE_URL}/agent/jobs/\${jobId}/ask\`, {
    method: "POST",
    headers: { ...authHeaders, "Content-Type": "application/json" },
    body: JSON.stringify({ Question: question }),
  });
  if (!res.ok) throw new Error(\`Ask failed: \${res.status}\`);
  return res.json();
}`;

const PY_EXAMPLE_DEFAULT = `# Server-side Python (Default: Server Key)
import os, requests

API_KEY = os.environ.get("PIXOUS_API_KEY")
BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
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
    return resp.json()`;

const PY_EXAMPLE_OWN_KEY = `# Server-side Python (Own Key: Personal Gemini Key)
import os, requests

API_KEY = os.environ.get("PIXOUS_API_KEY")
GEMINI_KEY = os.environ.get("GEMINI_API_KEY")
BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")

HEADERS = {
    "X-Document-Api-Key": API_KEY,
    "X-LLM-ApiKey": GEMINI_KEY,
    "X-LLM-Provider": "Gemini"
}

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
    return resp.json()`;

const CSHARP_EXAMPLE = `// Server-side .NET / C#
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000/api/") };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", apiKey);
// Optional Own Key header:
// client.DefaultRequestHeaders.Add("X-LLM-ApiKey", geminiKey);

// 1. Process document
using var form = new MultipartFormDataContent();
using var fileStream = File.OpenRead("contract.pdf");
form.Add(new StreamContent(fileStream), "File", "contract.pdf");
form.Add(new StringContent("Extract parties and dates"), "Instructions");

var processResponse = await client.PostAsync("documents/process", form);
processResponse.EnsureSuccessStatusCode();
var processResult = await processResponse.Content.ReadFromJsonAsync<JsonElement>();
string jobId = processResult.GetProperty("jobId").GetString()!;

// 2. Query Agent
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
  { status: "429", meaning: "Monthly credit exhausted, or provider rate-limited you", action: "Check Overview for remaining credit, or wait/retry with backoff" },
  { status: "500", meaning: "Unexpected server error", action: "Retry; check the details field for the underlying cause" },
];

export default function AgentsGuide() {
  const [e2eMode, setE2eMode] = useState<KeyMode>("default");
  const [e2eLang, setE2eLang] = useState<Lang>("python");
  const [workflowMode, setWorkflowMode] = useState<KeyMode>("default");
  const [summarizeMode, setSummarizeMode] = useState<KeyMode>("default");
  const [askMode, setAskMode] = useState<KeyMode>("default");
  const [codeMode, setCodeMode] = useState<KeyMode>("default");
  const [codeLang, setCodeLang] = useState<Lang>("javascript");

  function setAllModes(m: KeyMode) {
    setE2eMode(m);
    setWorkflowMode(m);
    setSummarizeMode(m);
    setAskMode(m);
    setCodeMode(m);
  }

  return (
    <DocsShell
      eyebrow="Developer Integration Guide"
      title="Pixous Document Intelligence API"
      description="Integrate Document Processing and grounded Analyst Agents (Summarize, Ask, and Validate Rules) into your applications. Documents are transcribed with visual OCR and verified with page-level citations."
      note="Just want to try this without writing code? Use the Playground — no API key needed."
      meta={[
        { label: "Base paths", value: "/api/documents · /api/agent" },
        { label: "Auth header", value: "X-Document-Api-Key" },
      ]}
      sections={SECTIONS}
    >
      {/* Global Quick Switcher Banner */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-navy/20 bg-navy/5 p-4 shadow-xs">
        <div>
          <p className="text-xs font-semibold text-navy">Global Documentation View:</p>
          <p className="text-xs text-gray-600">
            Switch all request examples between <strong>Server Key (Default)</strong> and <strong>Own Key (BYOK Gemini)</strong>.
          </p>
        </div>
        <div className="inline-flex rounded-lg border border-border-subtle bg-white p-1 shadow-xs">
          <button
            type="button"
            onClick={() => setAllModes("default")}
            className={`rounded-md px-3 py-1.5 text-xs font-medium transition-all ${
              summarizeMode === "default" && askMode === "default"
                ? "bg-navy text-white shadow-xs font-semibold"
                : "text-gray-500 hover:text-navy"
            }`}
          >
            Default (Server Key)
          </button>
          <button
            type="button"
            onClick={() => setAllModes("ownKey")}
            className={`rounded-md px-3 py-1.5 text-xs font-medium transition-all ${
              summarizeMode === "ownKey" && askMode === "ownKey"
                ? "bg-navy text-white shadow-xs font-semibold"
                : "text-gray-500 hover:text-navy"
            }`}
          >
            Own Key (BYOK Gemini)
          </button>
        </div>
      </div>

      <DocsSectionCard id="quick-ref" index={1} title="Quick Reference: Endpoints & Permissions">
        <p>Overview of primary endpoints, required API key permissions, and expected response formats:</p>
        <div className="overflow-x-auto my-2 rounded-lg border border-border-subtle bg-white">
          <table className="w-full text-left text-xs">
            <thead className="bg-gray-50 text-gray-700 font-semibold border-b border-border-subtle">
              <tr>
                <th className="py-2 px-3">Method &amp; Endpoint</th>
                <th className="py-2 px-3">Permission</th>
                <th className="py-2 px-3">Purpose</th>
                <th className="py-2 px-3">Key Returns</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border-subtle">
              <tr>
                <td className="py-2 px-3 font-mono text-navy font-medium">POST /api/documents/process</td>
                <td className="py-2 px-3"><span className="rounded bg-blue-100 px-1.5 py-0.5 text-blue-700 font-mono">process</span></td>
                <td className="py-2 px-3">Uploads &amp; processes PDF</td>
                <td className="py-2 px-3 font-mono text-gray-600">jobId, stitchedEnvelope</td>
              </tr>
              <tr>
                <td className="py-2 px-3 font-mono text-navy font-medium">GET /api/documents/jobs/{`{id}`}</td>
                <td className="py-2 px-3"><span className="rounded bg-blue-100 px-1.5 py-0.5 text-blue-700 font-mono">process</span></td>
                <td className="py-2 px-3">Fetches extraction results</td>
                <td className="py-2 px-3 font-mono text-gray-600">status, verificationReport</td>
              </tr>
              <tr>
                <td className="py-2 px-3 font-mono text-navy font-medium">POST /api/agent/jobs/{`{id}`}/summarize</td>
                <td className="py-2 px-3"><span className="rounded bg-purple-100 px-1.5 py-0.5 text-purple-700 font-mono">summarize</span></td>
                <td className="py-2 px-3">Executive or focused summary</td>
                <td className="py-2 px-3 font-mono text-gray-600">summary, focusArea</td>
              </tr>
              <tr>
                <td className="py-2 px-3 font-mono text-navy font-medium">POST /api/agent/jobs/{`{id}`}/ask</td>
                <td className="py-2 px-3"><span className="rounded bg-green-100 px-1.5 py-0.5 text-green-700 font-mono">ask</span></td>
                <td className="py-2 px-3">Grounded Q&amp;A with citations</td>
                <td className="py-2 px-3 font-mono text-gray-600">answer, citations</td>
              </tr>
              <tr>
                <td className="py-2 px-3 font-mono text-navy font-medium">POST /api/agent/jobs/{`{id}`}/validate-rules</td>
                <td className="py-2 px-3"><span className="rounded bg-amber-100 px-1.5 py-0.5 text-amber-700 font-mono">evaluate</span></td>
                <td className="py-2 px-3">Rule &amp; compliance scorecard</td>
                <td className="py-2 px-3 font-mono text-gray-600">overall_verdict, scores</td>
              </tr>
            </tbody>
          </table>
        </div>
      </DocsSectionCard>

      <DocsSectionCard id="architecture" index={2} title="Architecture & API Key Security">
        <DocsCallout tone="warn">
          <strong>Security Warning:</strong> Never embed your permanent Pixous API key (<InlineCode>X-Document-Api-Key</InlineCode>) or LLM keys in client-side code (React, Vue, Angular, mobile apps, or public repos). Always route requests through your own backend.
        </DocsCallout>

        <p className="mt-3 text-xs text-gray-700 leading-relaxed">
          The recommended integration architecture keeps your API credentials strictly server-side:
        </p>

        <div className="my-3 rounded-lg border border-border-subtle bg-app-bg p-4 font-mono text-xs text-gray-800 leading-relaxed">
          <p className="font-semibold text-navy">Client-to-Backend-to-Pixous Pattern:</p>
          <p className="mt-1">1. [User's Frontend] (React, Angular, Mobile) &rarr; Authenticates with your app's session/token.</p>
          <p>2. [User's Backend] (Node.js, Python, .NET) &rarr; Securely stores PIXOUS_API_KEY in .env.</p>
          <p>3. [Pixous API] &rarr; Receives authenticated request with X-Document-Api-Key from your backend.</p>
          <p>4. [User's Backend] &rarr; Relays answers, summaries, or audit results back to your client.</p>
        </div>
      </DocsSectionCard>

      <DocsSectionCard id="base-url" index={3} title="Base URL Configuration">
        <p>Configure the Pixous Base URL in environment variables to allow seamless switching between development and production environments:</p>
        <ul className="list-disc pl-5 my-2 space-y-1 text-xs text-gray-600">
          <li><strong>Local development:</strong> <InlineCode>http://localhost:5000/api</InlineCode></li>
          <li><strong>Production server:</strong> <InlineCode>https://your-server-domain/api</InlineCode></li>
        </ul>
        <CodeBlock language="javascript" code={BASE_URL_CONFIG} />
      </DocsSectionCard>

      <DocsSectionCard id="get-key" index={4} title="Get an API key">
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

      <DocsSectionCard id="authenticate" index={5} title="Authenticate every request">
        <p>Send your key in the following header on every request:</p>
        <CodeBlock code="X-Document-Api-Key: pixous_your_key_here" />
        <p className="text-xs text-gray-500">
          <InlineCode>Authorization: Bearer &lt;key&gt;</InlineCode> also works as a fallback, but
          the dedicated header is preferred.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="byok" index={6} title="Bring Your Own LLM Key (Gemini / OpenAI)">
        <p>
          By default, requests use the server's configured LLM key. You can supply your personal{" "}
          <strong>Google Gemini</strong> or <strong>OpenAI</strong> API key to bypass server limits
          and use your own quota directly.
        </p>

        <p className="mt-3 font-semibold text-xs text-navy uppercase tracking-wider">Option A: Pass via Header (Recommended)</p>
        <p className="text-xs text-gray-600">Send alongside your Pixous access key on any request:</p>
        <CodeBlock code={BYOK_HEADER} />

        <p className="mt-3 font-semibold text-xs text-navy uppercase tracking-wider">Option B: In Request Body (For Agent queries)</p>
        <p className="text-xs text-gray-600">Pass directly in the JSON payload when calling Ask or Summarize:</p>
        <CodeBlock language="json" code={BYOK_BODY} />

        <p className="mt-3 font-semibold text-xs text-navy uppercase tracking-wider">Option C: Server Environment Variable (For self-hosted)</p>
        <p className="text-xs text-gray-600">Set the environment variable on your backend server before launching:</p>
        <CodeBlock language="bash" code={BYOK_ENV} />
      </DocsSectionCard>

      <DocsSectionCard id="e2e-flow" index={7} title="Complete End-to-End Integration Flow">
        <KeyModeNavbar mode={e2eMode} onChange={setE2eMode} />
        <LanguageNavbar lang={e2eLang} onChange={setE2eLang} />
        {e2eMode === "default" ? (
          <>
            <p className="text-xs text-gray-600 mb-2">
              Default implementation using server-configured Gemini key. Requires only <InlineCode>X-Document-Api-Key</InlineCode>:
            </p>
            {e2eLang === "python" && <CodeBlock language="python" code={E2E_PYTHON_DEFAULT} />}
            {e2eLang === "javascript" && <CodeBlock language="javascript" code={E2E_JS_DEFAULT} />}
            {e2eLang === "csharp" && <CodeBlock language="csharp" code={E2E_CSHARP_DEFAULT} />}
            {e2eLang === "curl" && <CodeBlock language="bash" code={E2E_CURL_DEFAULT} />}
          </>
        ) : (
          <>
            <p className="text-xs text-gray-600 mb-2">
              Own Key implementation: Passes <InlineCode>X-LLM-ApiKey</InlineCode> alongside <InlineCode>X-Document-Api-Key</InlineCode> to use your personal Gemini quota:
            </p>
            {e2eLang === "python" && <CodeBlock language="python" code={E2E_PYTHON_OWN_KEY} />}
            {e2eLang === "javascript" && <CodeBlock language="javascript" code={E2E_JS_OWN_KEY} />}
            {e2eLang === "csharp" && <CodeBlock language="csharp" code={E2E_CSHARP_OWN_KEY} />}
            {e2eLang === "curl" && <CodeBlock language="bash" code={E2E_CURL_OWN_KEY} />}
          </>
        )}
      </DocsSectionCard>

      <DocsSectionCard id="workflow" index={8} title="The workflow: process a document, then use the agents">
        <KeyModeNavbar mode={workflowMode} onChange={setWorkflowMode} />
        <p>
          The agents operate on a <strong>job</strong> — you can't ask a question or get a summary
          until a document has finished processing and produced a <InlineCode>jobId</InlineCode>.
        </p>
        <EndpointPath method="POST" path="/api/documents/process" />
        <table className="w-full text-left text-xs mb-3">
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
            {workflowMode === "ownKey" && (
              <>
                <tr className="border-t border-gray-100">
                  <td className="py-1 pr-4 font-mono text-navy font-medium">X-LLM-ApiKey (Header)</td>
                  <td className="py-1 pr-4">Optional</td>
                  <td className="py-1">Your personal Google Gemini or OpenAI key</td>
                </tr>
                <tr className="border-t border-gray-100">
                  <td className="py-1 pr-4 font-mono text-navy font-medium">X-LLM-Provider (Header)</td>
                  <td className="py-1 pr-4">Optional</td>
                  <td className="py-1">"Gemini" or "OpenAI"</td>
                </tr>
              </>
            )}
          </tbody>
        </table>

        <p className="text-xs font-semibold text-gray-600 mb-1">
          {workflowMode === "default" ? "Request cURL (Server Key):" : "Request cURL (With Personal Gemini Key):"}
        </p>
        <CodeBlock language="bash" code={workflowMode === "default" ? PROCESS_CURL_DEFAULT : PROCESS_CURL_OWN_KEY} />

        <p className="mt-3 text-xs font-semibold text-gray-600 mb-1">Response:</p>
        <CodeBlock language="json" code={PROCESS_RESPONSE} />
        <p className="text-xs text-gray-500 mt-1">
          Response headers <InlineCode>X-Pages-Processed</InlineCode> and{" "}
          <InlineCode>X-Quota-Remaining</InlineCode> tell you what this call cost.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="summarize" index={9} title="Summarize">
        <KeyModeNavbar mode={summarizeMode} onChange={setSummarizeMode} />
        <EndpointPath method="POST" path="/api/agent/jobs/{jobId}/summarize" />
        <p>Generates an executive summary, optionally focused on one topic.</p>

        <p className="text-xs font-medium text-gray-500 mt-2">
          {summarizeMode === "default" ? "Request Body (Default)" : "Request Body (Own Key Mode)"}
        </p>
        <CodeBlock
          language="json"
          code={summarizeMode === "default" ? SUMMARIZE_REQUEST_DEFAULT : SUMMARIZE_REQUEST_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">cURL Request</p>
        <CodeBlock
          language="bash"
          code={summarizeMode === "default" ? SUMMARIZE_CURL_DEFAULT : SUMMARIZE_CURL_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">Response</p>
        <CodeBlock language="json" code={SUMMARIZE_RESPONSE} />
      </DocsSectionCard>

      <DocsSectionCard id="ask" index={10} title="Ask">
        <KeyModeNavbar mode={askMode} onChange={setAskMode} />
        <EndpointPath method="POST" path="/api/agent/jobs/{jobId}/ask" />
        <p>
          Ask a specific question and get a grounded answer with citations.{" "}
          <InlineCode>Question</InlineCode> is required (PascalCase).
        </p>

        <p className="text-xs font-medium text-gray-500 mt-2">
          {askMode === "default" ? "Request Body (Default)" : "Request Body (Own Key Mode)"}
        </p>
        <CodeBlock
          language="json"
          code={askMode === "default" ? ASK_REQUEST_DEFAULT : ASK_REQUEST_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">cURL Request</p>
        <CodeBlock
          language="bash"
          code={askMode === "default" ? ASK_CURL_DEFAULT : ASK_CURL_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">Response</p>
        <CodeBlock language="json" code={ASK_RESPONSE} />
        <DocsCallout tone="warn">
          If <InlineCode>isFoundInDocument</InlineCode> is <code>false</code>, treat the answer
          with caution — always check this field before trusting an answer in an automated
          pipeline.
        </DocsCallout>
      </DocsSectionCard>

      <DocsSectionCard id="validate-rules" index={11} title="Validate Rules">
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

      <DocsSectionCard id="failures-async" index={12} title="Handling Processing Failures & Async">
        <p className="text-xs text-gray-700">
          Check the <InlineCode>status</InlineCode> field on extraction responses:
        </p>
        <ul className="list-disc pl-5 my-2 space-y-1 text-xs text-gray-600">
          <li><strong>"Succeeded":</strong> All pages extracted cleanly.</li>
          <li><strong>"NeedsReview":</strong> Review <InlineCode>verificationReport.pages_with_no_extracted_content</InlineCode> or low confidence fields.</li>
          <li><strong>"Failed":</strong> Corrupt PDF or unreadable file.</li>
        </ul>

        <p className="mt-3 font-semibold text-xs text-navy uppercase tracking-wider">Asynchronous Processing (Large PDFs)</p>
        <p className="text-xs text-gray-600">For documents over 10 pages, send <InlineCode>Prefer: respond-async</InlineCode> to prevent gateway timeouts:</p>
        <CodeBlock language="bash" code={ASYNC_CURL} />
        <p className="text-xs text-gray-600 mt-2">The server returns <InlineCode>202 Accepted</InlineCode> with a polling URL:</p>
        <CodeBlock language="json" code={ASYNC_RESPONSE} />
      </DocsSectionCard>

      <DocsSectionCard id="code-examples" index={13} title="Server-side Code examples">
        <KeyModeNavbar mode={codeMode} onChange={setCodeMode} />
        <LanguageNavbar lang={codeLang} onChange={setCodeLang} langs={["javascript", "python", "csharp"]} />
        <DocsCallout tone="info">
          These examples run on your backend server (e.g. Node.js, Python, C#), where API keys can be safely retrieved from environment variables.
        </DocsCallout>

        {codeLang === "javascript" && (
          <CodeBlock language="javascript" code={codeMode === "default" ? JS_EXAMPLE_DEFAULT : JS_EXAMPLE_OWN_KEY} />
        )}
        {codeLang === "python" && (
          <CodeBlock language="python" code={codeMode === "default" ? PY_EXAMPLE_DEFAULT : PY_EXAMPLE_OWN_KEY} />
        )}
        {codeLang === "csharp" && <CodeBlock language="csharp" code={CSHARP_EXAMPLE} />}
      </DocsSectionCard>

      <DocsSectionCard id="errors" index={14} title="Error handling">
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

      <DocsSectionCard id="good-practices" index={15} title="Good practices">
        <ul className="list-disc space-y-1 pl-5 text-xs text-gray-600">
          <li>
            <strong>Keep keys server-side:</strong> Never expose <InlineCode>X-Document-Api-Key</InlineCode> in frontend clients.
          </li>
          <li>
            <strong>Reuse the jobId:</strong> Processing a document is the only operation that consumes page credits. Once you have a jobId, call Summarize/Ask/Validate Rules at no additional page cost.
          </li>
          <li>
            <strong>Check isFoundInDocument:</strong> On Ask responses, verify this boolean before using answers in automated pipelines.
          </li>
          <li>
            <strong>Use Async for large files:</strong> Use <InlineCode>Prefer: respond-async</InlineCode> on files over 10 pages to avoid HTTP timeouts.
          </li>
          <li>
            <strong>Implement exponential backoff:</strong> Retry transient 429 and 503 responses with progressive backoff delays (1s, 2s, 4s).
          </li>
        </ul>
      </DocsSectionCard>
    </DocsShell>
  );
}
