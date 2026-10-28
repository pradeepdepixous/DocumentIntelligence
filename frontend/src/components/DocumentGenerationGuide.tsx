import CodeBlock from "./CodeBlock";
import DocsShell, { DocsCallout, DocsSectionCard, EndpointPath, InlineCode } from "./docs/DocsShell";

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

const DRAFT_CURL = `curl -X POST https://your-server/api/documents/draft \\
  -H "X-Document-Api-Key: pixous_your_key_here" \\
  -H "Content-Type: application/json" \\
  -d '{
    "template_id": "nda",
    "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term."
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

const JS_EXAMPLE = `const API_KEY = "pixous_your_key_here";
const BASE_URL = "https://your-server/api";
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
}

// Usage
let draft = await draftDocument("nda", "Mutual NDA between Acme Inc and Beta LLC, effective today.");
if (draft.status === "needs_information") {
  const result = await fillMissingFields(draft.document_id, {
    signatory: { name: "Jane Doe", designation: "General Counsel" },
  });
  draft.status = result.status;
}
const generated = await generateDocument(draft.document_id);
console.log(generated.files); // [{ format, download_url }, ...]`;

const PY_EXAMPLE = `import requests

API_KEY = "pixous_your_key_here"
BASE_URL = "https://your-server/api"
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
    return resp.json()

draft = draft_document("nda", "Mutual NDA between Acme Inc and Beta LLC, effective today.")
if draft["status"] == "needs_information":
    result = fill_missing_fields(draft["document_id"], {
        "signatory": {"name": "Jane Doe", "designation": "General Counsel"},
    })
    draft["status"] = result["status"]

generated = generate_document(draft["document_id"])
print(generated["files"])  # [{ "format": ..., "download_url": ... }, ...]`;

