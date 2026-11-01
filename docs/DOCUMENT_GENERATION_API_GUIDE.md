# Document Generation API Guide

This guide shows you how to use the **Document Generation** API to turn a Word (.docx) template
plus a plain-language instruction into a fully drafted, downloadable document (DOCX and/or PDF).
It's a separate pipeline from the [Document Analyst Agents](AGENTS_API_GUIDE.md) (which *read* and
question already-processed documents) — this one *authors* new documents from a template.

If you just want to try this without writing code, use **Document Studio** in the web portal —
it drives the same API end to end with a UI.

---

## 1. Authenticate

All endpoints below (except downloads) require a signed-in portal session or API key with the
**`generate`** permission:

```
X-Document-Api-Key: pixous_your_key_here
```

Grant `generate` when creating or editing a key in **API Access** — it's separate from
`process`/`summarize`/`ask`/`evaluate`, which gate document processing and the Analyst Agents
instead. A signed-in portal session has it implicitly. A key without it gets a `403` on every
endpoint in this guide.

> **Bring Your Own LLM Key (BYOK):**
> AI drafting uses Google Gemini (or OpenAI) to populate template schemas from instructions.
> You can supply your personal key per-request via the `X-LLM-ApiKey` header or the `api_key` JSON parameter in the draft payload.

## 2. The workflow

Document generation is a five-step pipeline. A template defines the document's shape (sections,
field types, page layout); a draft is one instance of that template being filled in, identified by
a `document_id`.

```
┌──────────────┐   ┌───────────────┐   ┌────────────┐   ┌──────────────┐   ┌─────────────┐
│ 1. Register/ │ → │ 2. Draft      │ → │ 3. Validate │ → │ 4. Generate  │ → │ 5. Download │
│  upload a    │   │  content from │   │  / fill in  │   │  DOCX / PDF  │   │  the file   │
│  template    │   │  instructions │   │  gaps       │   │              │   │             │
└──────────────┘   └───────────────┘   └────────────┘   └──────────────┘   └─────────────┘
```

Step 1 is a one-time setup per document type (e.g. "NDA", "SOW", "Offer Letter"). Steps 2–5 run
once per document you actually generate.

---

## 3. Step 1 — Set up a template

A template can be defined in two ways: **upload an existing Word file** and let the API infer the
schema from it, or **register a JSON schema** by hand.

### 3.1 Upload a .docx template (recommended)

```
POST /api/templates/upload
Content-Type: multipart/form-data
```

| Field          | Required | Description                                                                 |
| -------------- | -------- | ----------------------------------------------------------------------------- |
| `file`         | Yes      | The `.docx` file to use as the template                                       |
| `template_id`  | No       | Defaults to the filename (lowercased, spaces/dashes → `_`)                    |
| `title`        | No       | Human-readable title                                                          |
| `description`  | No       | Short description                                                             |
| `document_type`| No       | Free-text category, e.g. `"nda"`, `"sow"`                                     |
| `schema_json`  | No       | A `content_schema` JSON object to override whatever is auto-detected          |

The parser scans the `.docx` for two kinds of placeholders and turns each into a schema section:

- **Word Content Controls** (Insert → Rich Text / Plain Text Content Control), tagged or aliased
  with the field name — these support text, multi-paragraph, bullet-list, and table fields.
- **Plain `{{field_name}}` text placeholders**, including dotted table columns like
  `{{line_items.description}}` and prefixed table markers like `{{table:approvals}}`.

```bash
curl -X POST https://your-server/api/templates/upload \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -F "file=@nda_template.docx" \
  -F "template_id=nda" \
  -F "title=Mutual NDA" \
  -F "document_type=nda"
```

Response (`201 Created`) is the full `DocumentTemplateDefinition` — see §3.3.

> **Inspect without saving:** `POST /api/templates/inspect` (same multipart fields, minus
> `schema_json`) runs the same parser and returns the proposed template plus `warnings` /
> `errors`, without persisting anything. Use this to preview/tune a template before uploading it.

### 3.2 Register a template by hand (JSON)

```
POST /api/templates
Content-Type: application/json
```

Body is a `DocumentTemplateDefinition`:

```json
{
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
}
```

### 3.3 Section types

| `type`            | Expected content shape                                                                 |
| ------------------ | ---------------------------------------------------------------------------------------- |
| `text`             | A single string                                                                          |
| `paragraphs`       | A string, or an array of strings (one per paragraph)                                     |
| `bullet_list` / `list` | An array of strings, or a single string                                              |
| `date`             | A string date — if omitted, the pipeline auto-fills today's UTC date (see §5)            |
| `address_block`    | An object `{ "name": "...", "address": "..." }`                                          |
| `signature_block`  | An object `{ "name": "...", "designation": "..." }`                                      |
| `table`            | An array of row objects, with keys matching the section's `fields`                       |
| `object`           | An arbitrary non-empty JSON object                                                       |

