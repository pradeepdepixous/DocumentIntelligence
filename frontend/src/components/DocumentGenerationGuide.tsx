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
  { id: "authenticate", label: "Authenticate" },
  { id: "workflow", label: "The workflow" },
  { id: "templates", label: "Set up a template" },
  { id: "section-types", label: "Section types" },
  { id: "draft", label: "Draft content" },
  { id: "validate", label: "Validate & fill gaps" },
  { id: "generate", label: "Generate files" },
  { id: "download", label: "Download" },
  { id: "code-examples", label: "Code examples" },
  { id: "errors", label: "Error handling" },
  { id: "good-practices", label: "Good practices" },
];

const UPLOAD_CURL = `curl -X POST https://your-server/api/templates/upload \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -F "file=@nda_template.docx" \\
  -F "template_id=nda" \\
  -F "title=Mutual NDA" \\
  -F "document_type=nda"`;

const TEMPLATE_JSON = `{
  "template_id": "nda",
  "version": 1,
  "document_type": "nda",
  "title": "Mutual NDA",
  "description": "Standard mutual non-disclosure agreement",
  "content_schema": {
    "sections": [
      { "id": "effective_date", "title": "Effective Date", "type": "date", "required": true },
      { "id": "disclosing_party", "title": "Disclosing Party", "type": "address_block", "required": true },
      { "id": "receiving_party", "title": "Receiving Party", "type": "address_block", "required": true },
      { "id": "purpose", "title": "Purpose", "type": "paragraphs", "required": true },
      { "id": "obligations", "title": "Obligations", "type": "bullet_list", "required": false },
      { "id": "signatory", "title": "Signatory", "type": "signature_block", "required": true }
    ]
  },
  "presentation": {
    "page_size": "A4",
    "font_family": "Arial",
    "primary_color": "#1E293B",
    "margins": { "top": 20, "bottom": 20, "left": 20, "right": 20 }
  }
}`;

const SECTION_TYPE_ROWS = [
  { type: "text", shape: "A single string" },
  { type: "paragraphs", shape: "A string, or an array of strings (one per paragraph)" },
  { type: "bullet_list / list", shape: "An array of strings, or a single string" },
  { type: "date", shape: "A string date — auto-filled with today's UTC date if omitted" },
  { type: "address_block", shape: 'Object { "name": "...", "address": "..." }' },
  { type: "signature_block", shape: 'Object { "name": "...", "designation": "..." }' },
  { type: "table", shape: "An array of row objects, with keys matching the section's fields" },
  { type: "object", shape: "An arbitrary non-empty JSON object" },
];

// Draft: Default vs Own Key
const DRAFT_REQUEST_DEFAULT = `{
  "template_id": "nda",
  "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term."
}`;

const DRAFT_CURL_DEFAULT = `curl -X POST https://your-server/api/documents/draft \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Content-Type: application/json" \\
  -d '{
    "template_id": "nda",
    "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term."
  }'`;

const DRAFT_REQUEST_OWN_KEY = `{
  "template_id": "nda",
  "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term.",
  "api_key": "AIzaSyYourPersonalGeminiKeyHere",
  "provider": "Gemini",
  "model": "gemini-flash-lite-latest"
}`;

const DRAFT_CURL_OWN_KEY = `curl -X POST https://your-server/api/documents/draft \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \\
  -H "Content-Type: application/json" \\
  -d '{
    "template_id": "nda",
    "instructions": "Mutual NDA between Acme Inc and Beta LLC, effective today.",
    "api_key": "AIzaSyYourPersonalGeminiKeyHere",
    "provider": "Gemini"
  }'`;

const DRAFT_RESPONSE = `{
  "document_id": "DOC-20261028-4F2A9B1C",
  "status": "needs_information",
  "template_id": "nda",
  "template_version": 1,
  "missing_fields": ["signatory.name", "signatory.designation"],
  "content": {
    "effective_date": "2026-10-28",
    "disclosing_party": { "name": "Acme Inc", "address": "123 Market St, SF" },
    "receiving_party": { "name": "Beta LLC", "address": "55 Harbor Rd, NY" },
    "purpose": ["This Agreement governs the exchange of confidential information between the parties..."]
  },
  "markdown_preview": "# Mutual NDA\\n\\n..."
}`;

const VALIDATE_REQUEST = `{
  "content": {
    "signatory": { "name": "Jane Doe", "designation": "General Counsel" }
  }
}`;

