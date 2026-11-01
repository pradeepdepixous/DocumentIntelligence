# Pixous Doc Intelligence (.NET + Gemini 2.5 Flash VLM)

A production-ready .NET 8 / C# solution that integrates Google's **Gemini 2.5 Flash** Vision-Language Model (VLM) for multimodal document intelligence. 

It extracts context-aware structured data from multi-page documents (PDFs, TIFFs, PNGs, JPEGs) and delivers a strictly validated JSON response that includes:
- **Field Name & Value**: Standardized semantic key-value pairs
- **Confidence Score**: Quantitative score (0.00 – 1.00) reflecting visual OCR clarity and contextual certainty
- **Page Number**: Precise 1-based page index indicating where each item was found in the multi-page document
- **Evidence Context**: Verbatim text snippets from the page supporting the extracted data
- **Document Metadata**: Inferred document classification, overall confidence, total pages analyzed, and executive summary
- **Tabular / Line Items**: Structured representation for line items and financial tables

---

## 🔑 Local Setup & Secrets

This repo ships with safe placeholder values in `appsettings.json`. Real secrets go in
`appsettings.Development.json`, which is gitignored and loaded automatically on top of
`appsettings.json` regardless of environment. Create it with:

```json
{
  "Gemini": { "ApiKey": "YOUR_REAL_GEMINI_KEY" },
  "ConnectionStrings": { "DocumentIntelligence": "Host=localhost;Port=5432;Database=pixous_doc_intelligence;Username=postgres;Password=YOUR_PASSWORD" },
  "PortalAuth": {
    "GoogleClientId": "YOUR_GOOGLE_OAUTH_CLIENT_ID.apps.googleusercontent.com",
    "JwtSigningKey": "a-fresh-random-secret-at-least-32-characters-long",
    "AdminEmails": [ "your@email.com" ]
  }
}
```

For the frontend, copy `frontend/.env.example` to `frontend/.env` and set `VITE_GOOGLE_CLIENT_ID`
to the same Google OAuth client ID (also gitignored — never commit `.env`).

Run the backend with `dotnet run` (serves on `http://localhost:5000`, Swagger at `/swagger`), and
the frontend separately with `cd frontend && npm install && npm run dev` (serves on `:5173`, proxying
`/api` and `/swagger` to the backend).

---

## 🏗️ Project Architecture

```
d:\pixous-doc_int\
│── PixousDocIntelligence.csproj     # .NET 8 project configuration with PdfPig
│── appsettings.json                 # Gemini API Key and Model configuration
│── Program.cs                       # End-to-end 7-Phase Orchestrator & CLI
│── run.ps1 / run.bat                # Cross-terminal bootstrap helpers
├── Domain/
│   ├── Contracts/                   # Fixed Processing Envelope & Conflict Ledger
│   │   └── DocumentEnvelope.cs
│   └── Planning/                    # Phase 2: Gemini Instruction Planner
│       ├── ExtractionPlan.cs
│       ├── IInstructionPlanner.cs
│       └── InstructionPlanner.cs
├── Pipeline/
│   ├── Ingestion/                   # Phase 1: PDF SHA-256 Checksum & Page Counting
│   │   ├── DocumentJob.cs
│   │   ├── IDocumentIngestionService.cs
│   │   └── DocumentIngestionService.cs
│   ├── Batching/                    # Phase 3: Overlapping Batch Scheduler & Slicer
│   │   ├── BatchManifest.cs
│   │   ├── IBatchScheduler.cs
│   │   └── BatchScheduler.cs
│   ├── Extraction/                  # Phase 4: Gemini 2.5 Flash Batch Extraction Engine
│   │   ├── IGeminiBatchExtractor.cs
│   │   └── GeminiBatchExtractor.cs
│   ├── Stitching/                   # Phase 5: Confidence-Aware Stitcher & Conflict Ledger
│   │   ├── IStitchingEngine.cs
│   │   └── ConfidenceAwareStitcher.cs
│   └── Verification/                # Phase 6: Deterministic Evidence Verifier & Quality Queue
│       ├── EvidenceVerificationModels.cs
│       ├── IEvidenceVerifier.cs
│       └── EvidenceVerifier.cs
├── Persistence/                     # Phase 7: PostgreSQL Enterprise Schema & Entities
│   ├── Migrations/
│   │   └── V1__InitialDocumentIntelligenceSchema.sql
│   └── Entities/
│       └── DatabaseEntities.cs
├── Export/                          # Phase 7: RFC 4180 CSV & Review Queue Exporters
│   └── DocumentExportService.cs
├── Agents/                          # Route B: Lightweight Document Q&A & Summarizer
│   ├── IDocAnalystAgent.cs
│   └── DocAnalystAgent.cs
└── output/                          # Generated JSON, CSV, and Review Queue artifacts
```