Set `"required": false` on any section that's optional — missing optional sections don't block
generation, only missing *required* ones do.

### 3.4 Other template endpoints

| Endpoint | Purpose |
| --- | --- |
| `GET /api/templates` | List all registered templates |
| `GET /api/templates/{id}?version=N` | Get one template (optionally a specific version) |
| `PUT /api/templates/{id}` | Update an existing template's schema |
| `GET /api/templates/{id}/download` | Download the raw uploaded `.docx` for a template (no auth required) |

---

## 4. Step 2 — Draft content from instructions

```
POST /api/documents/draft
Content-Type: application/json
```

Give the AI a plain-language instruction; it fills in as much of the template's schema as it can
and flags anything it couldn't determine.

| Field                | Required | Description                                                                 |
| --------------------- | -------- | ----------------------------------------------------------------------------- |
| `template_id`         | One of `template_id` / `template_definition` | ID of a previously registered template |
| `template_version`    | No       | Specific version of the template (defaults to latest)                        |
| `template_definition` | One of `template_id` / `template_definition` | Inline template schema, instead of referencing a saved one |
| `instructions`        | Yes      | Natural-language brief, e.g. `"NDA between Acme Inc and Beta LLC, effective today, standard mutual confidentiality obligations"` |
| `output_format`       | No       | Hint only (`"docx"` default) — actual formats are chosen later in Step 4     |
| `provider`            | No       | `"Gemini"` or `"OpenAI"` — defaults to server configuration                  |
| `api_key`             | No       | Bring-your-own LLM key (Google Gemini or OpenAI)                              |
| `model`               | No       | Specific model name                                                          |

#### Mode A: Default Implementation (Server Key)
```bash
curl -X POST https://your-server/api/documents/draft \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "template_id": "nda",
    "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term."
  }'
```

#### Mode B: Own Key Implementation (BYOK Gemini)
```bash
curl -X POST https://your-server/api/documents/draft \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere" \
  -H "Content-Type: application/json" \
  -d '{
    "template_id": "nda",
    "instructions": "Mutual NDA between Acme Inc (123 Market St, SF) and Beta LLC (55 Harbor Rd, NY), effective today, 2-year term.",
    "api_key": "AIzaSyYourPersonalGeminiKeyHere",
    "provider": "Gemini",
    "model": "gemini-flash-lite-latest"
  }'
```

**Response:**

```json
{
  "document_id": "DOC-20261028-4F2A9B1C",
  "status": "needs_information",
  "template_id": "nda",
  "template_version": 1,
  "missing_fields": ["signatory.name", "signatory.designation"],
  "content": {
    "effective_date": "2026-10-28",
    "disclosing_party": { "name": "Acme Inc", "address": "123 Market St, SF" },
    "receiving_party": { "name": "Beta LLC", "address": "55 Harbor Rd, NY" },
    "purpose": ["This Agreement governs the exchange of confidential information between the parties for evaluating a potential business relationship."]
  },
  "markdown_preview": "# Mutual NDA\n\n..."
}
```

`status` is one of:

- **`needs_information`** — one or more *required* sections are missing or empty. Check
  `missing_fields` and go to Step 3 to supply them.
- **`ready`** — everything required is present; you can skip straight to Step 4.

Save the returned `document_id` — every following step addresses this specific draft.

---

## 5. Step 3 — Validate and fill in gaps

```
POST /api/documents/{document_id}/validate
Content-Type: application/json
```

Call this with no body (or `{}`) to re-check the current draft's completeness, or pass
`content` to merge in values the end user supplied for previously-missing fields.

```json
{
  "content": {
    "signatory": { "name": "Jane Doe", "designation": "General Counsel" }
  }
}
```

```bash
curl -X POST https://your-server/api/documents/DOC-20261028-4F2A9B1C/validate \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"content": {"signatory": {"name": "Jane Doe", "designation": "General Counsel"}}}'
```

**Response:**

```json
{
  "document_id": "DOC-20261028-4F2A9B1C",
  "is_valid": true,
  "status": "ready",
  "missing_fields": [],
  "errors": []
}
```

Fields you submit are merged into the stored draft (not replaced wholesale), so you only need to
send the fields you're filling in. Repeat this call until `is_valid` is `true` / `status` is
`"ready"`.

> **Date fields:** any section of type `"date"` (or with id `"date"`) is auto-filled with the
> current UTC date if left empty — you don't need to supply it unless you want a specific date.

> **Preview at any time:** `GET /api/documents/{document_id}/preview` returns the same shape as
> the draft response (current content + `markdown_preview`) without changing anything — useful for
> showing a live preview in a UI while the user edits fields.

---

## 6. Step 4 — Generate the file(s)

```
POST /api/documents/{document_id}/generate
Content-Type: application/json
```

```json
{ "formats": ["docx", "pdf"] }
```