const VALIDATE_RESPONSE = `{
  "document_id": "DOC-20261028-4F2A9B1C",
  "is_valid": true,
  "status": "ready",
  "missing_fields": [],
  "errors": []
}`;

const GENERATE_REQUEST = `{ "formats": ["docx", "pdf"] }`;

const GENERATE_RESPONSE = `{
  "document_id": "DOC-20261028-4F2A9B1C",
  "status": "generated",
  "files": [
    { "format": "docx", "download_url": "/api/documents/DOC-20261028-4F2A9B1C/download?format=docx" },
    { "format": "pdf", "download_url": "/api/documents/DOC-20261028-4F2A9B1C/download?format=pdf" }
  ]
}`;

const DOWNLOAD_CURL = `curl -OJ "https://your-server/api/documents/DOC-20261028-4F2A9B1C/download?format=pdf"`;

// Server Code Examples: Default
const JS_EXAMPLE_DEFAULT = `// Node.js (Server-side Default)
const API_KEY = process.env.PIXOUS_API_KEY;
const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";
const HEADERS = { "X-Document-Api-Key": API_KEY, "Content-Type": "application/json" };

async function draftDocument(templateId, instructions) {
  const res = await fetch(\`\${BASE_URL}/documents/draft\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ template_id: templateId, instructions }),
  });
  if (!res.ok) throw new Error(\`Draft failed: \${res.status}\`);
  return res.json();
}

async function fillMissingFields(documentId, content) {
  const res = await fetch(\`\${BASE_URL}/documents/\${documentId}/validate\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ content }),
  });
  if (!res.ok) throw new Error(\`Validate failed: \${res.status}\`);
  return res.json();
}

async function generateDocument(documentId, formats = ["docx", "pdf"]) {
  const res = await fetch(\`\${BASE_URL}/documents/\${documentId}/generate\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ formats }),
  });
  if (!res.ok) throw new Error(\`Generate failed: \${res.status}\`);
  return res.json();
}`;

// Server Code Examples: Own Key
const JS_EXAMPLE_OWN_KEY = `// Node.js (Server-side with Personal Gemini Key)
const API_KEY = process.env.PIXOUS_API_KEY;
const GEMINI_KEY = process.env.GEMINI_API_KEY;
const BASE_URL = process.env.PIXOUS_BASE_URL || "http://localhost:5000/api";

const HEADERS = {
  "X-Document-Api-Key": API_KEY,
  "X-LLM-ApiKey": GEMINI_KEY,
  "Content-Type": "application/json",
};

async function draftDocument(templateId, instructions) {
  const res = await fetch(\`\${BASE_URL}/documents/draft\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({
      template_id: templateId,
      instructions,
      api_key: GEMINI_KEY,
      provider: "Gemini",
      model: "gemini-flash-lite-latest",
    }),
  });
  if (!res.ok) throw new Error(\`Draft failed: \${res.status}\`);
  return res.json();
}

async function fillMissingFields(documentId, content) {
  const res = await fetch(\`\${BASE_URL}/documents/\${documentId}/validate\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ content }),
  });
  if (!res.ok) throw new Error(\`Validate failed: \${res.status}\`);
  return res.json();
}

async function generateDocument(documentId, formats = ["docx", "pdf"]) {
  const res = await fetch(\`\${BASE_URL}/documents/\${documentId}/generate\`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ formats }),
  });
  if (!res.ok) throw new Error(\`Generate failed: \${res.status}\`);
  return res.json();
}`;

const PY_EXAMPLE_DEFAULT = `# Python (Server-side Default)
import os, requests

API_KEY = os.environ.get("PIXOUS_API_KEY")
BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")
HEADERS = {"X-Document-Api-Key": API_KEY, "Content-Type": "application/json"}

def draft_document(template_id, instructions):
    resp = requests.post(
        f"{BASE_URL}/documents/draft",
        headers=HEADERS,
        json={"template_id": template_id, "instructions": instructions},
    )
    resp.raise_for_status()
    return resp.json()

def fill_missing_fields(document_id, content):
    resp = requests.post(
        f"{BASE_URL}/documents/{document_id}/validate",
        headers=HEADERS,
        json={"content": content},
    )
    resp.raise_for_status()
    return resp.json()

def generate_document(document_id, formats=None):
    resp = requests.post(
        f"{BASE_URL}/documents/{document_id}/generate",
        headers=HEADERS,
        json={"formats": formats or ["docx", "pdf"]},
    )
    resp.raise_for_status()
    return resp.json()`;