const CSHARP_EXAMPLE = `using var client = new HttpClient { BaseAddress = new Uri("https://your-server/api/") };
client.DefaultRequestHeaders.Add("X-Document-Api-Key", "pixous_your_key_here");

// 1. Draft content from instructions
var draftBody = JsonContent.Create(new {
    template_id = "nda",
    instructions = "Mutual NDA between Acme Inc and Beta LLC, effective today."
});
var draftResponse = await client.PostAsync("documents/draft", draftBody);
draftResponse.EnsureSuccessStatusCode();
var draft = await draftResponse.Content.ReadFromJsonAsync<JsonElement>();
string documentId = draft.GetProperty("document_id").GetString()!;

// 2. Fill in any missing required fields
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
      <DocsSectionCard id="authenticate" index={1} title="Authenticate">
        <p>Same as the rest of the API — send your key on every request:</p>
        <CodeBlock code="X-Document-Api-Key: pixous_your_key_here" />
        <DocsCallout>
          Every endpoint in this guide requires the <InlineCode>generate</InlineCode> permission on
          the key (or a signed-in portal session, which has it implicitly). Grant it when creating
          or editing a key in <strong>API Access</strong> — a key without it gets a{" "}
          <InlineCode>403</InlineCode>.
        </DocsCallout>
      </DocsSectionCard>

      <DocsSectionCard id="workflow" index={2} title="The workflow">
        <p>
          A <strong>template</strong> defines the document's shape (sections, field types, page
          layout); a <strong>draft</strong> is one instance of that template being filled in,
          identified by a <InlineCode>document_id</InlineCode>.
        </p>
        <ol className="list-decimal space-y-1 pl-5">
          <li>Register or upload a template (one-time setup per document type)</li>
          <li>Draft content from a plain-language instruction</li>
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
              <td className="py-1 pr-4 font-mono">title / description / document_type</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">Human-readable metadata</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-1 pr-4 font-mono">schema_json</td>
              <td className="py-1 pr-4">No</td>
              <td className="py-1">A content_schema JSON object to override whatever is auto-detected</td>
            </tr>
          </tbody>
        </table>
        <p>
          The parser scans the .docx for <strong>Word Content Controls</strong> (tagged or
          aliased) and plain <InlineCode>{"{{field_name}}"}</InlineCode> text placeholders —
          including dotted table columns like <InlineCode>{"{{line_items.description}}"}</InlineCode>{" "}
          — and turns each into a schema section.
        </p>
        <CodeBlock language="bash" code={UPLOAD_CURL} />
        <DocsCallout>
          <strong>Inspect without saving:</strong>{" "}
          <InlineCode>POST /api/templates/inspect</InlineCode> runs the same parser and returns
          the proposed template plus warnings/errors, without persisting anything — use it to
          preview/tune a template before uploading.
        </DocsCallout>

        <p className="mt-4 font-medium text-gray-800">Or register a template by hand (JSON)</p>
        <EndpointPath method="POST" path="/api/templates" />
        <CodeBlock language="json" code={TEMPLATE_JSON} />

        <p className="mt-4 font-medium text-gray-800">Other template endpoints</p>
        <table className="w-full text-left text-xs">
          <tbody>
            <tr className="border-t border-gray-100">
              <td className="py-2 pr-4"><EndpointPath method="GET" path="/api/templates" /></td>
              <td className="py-2">List all registered templates</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-2 pr-4"><EndpointPath method="GET" path="/api/templates/{id}?version=N" /></td>
              <td className="py-2">Get one template (optionally a specific version)</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-2 pr-4"><EndpointPath method="PUT" path="/api/templates/{id}" /></td>
              <td className="py-2">Update an existing template's schema</td>
            </tr>
            <tr className="border-t border-gray-100">
              <td className="py-2 pr-4"><EndpointPath method="GET" path="/api/templates/{id}/download" /></td>
              <td className="py-2">Download the raw uploaded .docx for a template (no auth required)</td>
            </tr>
          </tbody>
        </table>
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
        <EndpointPath method="POST" path="/api/documents/draft" />
        <p>
          Give the AI a plain-language instruction; it fills in as much of the template's schema
          as it can and flags anything it couldn't determine. Provide either{" "}
          <InlineCode>template_id</InlineCode> (a saved template) or an inline{" "}
          <InlineCode>template_definition</InlineCode>.
        </p>
        <CodeBlock language="bash" code={DRAFT_CURL} />
        <p className="text-xs font-medium text-gray-500">Response</p>
        <CodeBlock language="json" code={DRAFT_RESPONSE} />
        <p className="text-xs text-gray-500">
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
        <p className="text-xs text-gray-500">
          <strong>Preview at any time:</strong>{" "}
          <InlineCode>GET /api/documents/{"{document_id}"}/preview</InlineCode> returns the current
          content + markdown preview without changing anything.
        </p>
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
        <DocsCallout>
          <strong>DOCX rendering:</strong> if the template was created via{" "}
          <InlineCode>/api/templates/upload</InlineCode> with a real .docx on file, the docx output
          is produced by populating that original Word file's content controls/placeholders
          directly — formatting, headers, and styling are preserved exactly. Otherwise docx is
          generated from scratch using <InlineCode>presentation</InlineCode> settings. PDF is
          always rendered independently from the structured content.
        </DocsCallout>
        <p className="text-xs text-gray-500">
          You can call generate again later (e.g. after validating new edits) — it re-renders and
          overwrites the stored files for that document_id.
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="download" index={8} title="Download">
        <EndpointPath method="GET" path="/api/documents/{document_id}/download?format=docx" />
        <p>
          No authentication required — this is a direct file link, safe to hand to an end user or
          embed in an email. <InlineCode>format</InlineCode> is <InlineCode>docx</InlineCode>{" "}
          (default) or <InlineCode>pdf</InlineCode>. Returns 404 if that format hasn't been
          generated yet — call Generate first.
        </p>
        <CodeBlock language="bash" code={DOWNLOAD_CURL} />
      </DocsSectionCard>

      <DocsSectionCard id="code-examples" index={9} title="Code examples">
        <p className="text-xs font-medium text-gray-500">JavaScript (fetch)</p>
        <CodeBlock language="javascript" code={JS_EXAMPLE} />
        <p className="text-xs font-medium text-gray-500">Python (requests)</p>
        <CodeBlock language="python" code={PY_EXAMPLE} />
        <p className="text-xs font-medium text-gray-500">C#</p>
        <CodeBlock language="csharp" code={CSHARP_EXAMPLE} />
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
        <p className="text-xs text-gray-500">
          Every error response is <InlineCode>{"{ error }"}</InlineCode> (validation failures also
          include a <InlineCode>details</InlineCode> array).
        </p>
      </DocsSectionCard>

      <DocsSectionCard id="good-practices" index={11} title="Good practices">
        <ul className="list-disc space-y-1 pl-5">
          <li>
            <strong>Upload the real .docx as the template</strong> when one exists — it gives you
            pixel-perfect DOCX output and lets the parser infer the schema for you.
          </li>
          <li>
            <strong>Use inspect before upload</strong> when tuning a new template, so you can see
            detected sections/warnings and adjust the source .docx before saving.
          </li>
          <li>
            <strong>Loop validate until is_valid: true</strong> rather than calling generate
            speculatively — generate doesn't block on missing fields, so an incomplete draft will
            render with gaps.
          </li>
          <li>
            <strong>Re-use the document_id</strong> across validate/generate/download calls for the
            same document instance; start a new draft call only for a genuinely new document.
          </li>
        </ul>
      </DocsSectionCard>
    </DocsShell>
  );
}