`formats` defaults to `["docx", "pdf"]` if omitted. Supported values: `"docx"`, `"pdf"`.

```bash
curl -X POST https://your-server/api/documents/DOC-20261028-4F2A9B1C/generate \
  -H "X-Document-Api-Key: pixous_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"formats": ["docx", "pdf"]}'
```

**Response:**

```json
{
  "document_id": "DOC-20261028-4F2A9B1C",
  "status": "generated",
  "files": [
    { "format": "docx", "download_url": "/api/documents/DOC-20261028-4F2A9B1C/download?format=docx" },
    { "format": "pdf", "download_url": "/api/documents/DOC-20261028-4F2A9B1C/download?format=pdf" }
  ]
}
```

> **DOCX rendering:** if the template was created via `/api/templates/upload` (i.e. a real `.docx`
> was stored for it), the `docx` output is produced by populating that *original* Word file's
> content controls/placeholders directly — so your formatting, headers, and styling are preserved
> exactly. If no source `.docx` is on file (e.g. a hand-registered JSON template), `docx` is
> generated from scratch based on `presentation` (page size, font, margins). `pdf` is always
> rendered independently from the structured content and `presentation` settings.

You can call `generate` again later (e.g. after validating new edits) — it re-renders and
overwrites the stored files for that `document_id`.

---

## 7. Step 5 — Download

```
GET /api/documents/{document_id}/download?format=docx
```

No authentication required — this is a direct file link, safe to hand to an end user or embed in
an email. `format` is `docx` (default) or `pdf`. Returns `404` if that format hasn't been
generated yet for this document — call Step 4 first.

```bash
curl -OJ "https://your-server/api/documents/DOC-20261028-4F2A9B1C/download?format=pdf"
```

---

## 8. Code examples

### JavaScript (fetch)

```javascript
const API_KEY = "pixous_your_key_here";
const BASE_URL = "https://your-server/api";
const HEADERS = { "X-Document-Api-Key": API_KEY, "Content-Type": "application/json" };

async function draftDocument(templateId, instructions) {
  const res = await fetch(`${BASE_URL}/documents/draft`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ template_id: templateId, instructions }),
  });
  if (!res.ok) throw new Error(`Draft failed: ${res.status}`);
  return res.json();
}

async function fillMissingFields(documentId, content) {
  const res = await fetch(`${BASE_URL}/documents/${documentId}/validate`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ content }),
  });
  if (!res.ok) throw new Error(`Validate failed: ${res.status}`);
  return res.json();
}

async function generateDocument(documentId, formats = ["docx", "pdf"]) {
  const res = await fetch(`${BASE_URL}/documents/${documentId}/generate`, {
    method: "POST",
    headers: HEADERS,
    body: JSON.stringify({ formats }),
  });
  if (!res.ok) throw new Error(`Generate failed: ${res.status}`);
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
console.log(generated.files); // [{ format, download_url }, ...]
```

### Python (requests)

```python
import requests

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
print(generated["files"])  # [{ "format": ..., "download_url": ... }, ...]
```

### C#

```csharp
using var client = new HttpClient { BaseAddress = new Uri("https://your-server/api/") };
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
Console.WriteLine(generated.GetProperty("files"));
```

---

## 9. Error handling

| Status | Meaning                                                                 | What to do                                                            |
| ------ | -------------------------------------------------------------------------- | ------------------------------------------------------------------------ |
| `400`  | Missing `template_id`/`template_definition`, invalid template schema, or unsupported output format | Fix the request body per the `error` message                         |
| `401`  | Missing or invalid API key / session                                   | Check the `X-Document-Api-Key` header is present and the key hasn't been revoked |
| `403`  | Key doesn't have the `generate` permission                             | Add `generate` to the key's permissions in **API Access**              |
| `404`  | Template or document draft not found                                   | Confirm the `template_id`/`document_id` is correct and was created first |
| `500`  | Rendering failure (DOCX/PDF generation error)                          | Check the `error` message for the underlying cause; retry              |
| `502`  | The underlying LLM provider failed during drafting                     | Retry the draft call, or switch `provider`/`model`                     |

Every error response is `{ "error": "..." }` (validation failures also include a `details` array).

## 10. Good practices

- **Upload the real `.docx` as the template** when one exists — it gives you pixel-perfect DOCX
  output and lets the parser infer the schema for you instead of hand-writing it.
- **Use `inspect` before `upload`** when tuning a new template, so you can see the detected
  sections/warnings and adjust the source `.docx` (or pass `schema_json` overrides) before saving.
- **Loop `validate` until `is_valid: true`** rather than calling `generate` speculatively —
  `generate` doesn't block on missing fields itself, so an incomplete draft will render with gaps.
- **Re-use the `document_id`** across validate/generate/download calls for the same document
  instance; start a new `draft` call only when authoring a genuinely new document.