---

## 🚀 Quick Start

### 1. Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- A [Google AI Studio API Key](https://aistudio.google.com/app/apikey)

### 2. Configure API Key
You can configure your API key in **three ways**:

#### Option A: In `appsettings.Development.json` (Local default)
```json
{
  "Gemini": {
    "ApiKey": "YOUR_ACTUAL_GEMINI_API_KEY",
    "Model": "gemini-flash-lite-latest"
  }
}
```

#### Option B: Environment Variable
```powershell
$env:GEMINI_API_KEY="YOUR_ACTUAL_GEMINI_API_KEY"
```

#### Option C: Per-Request BYOK Header (HTTP API)
Send your personal key directly in requests without changing server config:
```http
X-LLM-ApiKey: AIzaSyYourPersonalGeminiKeyHere
X-LLM-Provider: Gemini
X-LLM-Model: gemini-flash-lite-latest
```

### 3. Run the Application
Run the project and specify your document path:

```powershell
dotnet run -- "C:\path\to\your\document.pdf"
```

Or run interactively:
```powershell
dotnet run
```

---

## 📋 Sample Structured JSON Output (Key-Value Dictionary)

```json
{
  "document_type": "Company Brochure / Workspace Services Overview",
  "overall_summary": "This document is a comprehensive brochure for Workiify, a company offering simplified workspace solutions...",
  "total_pages_detected": 9,
  "overall_confidence": 0.98,
  "fields": {
    "company_name": {
      "value": "Workiify",
      "confidence": 0.99,
      "page_number": 1,
      "evidence_context": "WORKIIFY"
    },
    "services_offered": {
      "value": [
        "Serviced offices",
        "Coworking facilities",
        "Virtual Offices",
        "Extra Space Self Storage",
        "Ecobox (Eco friendly moving solutions)"
      ],
      "confidence": 0.97,
      "page_number": 1,
      "evidence_context": "Workiify Serviced offices and coworking facilities tailored to meet your needs"
    },
    "branch_locations": {
      "value": [
        "AV Info Tech Park – Saravanapatti",
        "AV Info Tech Office - Gandhipuram"
      ],
      "confidence": 0.97,
      "page_number": 3,
      "evidence_context": "OUR BRANCHES: AV Info Tech Park – Saravanapatti, AV Info Tech Office - Gandhipuram"
    },
    "email_address": {
      "value": "info@workiify.com",
      "confidence": 0.98,
      "page_number": 9,
      "evidence_context": "Email: info@workiify.com"
    },
    "website": {
      "value": "www.workiify.com",
      "confidence": 0.98,
      "page_number": 9,
      "evidence_context": "Website: www.workiify.com"
    }
  },
  "tables": []
}
```

### C# Direct Access Example
```csharp
// Instant O(1) dictionary lookups:
string companyName = result.Fields["company_name"].GetDisplayValue();
double confidence = result.Fields["company_name"].Confidence;
int pageNumber = result.Fields["company_name"].PageNumber;
```

---

## 💡 How It Works Under the Hood

1. **Multimodal Payload Delivery**:
   The document (PDF or image) is read and converted to Base64 in an `inlineData` part sent directly to the Gemini API endpoint.
2. **Deterministic Structured Output (`responseSchema`)**:
   Instead of relying on regex or fragile prompt parsing, the solution leverages Gemini's native OpenAPI `responseSchema` and `responseMimeType: "application/json"`. The model is mathematically constrained to only output tokens conforming to our exact schema.
3. **Contextual & Grounded Extraction**:
   Gemini 2.5 Flash reviews layout, visual typography, tables, and relationships across all pages, simultaneously attributing the exact 1-based `page_number` and `evidence_context` for auditability and verification.
