# Generic Document Validation API Guide

This guide shows you how to use the **Document Validation Engine** to inspect any uploaded document (PDF or image) against custom validation requirements without writing backend code or hardcoding document types.

It's an independent capability of the Pixous Document Intelligence platform:
- **Extraction:** Reads and extracts tabular and schema data into structured JSON.
- **Validation:** Inspects documents against business criteria and returns a deterministic `valid`, `invalid`, or `manual_review` decision with supporting visual and textual evidence.
- **Summarization & Q&A:** Grounded analyst agents that answer queries over documents.

---

## 1. Authentication & BYOK (Bring Your Own Key)

All validation endpoints accept either an API key or a portal JWT bearer session:

```http
X-Document-Api-Key: pixous_your_api_key_here
```

### Bring Your Own Gemini Key (BYOK)
You can supply your personal or customer-specific Gemini key per request using standard headers:

```http
X-LLM-Provider: Gemini
X-LLM-ApiKey: AIzaSy...your_gemini_key
X-LLM-Model: gemini-flash-latest
```

If these headers are omitted, the engine uses the platform's centrally configured Gemini key.

---

## 2. Architecture & Decision Policy

The validation engine strictly separates **AI Observation** from **Business Decision-Making**:

```
[ Uploaded Document ] ──► [ Gemini Analyzer ] ──► [ AI Observations ] (Facts & Evidence)
                                                            │
                                                            ▼
[ Validation Profile ] ─────────────────────────► [ .NET Rule Evaluator ]
                                                            │
                                                            ▼
                                                  [ .NET Decision Service ]
                                                            │
                                                            ▼
                                               Valid | Invalid | ManualReview
```

- **Gemini** only observes visible facts, quotes evidence, and estimates legibility/certainty. It does **not** approve or reject documents.
- **.NET Evaluator & Decision Service** applies deterministic rules:
  - If any mandatory check is **missing/failed** $\to$ **`invalid`**
  - If any mandatory check is **ambiguous/uncertain** $\to$ **`manual_review`** (or policy-specified action)
  - If all mandatory checks pass $\to$ **`valid`**

---

## 3. Two Integration Approaches

### Approach A: Inline Validation (Instant Testing)
Send the document file and validation criteria together in a single multipart request. Best for testing and applications with dynamic validation requirements.

### Approach B: Reusable Validation Profiles (Recommended for Production)
Register a validation profile once, receive a `profileId`, and reference that profile for all subsequent document uploads.

---

## 4. Endpoints Reference

### 4.1 Create a Validation Profile
`POST /api/validation/profiles`

```json
{
  "profileId": "profile_income_cert",
  "name": "Income Certificate Validation",
  "documentType": "income_certificate",
  "description": "Standard revenue department income certificate check",
  "decisionPolicy": {
    "onUncertain": "manual_review",
    "minConfidenceThreshold": 0.80
  },
  "requirements": [
    {
      "id": "doc_identity",
      "description": "The document must be an income certificate",
      "type": "DocumentClassification",
      "required": true
    },
    {
      "id": "issuing_authority",
      "description": "Issuing authority (Tahsildar / Revenue Department) must be identifiable",
      "type": "ContentPresence",
      "required": true
    },
    {
      "id": "certificate_number",
      "description": "Certificate registration number must be visible",
      "type": "ContentPresence",
      "required": true
    },
    {
      "id": "issue_date",
      "description": "Issue date must be visible and legible",
      "type": "ContentPresence",
      "required": true
    }
  ]
}
```

**Response (`201 Created`):**
```json
{
  "profileId": "profile_income_cert",
  "tenantId": "default-tenant",
  "name": "Income Certificate Validation",
  "documentType": "income_certificate",
  "version": 1,
  "isActive": true,
  "createdAtUtc": "2026-10-30T09:45:00Z"
}
```

---

### 4.2 Validate an Uploaded Document
`POST /api/documents/validate`  
`Content-Type: multipart/form-data`

| Parameter | Type | Description |
| :--- | :--- | :--- |
| `file` | Binary | PDF, PNG, JPEG, or WebP document file (up to 10 pages) |
| `profileId` | Text | ID of a saved profile (e.g. `profile_income_cert`) |
| `requirementsJson` | Text | *(Optional)* Inline JSON requirements string (Alternative to `profileId`) |

#### Example using `curl` with a Saved Profile:
```bash
curl -X POST "http://localhost:5000/api/documents/validate" \
  -H "X-Document-Api-Key: pixous_your_api_key" \
  -F "file=@income_certificate.pdf" \
  -F "profileId=profile_income_cert"
```

#### Example using `curl` with Inline Requirements:
```bash
curl -X POST "http://localhost:5000/api/documents/validate" \
  -H "X-Document-Api-Key: pixous_your_api_key" \
  -F "file=@community_certificate.pdf" \
  -F 'requirementsJson={"documentType":"community_certificate","requirements":[{"id":"community_name","description":"Community name specified","required":true},{"id":"seal","description":"Official seal visible","required":true}]}'
```

---

### 4.3 Validation Response Format

**Response (`200 OK`):**
```json
{
  "validationId": "val_1a2f4e714f8e4825820fd44251c6004d",
  "documentType": "income_certificate",
  "status": "valid",
  "isValid": true,
  "checks": [
    {
      "requirementId": "doc_identity",
      "status": "passed",
      "evidence": "Header clearly states 'Government of Tamil Nadu - Revenue Department Income Certificate'",
      "confidence": 0.99,
      "required": true
    },
    {
      "requirementId": "issuing_authority",
      "status": "passed",
      "evidence": "Seal of the Zonal Deputy Tahsildar with digital signature",
      "confidence": 0.96,
      "required": true
    },
    {
      "requirementId": "certificate_number",
      "status": "passed",
      "evidence": "Application / Certificate No: REV/INC/2026/89410",
      "confidence": 0.98,
      "required": true
    },
    {
      "requirementId": "issue_date",
      "status": "passed",
      "evidence": "Dated: 18/03/2026",
      "confidence": 0.95,
      "required": true
    }
  ],
  "message": "Document successfully satisfied all validation requirements.",
  "validatedAtUtc": "2026-10-30T09:47:00Z"
}
```

---

### 4.4 Check Statuses & Overall Decisions

#### Individual Check Status (`checks[].status`):
- **`passed`**: Visible evidence found with confidence $\ge$ `minConfidenceThreshold`.
- **`failed`**: Item is explicitly missing or classification is mismatched.
- **`uncertain`**: Item is partially obscured, blurry, unreadable, or below the confidence threshold.

#### Overall Document Decision (`status` & `isValid`):
- **`valid` (`isValid: true`)**: All mandatory requirements passed.
- **`invalid` (`isValid: false`)**: One or more mandatory requirements failed.
- **`manual_review` (`isValid: null`)**: Mandatory requirements were ambiguous or unreadable and need human review.

---

### 4.5 Retrieve Historical Validation Audit
`GET /api/validation/{validationId}` or `GET /api/documents/validation/{validationId}`

```http
GET /api/validation/val_1a2f4e714f8e4825820fd44251c6004d
```

Returns the immutable audit record stored in PostgreSQL, including the exact `profileVersion` used at the time of evaluation.

---

### 4.6 Update a Profile (Automatic Versioning)
`PUT /api/validation/profiles/{profileId}`

When you update a profile, its `version` is automatically incremented (`1 -> 2 -> 3`). Historical validation records remain linked to the exact version that was active when they were validated.