const PY_EXAMPLE_OWN_KEY = `# Python (Server-side with Personal Gemini Key)
import os, requests

API_KEY = os.environ.get("PIXOUS_API_KEY")
GEMINI_KEY = os.environ.get("GEMINI_API_KEY")
BASE_URL = os.environ.get("PIXOUS_BASE_URL", "http://localhost:5000/api")

HEADERS = {
    "X-Document-Api-Key": API_KEY,
    "X-LLM-ApiKey": GEMINI_KEY,
    "Content-Type": "application/json",
}

def draft_document(template_id, instructions):
    resp = requests.post(
        f"{BASE_URL}/documents/draft",
        headers=HEADERS,
        json={
            "template_id": template_id,
            "instructions": instructions,
            "api_key": GEMINI_KEY,
            "provider": "Gemini",
            "model": "gemini-flash-lite-latest",
        },
    )
    resp.raise_for_status()
    return resp.json()

def fill_missing_fields(document_id, content):
    resp = requests.post(
        f"{BASE_URL}/documents/{document_id}/validate",
        headers=HEADERS,
        json={"content": content},
    )
    resp.raise_for_status()
    return resp.json()

def generate_document(document_id, formats=None):
    resp = requests.post(
        f"{BASE_URL}/documents/{document_id}/generate",
        headers=HEADERS,
        json={"formats": formats or ["docx", "pdf"]},
    )
    resp.raise_for_status()
    return resp.json()`;

const CSHARP_EXAMPLE = `// .NET / C#
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000/api/") };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", "pixous_your_key_here");
// Optional Own Key header:
// client.DefaultRequestHeaders.Add("X-LLM-ApiKey", "AIzaSyYourPersonalGeminiKey");

// 1. Draft content from instructions
var draftBody = JsonContent.Create(new {
    template_id = "nda",
    instructions = "Mutual NDA between Acme Inc and Beta LLC, effective today.",
    // api_key = "AIzaSy..." // Optional per-request override
});
var draftResponse = await client.PostAsync("documents/draft", draftBody);
draftResponse.EnsureSuccessStatusCode();
var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
string documentId = draft.GetProperty("document_id").GetString()!;

// 2. Fill in missing required fields
if (draft.GetProperty("status").GetString() == "needs_information")
{
    var validateBody = JsonContent.Create(new {
        content = new {
            signatory = new { name = "Jane Doe", designation = "General Counsel" }
        }
    });
    var validateResponse = await client.PostAsync($"documents/{documentId}/validate", validateBody);
    validateResponse.EnsureSuccessStatusCode();
}

// 3. Generate DOCX + PDF
var generateBody = JsonContent.Create(new { formats = new[] { "docx", "pdf" } });
var generateResponse = await client.PostAsync($"documents/{documentId}/generate", generateBody);
generateResponse.EnsureSuccessStatusCode();
var generated = await generateResponse.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine(generated.GetProperty("files"));`;

const ERROR_ROWS = [
  { status: "400", meaning: "Missing template_id/template_definition, invalid template schema, or unsupported output format", action: "Fix the request body per the error message" },
  { status: "401", meaning: "Missing or invalid API key / session", action: "Check the X-Document-Api-Key header is present and the key hasn't been revoked" },
  { status: "403", meaning: "Key doesn't have the generate permission", action: "Add generate to the key's permissions in API Access" },
  { status: "404", meaning: "Template or document draft not found", action: "Confirm the template_id/document_id is correct and was created first" },
  { status: "500", meaning: "Rendering failure (DOCX/PDF generation error)", action: "Check the error message for the underlying cause; retry" },
  { status: "502", meaning: "The underlying LLM provider failed during drafting", action: "Retry the draft call, or switch provider/model" },
];

export default function DocumentGenerationGuide() {
  const [draftMode, setDraftMode] = useState<KeyMode>("default");
  const [codeMode, setCodeMode] = useState<KeyMode>("default");
  const [codeLang, setCodeLang] = useState<Lang>("javascript");

  function setAllModes(m: KeyMode) {
    setDraftMode(m);
    setCodeMode(m);
  }

  return (
    <DocsShell
      eyebrow="Document Generation"
      title="Author documents from a template"
      description="Turn a Word (.docx) template plus a plain-language instruction into a fully drafted, downloadable document (DOCX and/or PDF). This is a separate pipeline from the Document Analyst Agents — this one authors new documents instead of reading existing ones."
      note="Prefer clicking around first? Document Studio in the portal drives this exact API end to end."
      meta={[
        { label: "Base path", value: "/api/documents · /api/templates" },
        { label: "Auth header", value: "X-Document-Api-Key" },
      ]}
      sections={SECTIONS}
    >
      {/* Global Quick Switcher Banner */}
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-navy/20 bg-navy/5 p-4 shadow-xs">
        <div>
          <p className="text-xs font-semibold text-navy">Global Documentation View:</p>
          <p className="text-xs text-gray-600">
            Switch generation examples between <strong>Server Key (Default)</strong> and <strong>Own Key (BYOK Gemini)</strong>.
          </p>
        </div>
        <div className="inline-flex rounded-lg border border-border-subtle bg-white p-1 shadow-xs">
          <button
            type="button"
            onClick={() => setAllModes("default")}
            className={`rounded-md px-3 py-1.5 text-xs font-medium transition-all ${
              draftMode === "default"
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
              draftMode === "ownKey"
                ? "bg-navy text-white shadow-xs font-semibold"
                : "text-gray-500 hover:text-navy"
            }`}
          >
            Own Key (BYOK Gemini)
          </button>
        </div>
      </div>

      <DocsSectionCard id="authenticate" index={1} title="Authenticate">
        <p>Same as the rest of the API — send your key on every request:</p>
        <CodeBlock code="X-Document-Api-Key: pixous_your_key_here" />
        <DocsCallout>
          Every endpoint in this guide requires the <InlineCode>generate</InlineCode> permission on
          the key (or a signed-in portal session, which has it implicitly). Grant it when creating
          or editing a key in <strong>API Access</strong> — a key without it gets a{" "}
          <InlineCode>403</InlineCode>.
        </DocsCallout>
        <p className="text-xs text-gray-600 mt-2">
          <strong>Own Key (BYOK):</strong> To power AI drafting with your personal Google Gemini or OpenAI key, pass <InlineCode>X-LLM-ApiKey: AIzaSy...</InlineCode> or the <InlineCode>api_key</InlineCode> body parameter.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="workflow" index={2} title="The workflow">
        <p>
          A <strong>template</strong> defines the document's shape (sections, field types, page
          layout); a <strong>draft</strong> is one instance of that template being filled in,
          identified by a <InlineCode>document_id</InlineCode>.
        </p>
        <ol className="list-decimal space-y-1 pl-5">
          <li>Register or upload a template (one-time setup per document type)</li>
          <li>Draft content from a plain-language instruction (AI Powered)</li>
          <li>Validate the draft and fill in any missing required fields</li>
          <li>Generate DOCX / PDF output</li>
          <li>Download the file</li>
        </ol>
      </DocsSectionCard>

      <DocsSectionCard id="templates" index={3} title="Set up a template">
        <p className="font-medium text-gray-800">Upload a .docx template (recommended)</p>
        <EndpointPath method="POST" path="/api/templates/upload" />
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
              <td className="py-1 pr-4 font-mono">file</td>
              <td className="py-1 pr-4">Yes</td>
              <td className="py-1">The .docx file to use as the template</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">template_id</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Defaults to the filename (lowercased, spaces/dashes → _)</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">title</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Human-readable title</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">description</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Short description</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">document_type</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Free-text category, e.g. "nda", "sow"</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">schema_json</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">A content_schema JSON object to override whatever is auto-detected</td>
            </tr>
          </tbody>
        </table>
        <CodeBlock language="bash" code={UPLOAD_CURL} />

        <p className="font-medium text-gray-800 mt-5">Register a template by hand (JSON)</p>
        <EndpointPath method="POST" path="/api/templates" />
        <CodeBlock language="json" code={TEMPLATE_JSON} />
      </DocsSectionCard>

      <DocsSectionCard id="section-types" index={4} title="Section types">
        <p>
          Set <InlineCode>"required": false</InlineCode> on any section that's optional — missing
          optional sections don't block generation, only missing <em>required</em> ones do.
        </p>
        <table className="w-full text-left text-xs">
          <thead className="text-gray-500">
            <tr>
              <th className="py-1 pr-4">type</th>
              <th className="py-1">Expected content shape</th>
            </tr>
          </thead>
          <tbody>
            {SECTION_TYPE_ROWS.map((row) => (
              <tr key={row.type} className="border-t border-gray-100">
                <td className="py-1.5 pr-4 font-mono">{row.type}</td>
                <td className="py-1.5">{row.shape}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </DocsSectionCard>

      <DocsSectionCard id="draft" index={5} title="Draft content from instructions">
        <KeyModeNavbar mode={draftMode} onChange={setDraftMode} />
        <EndpointPath method="POST" path="/api/documents/draft" />
        <p>
          Give the AI a plain-language instruction; it fills in as much of the template's schema
          as it can and flags anything it couldn't determine.
        </p>

        <p className="text-xs font-medium text-gray-500 mt-2">
          {draftMode === "default" ? "Request Body (Server Key)" : "Request Body (Own Key Mode)"}
        </p>
        <CodeBlock
          language="json"
          code={draftMode === "default" ? DRAFT_REQUEST_DEFAULT : DRAFT_REQUEST_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">cURL Request</p>
        <CodeBlock
          language="bash"
          code={draftMode === "default" ? DRAFT_CURL_DEFAULT : DRAFT_CURL_OWN_KEY}
        />

        <p className="text-xs font-medium text-gray-500 mt-3">Response</p>
        <CodeBlock language="json" code={DRAFT_RESPONSE} />
        <p className="text-xs text-gray-500 mt-1">
          <InlineCode>status</InlineCode> is <strong>needs_information</strong> (required fields
          missing — check <InlineCode>missing_fields</InlineCode> and go to Validate) or{" "}
          <strong>ready</strong> (skip straight to Generate). Save the returned{" "}
          <InlineCode>document_id</InlineCode> — every following step addresses this specific draft.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="validate" index={6} title="Validate and fill in gaps">
        <EndpointPath method="POST" path="/api/documents/{document_id}/validate" />
        <p>
          Call with no body to re-check completeness, or pass <InlineCode>content</InlineCode> to
          merge in values for previously-missing fields — only the fields you send are merged, not
          the whole draft.
        </p>
        <p className="text-xs font-medium text-gray-500">Request</p>
        <CodeBlock language="json" code={VALIDATE_REQUEST} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={VALIDATE_RESPONSE} />
        <DocsCallout tone="warn">
          <strong>Date fields</strong> are auto-filled with the current UTC date if left empty —
          you don't need to supply them unless you want a specific date.
        </DocsCallout>
      </DocsSectionCard>

      <DocsSectionCard id="generate" index={7} title="Generate the file(s)">
        <EndpointPath method="POST" path="/api/documents/{document_id}/generate" />
        <p>
          <InlineCode>formats</InlineCode> defaults to <InlineCode>["docx", "pdf"]</InlineCode> if
          omitted. Supported values: <InlineCode>docx</InlineCode>, <InlineCode>pdf</InlineCode>.
        </p>
        <CodeBlock language="json" code={GENERATE_REQUEST} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={GENERATE_RESPONSE} />
      </DocsSectionCard>

      <DocsSectionCard id="download" index={8} title="Download">
        <EndpointPath method="GET" path="/api/documents/{document_id}/download?format=docx" />
        <p>
          No authentication required — direct file link, safe to hand to an end user or
          embed in an email. <InlineCode>format</InlineCode> is <InlineCode>docx</InlineCode>{" "}
          (default) or <InlineCode>pdf</InlineCode>.
        </p>
        <CodeBlock language="bash" code={DOWNLOAD_CURL} />
      </DocsSectionCard>

      <DocsSectionCard id="code-examples" index={9} title="Server-side Code examples">
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

      <DocsSectionCard id="errors" index={10} title="Error handling">
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
      </DocsSectionCard>

      <DocsSectionCard id="good-practices" index={11} title="Good practices">
        <ul className="list-disc space-y-1 pl-5 text-xs text-gray-600">
          <li>
            <strong>Keep keys server-side:</strong> Never expose <InlineCode>X-Document-Api-Key</InlineCode> or <InlineCode>X-LLM-ApiKey</InlineCode> in browser code.
          </li>
          <li>
            <strong>Inspect before uploading:</strong> Use <InlineCode>POST /api/templates/inspect</InlineCode> to test Word templates and placeholder detection without persisting.
          </li>
          <li>
            <strong>Check needs_information:</strong> Always verify if <InlineCode>status === "needs_information"</InlineCode> so your application can prompt the user for any missing facts before generating output files.
          </li>
        </ul>
      </DocsSectionCard>
    </DocsShell>
  );
}
