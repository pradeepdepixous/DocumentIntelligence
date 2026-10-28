import { useState, useEffect, useRef, type ChangeEvent, type DragEvent } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import type {
  DocumentTemplateDefinition,
  TemplateInspectionResult,
  DraftResponse,
  ValidateDraftResponse,
  GenerateResponse,
} from "../lib/documentGenerationTypes";

interface PresetItem {
  name: string;
  templateIdMatch: string;
  category: string;
  instructions: string;
}

const PRESETS: PresetItem[] = [
  {
    name: "Metro Health Portal (BRD)",
    templateIdMatch: "brd",
    category: "Business Requirements",
    instructions: `Draft a comprehensive Business Requirements Document for Metro Health System's Patient Engagement Portal.

Key Details:
- Project: Metro Health Next-Gen Patient Portal
- Author: Sarah Jenkins, Senior Business Analyst
- Version: 1.0.0
- Executive Summary: Metro Health is modernizing its digital patient front door to unify appointment booking, lab results delivery, telehealth consultations, and insurance verification across 14 hospital campuses and 60 outpatient clinics.
- Business Objectives: Reduce call center load by 40%, cut patient intake registration time from 15 mins to under 3 mins, and achieve 99.9% uptime.
- Scope: In-scope: Patient self-service registration, EHR bidirectional sync (HL7/FHIR), automated SMS/email reminders. Out-of-scope: In-hospital inpatient bedside monitor integration.
- Stakeholders: Dr. Amanda Vance (Chief Medical Officer), Marcus Chen (VP Patient Experience), Priya Patel (Lead Cloud Architect), David Miller (Director of Nursing).
- Functional Requirements:
  1. Patient Registration & Identity Proofing via OAuth2 and photo ID capture.
  2. Telehealth Video Sessions with WebRTC encryption.
  3. Real-time Lab & Diagnostic Imaging results delivery with push notification.
  4. Online Copay & Bill Settlement through Stripe Healthcare integration.
- Timeline: Q1 Phase 1 Design, Q2 Pilot in 3 clinics, Q3 Full Regional Rollout.`,
  },
  {
    name: "MedSync EHR Gateway (SRS)",
    templateIdMatch: "srs",
    category: "Software Specs",
    instructions: `Draft a detailed Software Requirements Specification for MedSync Interoperability Gateway.

Key Details:
- System: MedSync Interoperability Gateway v2.4
- Lead Architect: Alex Mercer, Principal Software Engineer
- Purpose: Provide a high-throughput, HIPAA-compliant microservices gateway that translates proprietary clinic EHR formats into standardized HL7 FHIR v4 JSON payloads.
- Architecture: Microservices event-driven architecture hosted on Kubernetes. Kafka handles asynchronous event messaging, Redis caches patient session tokens, and PostgreSQL stores audit logs.
- User Classes: System Administrators, Hospital Integration Specialists, Compliance Officers, External Healthcare APIs.
- Functional Requirements:
  1. FHIR Resource Transformation: Ingest HL7 v2 and map to FHIR R4 Bundle within 250ms.
  2. Token Introspection: Validate SMART-on-FHIR JWT tokens with asymmetric RSA-256.
  3. Patient Consent Verification: Enforce opt-in/opt-out patient policy filter on all query endpoints.
  4. Audit Trail: Cryptographically sign and store immutable audit log for all PHI access.
- Non-Functional Requirements:
  1. Latency: 95th percentile response time under 180ms.
  2. Availability: 99.99% uptime with multi-region active-passive failover.
  3. Security: End-to-end TLS 1.3, AES-256 at rest, FIPS 140-2 validated encryption.`,
  },
  {
    name: "Executive Letter of Intent",
    templateIdMatch: "letter",
    category: "Corporate Letter",
    instructions: `Draft an official Executive Letter of Intent between Pixous Technologies and Apex Enterprise Solutions regarding a strategic cloud data intelligence partnership. Include formal greeting, partnership scope, term duration of 3 years, and closing executive signature block.`,
  },
];

const TAG_GROUPS = [
  {
    category: "Header & Document Metadata",
    tags: [
      { name: "{{project_name}}", desc: "Project or product name" },
      { name: "{{document_title}}", desc: "Main title of document" },
      { name: "{{version}}", desc: "Document version (e.g. 1.0)" },
      { name: "{{author}}", desc: "Document author or analyst" },
      { name: "{{date}}", desc: "Publication / current date" },
    ],
  },
  {
    category: "Executive & Prose Sections",
    tags: [
      { name: "{{executive_summary}}", desc: "Executive summary narrative" },
      { name: "{{business_objectives}}", desc: "Key business goals" },
      { name: "{{project_scope}}", desc: "In-scope and out-of-scope" },
      { name: "{{system_overview}}", desc: "Architecture & system description" },
      { name: "{{user_classes}}", desc: "User roles and personas" },
      { name: "{{non_functional_requirements}}", desc: "Performance, uptime, SLAs" },
      { name: "{{security_requirements}}", desc: "Auth, encryption, compliance" },
    ],
  },
  {
    category: "Dynamic Repeating Tables",
    tags: [
      { name: "functional_requirements", desc: "Columns: {{id}}, {{requirement}}, {{priority}}" },
      { name: "stakeholders", desc: "Columns: {{name}}, {{role}}, {{responsibility}}" },
      { name: "project_milestones", desc: "Columns: {{phase}}, {{target_date}}, {{deliverables}}" },
      { name: "risk_matrix", desc: "Columns: {{risk}}, {{severity}}, {{mitigation}}" },
    ],
  },
];

export default function DocumentStudio() {
  // State: Templates
  const [templates, setTemplates] = useState<DocumentTemplateDefinition[]>([]);
  const [selectedTemplateId, setSelectedTemplateId] = useState<string>("");
  const [loadingTemplates, setLoadingTemplates] = useState<boolean>(true);

  // State: Instructions & Draft
  const [instructions, setInstructions] = useState<string>(PRESETS[0].instructions);
  const [outputFormat, setOutputFormat] = useState<"docx" | "pdf">("docx");
  const [drafting, setDrafting] = useState<boolean>(false);
  const [draftStepText, setDraftStepText] = useState<string>("");
  const [currentDraft, setCurrentDraft] = useState<DraftResponse | null>(null);
  const [activeTab, setActiveTab] = useState<"rendered" | "json">("rendered");

  // State: Missing Fields & Refinement
  const [missingInputs, setMissingInputs] = useState<Record<string, string>>({});
  const [validating, setValidating] = useState<boolean>(false);

  // State: Generating & Downloading
  const [generating, setGenerating] = useState<boolean>(false);
  const [downloadSuccess, setDownloadSuccess] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [copiedTag, setCopiedTag] = useState<string | null>(null);

  // State: Template Modal & Tabs
  const [showTemplateModal, setShowTemplateModal] = useState<boolean>(false);
  const [modalTab, setModalTab] = useState<"guide" | "starters" | "upload" | "list">("guide");

  // State: Template Upload & Inspection
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [inspecting, setInspecting] = useState<boolean>(false);
  const [uploadSaving, setUploadSaving] = useState<boolean>(false);
  const [inspectionResult, setInspectionResult] = useState<TemplateInspectionResult | null>(null);
  const [uploadTitle, setUploadTitle] = useState<string>("");
  const [uploadCategory, setUploadCategory] = useState<string>("brd");
  const [uploadDescription, setUploadDescription] = useState<string>("");
  const [isDragOver, setIsDragOver] = useState<boolean>(false);

  const fileInputRef = useRef<HTMLInputElement>(null);

  // Load registered templates on mount
  useEffect(() => {
    loadTemplates();
  }, []);

  async function loadTemplates(preferredId?: string) {
    setLoadingTemplates(true);
    try {
      const data = await apiFetch<DocumentTemplateDefinition[]>("/templates");
      setTemplates(data || []);
      if (data && data.length > 0) {
        if (preferredId && data.some((t) => t.template_id === preferredId)) {
          setSelectedTemplateId(preferredId);
        } else if (!selectedTemplateId) {
          const brdMatch = data.find((t) => t.template_id.includes("brd")) || data[0];
          setSelectedTemplateId(brdMatch.template_id);
        }
      }
    } catch (err) {
      console.error("Failed to load templates", err);
    } finally {
      setLoadingTemplates(false);
    }
  }

  const activeTemplate = templates.find((t) => t.template_id === selectedTemplateId);

  function handleSelectPreset(preset: PresetItem) {
    setInstructions(preset.instructions);
    const match = templates.find((t) =>
      t.template_id.toLowerCase().includes(preset.templateIdMatch.toLowerCase())
    );
    if (match) {
      setSelectedTemplateId(match.template_id);
    }
  }

  function handleCopyTag(tag: string) {
    navigator.clipboard.writeText(tag);
    setCopiedTag(tag);
    setTimeout(() => setCopiedTag(null), 2000);
  }

  function downloadStarterFile(filename: string) {
    const directUrl = `/templates/${filename}`;
    const link = document.createElement("a");
    link.href = directUrl;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setDownloadSuccess(`Downloaded starter template: ${filename}`);
  }

  // Step 1: Draft with AI
  async function handleDraftDocument() {
    if (!selectedTemplateId) {
      setErrorMessage("Please select a document template first.");
      return;
    }
    if (!instructions.trim()) {
      setErrorMessage("Please enter instructions or choose a preset.");
      return;
    }

    setDrafting(true);
    setErrorMessage(null);
    setDownloadSuccess(null);
    setDraftStepText("Analyzing template structure & schema...");

    const stepTimer1 = setTimeout(() => {
      setDraftStepText("Synthesizing content with DeepSeek / LLM engine...");
    }, 1500);

    const stepTimer2 = setTimeout(() => {
      setDraftStepText("Validating business rules & formatting tables...");
    }, 3500);

    try {
      const res = await apiFetch<DraftResponse>("/documents/draft", {
        method: "POST",
        body: JSON.stringify({
          template_id: selectedTemplateId,
          instructions: instructions.trim(),
          output_format: outputFormat,
        }),
      });

      setCurrentDraft(res);
      const initialMissing: Record<string, string> = {};
      if (res.missing_fields && res.missing_fields.length > 0) {
        res.missing_fields.forEach((f) => {
          initialMissing[f] = "";
        });
      }
      setMissingInputs(initialMissing);
    } catch (err: any) {
      if (err instanceof ApiError) {
        setErrorMessage(err.message || "Failed to generate draft.");
      } else {
        setErrorMessage("An unexpected error occurred while communicating with the document generation pipeline.");
      }
    } finally {
      clearTimeout(stepTimer1);
      clearTimeout(stepTimer2);
      setDrafting(false);
      setDraftStepText("");
    }
  }

  // Step 2: Refine Missing Fields
  async function handleRefineMissingFields() {
    if (!currentDraft) return;

    setValidating(true);
    setErrorMessage(null);

    try {
      const updatedContent = { ...(currentDraft.content || {}) };
      for (const [key, val] of Object.entries(missingInputs)) {
        if (val.trim()) {
          updatedContent[key] = val.trim();
        }
      }

      const res = await apiFetch<ValidateDraftResponse>(`/documents/${currentDraft.document_id}/validate`, {
        method: "POST",
        body: JSON.stringify({ content: updatedContent }),
      });

      setCurrentDraft((prev) => {
        if (!prev) return null;
        return {
          ...prev,
          status: res.status,
          missing_fields: res.missing_fields,
          content: updatedContent,
        };
      });

      const remaining: Record<string, string> = {};
      res.missing_fields.forEach((f) => {
        remaining[f] = missingInputs[f] || "";
      });
      setMissingInputs(remaining);
    } catch (err: any) {
      setErrorMessage(err instanceof ApiError ? err.message : "Validation update failed.");
    } finally {
      setValidating(false);
    }
  }

  // Step 3: Generate & Download Document
  async function handleGenerateAndDownload(format: "docx" | "pdf") {
    if (!currentDraft) return;

    setGenerating(true);
    setErrorMessage(null);
    setDownloadSuccess(null);

    try {
      await apiFetch<GenerateResponse>(`/documents/${currentDraft.document_id}/generate`, {
        method: "POST",
        body: JSON.stringify({ formats: [format] }),
      });

      const downloadPath = `/documents/${currentDraft.document_id}/download?format=${format}`;
      const downloadFilename = `${currentDraft.template_id}_${currentDraft.document_id.slice(0, 8)}.${format}`;

      const res = await fetch(`/api${downloadPath}`);
      if (!res.ok) {
        throw new Error(`Failed to download rendered ${format.toUpperCase()} file`);
      }
      const blob = await res.blob();
      const blobUrl = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = blobUrl;
      a.download = downloadFilename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(blobUrl);

      setDownloadSuccess(`Successfully downloaded ${downloadFilename}`);
    } catch (err: any) {
      setErrorMessage(err.message || `Failed to generate ${format.toUpperCase()} document.`);
    } finally {
      setGenerating(false);
    }
  }

  // File Upload & Inspection Flow
  async function handleFileSelected(file: File) {
    if (!file.name.toLowerCase().endsWith(".docx")) {
      setErrorMessage("Please select a valid Word (.docx) template file.");
      return;
    }

    setUploadFile(file);
    setInspecting(true);
    setInspectionResult(null);
    setErrorMessage(null);

    try {
      const formData = new FormData();
      formData.append("file", file);

      const inspectData = await apiFetch<TemplateInspectionResult>("/templates/inspect", {
        method: "POST",
        body: formData,
      });

      setInspectionResult(inspectData);
      setUploadTitle(inspectData.template.title || file.name.replace(/\.[^/.]+$/, ""));
      setUploadCategory(inspectData.template.document_type || "document");
      setUploadDescription(inspectData.template.description || "");
    } catch (err: any) {
      setErrorMessage(err instanceof ApiError ? err.message : "Failed to inspect uploaded .docx template.");
    } finally {
      setInspecting(false);
    }
  }

  function handleDropFile(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragOver(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      handleFileSelected(e.dataTransfer.files[0]);
    }
  }

  async function handleSaveUploadedTemplate() {
    if (!uploadFile || !inspectionResult) return;

    setUploadSaving(true);
    setErrorMessage(null);

    try {
      const formData = new FormData();
      formData.append("file", uploadFile);
      formData.append("template_id", inspectionResult.template_id);
      formData.append("title", uploadTitle);
      formData.append("document_type", uploadCategory);
      formData.append("description", uploadDescription);

      const saved = await apiFetch<DocumentTemplateDefinition>("/templates/upload", {
        method: "POST",
        body: formData,
      });

      await loadTemplates(saved.template_id);
      setShowTemplateModal(false);
      setUploadFile(null);
      setInspectionResult(null);
      setDownloadSuccess(`Template "${saved.title || saved.template_id}" uploaded and registered successfully!`);
    } catch (err: any) {
      setErrorMessage(err instanceof ApiError ? err.message : "Failed to save template.");
    } finally {
      setUploadSaving(false);
    }
  }

  return (
    <div className="space-y-6">
      {/* Studio Header */}
      <div className="flex flex-col gap-4 rounded-2xl border border-border-subtle bg-white p-6 shadow-xs md:flex-row md:items-center md:justify-between">
        <div>
          <div className="flex items-center gap-2.5">
            <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-navy text-sm text-white shadow-xs">
              📄
            </span>
            <h1 className="text-xl font-bold tracking-tight text-gray-900">Document Studio</h1>
            <span className="rounded-full bg-blue-50 px-2.5 py-0.5 text-xs font-semibold text-blue-700">
              AI Document Pipeline
            </span>
          </div>
          <p className="mt-1 text-sm text-gray-500">
            Generate publication-ready Word (.docx) and PDF documents dynamically from templates without hardcoded schemas.
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2.5">
          <button
            onClick={() => {
              setModalTab("guide");
              setShowTemplateModal(true);
            }}
            className="flex items-center gap-2 rounded-lg border border-blue-200 bg-blue-50/60 px-3 py-2 text-xs font-semibold text-blue-800 shadow-xs hover:bg-blue-100"
          >
            <span>💡</span>
            <span>Template Guide & Cheat Sheet</span>
          </button>

          <button
            onClick={() => {
              setModalTab("upload");
              setShowTemplateModal(true);
            }}
            className="flex items-center gap-2 rounded-lg border border-border-subtle bg-white px-3 py-2 text-xs font-semibold text-gray-700 shadow-xs hover:bg-gray-50"
          >
            <svg className="h-4 w-4 text-gray-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
            </svg>
            <span>Upload Template (.docx)</span>
          </button>

          {currentDraft && (
            <button
              onClick={() => {
                setCurrentDraft(null);
                setDownloadSuccess(null);
                setErrorMessage(null);
              }}
              className="rounded-lg bg-gray-100 px-3 py-2 text-xs font-semibold text-gray-600 hover:bg-gray-200"
            >
              Reset Canvas
            </button>
          )}
        </div>
      </div>

      {/* Starter Templates Quick-Banner */}
      <div className="rounded-xl border border-emerald-200 bg-gradient-to-r from-emerald-50/80 via-teal-50/50 to-white p-4 shadow-xs">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-start gap-3">
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-emerald-600 text-white text-base shadow-xs">
              📥
            </span>
            <div>
              <h2 className="text-xs font-bold uppercase tracking-wider text-emerald-950">
                Starter Word Templates (Ready to Use)
              </h2>
              <p className="text-xs text-emerald-800 mt-0.5">
                New to templates? Download pre-configured Word templates with sample tags and formatted tables, customize them in Word, and upload.
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={() => downloadStarterFile("brd_template.docx")}
              className="flex items-center gap-1.5 rounded-lg border border-emerald-300 bg-white px-3 py-1.5 text-xs font-bold text-emerald-800 shadow-xs hover:bg-emerald-100 transition-all cursor-pointer"
            >
              <span>📄</span>
              <span>Download BRD Starter (.docx)</span>
            </button>

            <button
              onClick={() => downloadStarterFile("srs_template.docx")}
              className="flex items-center gap-1.5 rounded-lg border border-emerald-300 bg-white px-3 py-1.5 text-xs font-bold text-emerald-800 shadow-xs hover:bg-emerald-100 transition-all cursor-pointer"
            >
              <span>⚙️</span>
              <span>Download SRS Starter (.docx)</span>
            </button>
          </div>
        </div>
      </div>

      {/* Global Alerts */}
      {errorMessage && (
        <div className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 flex items-start gap-3">
          <span className="text-red-500 font-bold">⚠️</span>
          <div className="flex-1">{errorMessage}</div>
          <button onClick={() => setErrorMessage(null)} className="text-red-400 hover:text-red-600 text-xs">✕</button>
        </div>
      )}

      {downloadSuccess && (
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span>✅</span>
            <span>{downloadSuccess}</span>
          </div>
          <button onClick={() => setDownloadSuccess(null)} className="text-emerald-500 hover:text-emerald-700 text-xs">✕</button>
        </div>
      )}

      {/* Main Studio Grid */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-12">
        {/* Left Column: AI Co-Pilot & Prompting (5 cols) */}
        <div className="space-y-5 lg:col-span-5">
          {/* Quick-Start Presets */}
          <div className="rounded-xl border border-border-subtle bg-white p-4 shadow-xs">
            <div className="mb-2.5 flex items-center justify-between">
              <span className="text-xs font-semibold tracking-wide uppercase text-gray-500">Quick Prompt Presets</span>
              <span className="text-[11px] text-gray-400">Click to autofill</span>
            </div>
            <div className="flex flex-wrap gap-2">
              {PRESETS.map((p) => (
                <button
                  key={p.name}
                  onClick={() => handleSelectPreset(p)}
                  className="rounded-lg border border-border-subtle bg-gray-50 px-2.5 py-1.5 text-left text-xs font-medium text-gray-700 transition-colors hover:border-blue-400 hover:bg-blue-50 hover:text-blue-700"
                >
                  <span className="block font-semibold">{p.name}</span>
                  <span className="text-[10px] text-gray-400">{p.category}</span>
                </button>
              ))}
            </div>
          </div>

          {/* Template Selector Card */}
          <div className="rounded-xl border border-border-subtle bg-white p-5 shadow-xs space-y-4">
            <div>
              <div className="flex items-center justify-between mb-1.5">
                <label className="block text-xs font-bold uppercase tracking-wider text-gray-600">
                  Target Template
                </label>
                <button
                  type="button"
                  onClick={() => {
                    setModalTab("list");
                    setShowTemplateModal(true);
                  }}
                  className="text-[11px] text-blue-600 hover:text-blue-800 font-semibold"
                >
                  Browse all ({templates.length})
                </button>
              </div>

              {loadingTemplates ? (
                <div className="h-9 w-full animate-pulse rounded-lg bg-gray-100" />
              ) : (
                <select
                  value={selectedTemplateId}
                  onChange={(e) => setSelectedTemplateId(e.target.value)}
                  className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-800 shadow-xs focus:border-navy focus:outline-none focus:ring-1 focus:ring-navy"
                >
                  {templates.map((tpl) => (
                    <option key={tpl.template_id} value={tpl.template_id}>
                      {tpl.title || tpl.template_id} (v{tpl.version} - {tpl.document_type})
                    </option>
                  ))}
                </select>
              )}
            </div>

            {/* Selected Template Badge Summary */}
            {activeTemplate && (
              <div className="rounded-lg bg-slate-50 p-3 text-xs border border-gray-200 space-y-1.5">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-gray-700">{activeTemplate.title || activeTemplate.template_id}</span>
                  <span className="rounded bg-blue-100 px-2 py-0.5 text-[10px] font-semibold text-blue-800 uppercase">
                    {activeTemplate.document_type}
                  </span>
                </div>
                <p className="text-gray-500 line-clamp-2">{activeTemplate.description || "No description provided."}</p>
                <div className="flex items-center gap-3 pt-1 text-[11px] text-gray-500">
                  <span>Sections: <strong>{activeTemplate.content_schema?.sections?.length || 0}</strong></span>
                  <span>•</span>
                  <span>
                    Tables: <strong>{activeTemplate.content_schema?.sections?.filter((s) => s.type === "table")?.length || 0}</strong>
                  </span>
                  <span>•</span>
                  <span>Page: <strong>{activeTemplate.presentation?.page_size || "Letter"}</strong></span>
                </div>
              </div>
            )}

            {/* Instruction Prompt Area */}
            <div>
              <div className="flex items-center justify-between mb-1.5">
                <label className="text-xs font-bold uppercase tracking-wider text-gray-600">
                  Document Instructions & Requirements
                </label>
                <span className="text-[11px] text-gray-400">Natural language outline</span>
              </div>
              <textarea
                rows={9}
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
                placeholder="Describe your document requirements, objectives, scope, stakeholders, or system specifications..."
                className="w-full rounded-lg border border-gray-300 p-3 text-xs leading-relaxed font-sans text-gray-800 shadow-xs focus:border-navy focus:outline-none focus:ring-1 focus:ring-navy"
              />
            </div>

            {/* Format Selector & Draft Action */}
            <div className="flex items-center justify-between pt-1">
              <div className="flex items-center gap-2">
                <span className="text-xs text-gray-500 font-medium">Output:</span>
                <button
                  type="button"
                  onClick={() => setOutputFormat("docx")}
                  className={`rounded-md px-2.5 py-1 text-xs font-semibold ${
                    outputFormat === "docx"
                      ? "bg-navy text-white shadow-xs"
                      : "bg-gray-100 text-gray-600 hover:bg-gray-200"
                  }`}
                >
                  DOCX (Word)
                </button>
                <button
                  type="button"
                  onClick={() => setOutputFormat("pdf")}
                  className={`rounded-md px-2.5 py-1 text-xs font-semibold ${
                    outputFormat === "pdf"
                      ? "bg-navy text-white shadow-xs"
                      : "bg-gray-100 text-gray-600 hover:bg-gray-200"
                  }`}
                >
                  PDF
                </button>
              </div>

              <button
                type="button"
                disabled={drafting || !selectedTemplateId}
                onClick={handleDraftDocument}
                className="flex items-center gap-2 rounded-lg bg-navy px-4 py-2.5 text-xs font-bold text-white shadow-md hover:bg-navy-dark disabled:opacity-50 transition-all cursor-pointer"
              >
                {drafting ? (
                  <>
                    <svg className="h-4 w-4 animate-spin text-white" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                    </svg>
                    <span>Generating...</span>
                  </>
                ) : (
                  <>
                    <span>✨</span>
                    <span>Draft with AI</span>
                  </>
                )}
              </button>
            </div>

            {/* Drafting Progress Animation */}
            {drafting && (
              <div className="rounded-lg bg-blue-50 border border-blue-200 p-3 text-xs text-blue-800 flex items-center gap-2.5 animate-pulse">
                <span className="h-2 w-2 rounded-full bg-blue-600 animate-ping" />
                <span className="font-medium">{draftStepText || "Processing document draft..."}</span>
              </div>
            )}
          </div>

          {/* Missing Information & Refinement Bar */}
          {currentDraft && currentDraft.status === "needs_information" && currentDraft.missing_fields?.length > 0 && (
            <div className="rounded-xl border border-amber-300 bg-amber-50/80 p-5 shadow-xs space-y-3">
              <div className="flex items-center gap-2">
                <span className="text-amber-600 font-bold">⚠️</span>
                <h3 className="text-xs font-bold uppercase tracking-wider text-amber-900">
                  Missing Mandatory Information ({currentDraft.missing_fields.length})
                </h3>
              </div>
              <p className="text-xs text-amber-800">
                The template requires the following specific fields which were not detected in the instructions. Please fill them in to complete the draft:
              </p>

              <div className="space-y-2.5 pt-1">
                {currentDraft.missing_fields.map((field) => (
                  <div key={field}>
                    <label className="block text-[11px] font-semibold text-amber-900 mb-0.5">
                      {field.replace(/_/g, " ").toUpperCase()}
                    </label>
                    <input
                      type="text"
                      value={missingInputs[field] || ""}
                      onChange={(e) =>
                        setMissingInputs({ ...missingInputs, [field]: e.target.value })
                      }
                      placeholder={`Enter value for ${field}...`}
                      className="w-full rounded-md border border-amber-300 bg-white px-2.5 py-1.5 text-xs text-gray-800 shadow-xs focus:border-amber-600 focus:outline-none"
                    />
                  </div>
                ))}
              </div>

              <button
                type="button"
                disabled={validating}
                onClick={handleRefineMissingFields}
                className="w-full rounded-lg bg-amber-700 py-2 text-xs font-bold text-white shadow-xs hover:bg-amber-800 disabled:opacity-50"
              >
                {validating ? "Validating & Updating..." : "Refine & Complete Draft →"}
              </button>
            </div>
          )}

          {/* Export Action Card */}
          {currentDraft && (
            <div className="rounded-xl border border-border-subtle bg-white p-5 shadow-xs space-y-3">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-xs font-bold uppercase tracking-wider text-gray-700">Document Actions</h3>
                  <p className="text-[11px] text-gray-500">Document ID: {currentDraft.document_id.slice(0, 12)}...</p>
                </div>
                <span
                  className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${
                    currentDraft.status === "ready" || currentDraft.status === "generated"
                      ? "bg-emerald-100 text-emerald-800"
                      : "bg-amber-100 text-amber-800"
                  }`}
                >
                  Status: {currentDraft.status}
                </span>
              </div>

              <div className="grid grid-cols-2 gap-2.5 pt-1">
                <button
                  type="button"
                  disabled={generating}
                  onClick={() => handleGenerateAndDownload("docx")}
                  className="flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-3 py-2.5 text-xs font-bold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50 cursor-pointer"
                >
                  <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zM6 20V4h7v5h5v11H6z" />
                  </svg>
                  <span>Download .DOCX</span>
                </button>

                <button
                  type="button"
                  disabled={generating}
                  onClick={() => handleGenerateAndDownload("pdf")}
                  className="flex items-center justify-center gap-2 rounded-lg bg-red-600 px-3 py-2.5 text-xs font-bold text-white shadow-sm hover:bg-red-700 disabled:opacity-50 cursor-pointer"
                >
                  <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 14H7v-2h5v2zm5-4H7v-2h10v2zm0-4H7V7h10v2z" />
                  </svg>
                  <span>Download .PDF</span>
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Right Column: Live Document Preview Canvas (7 cols) */}
        <div className="space-y-4 lg:col-span-7">
          <div className="flex items-center justify-between rounded-xl border border-border-subtle bg-white px-4 py-2.5 shadow-xs">
            <div className="flex items-center gap-2">
              <span className="text-xs font-bold uppercase tracking-wide text-gray-500">Document Canvas</span>
              {currentDraft && (
                <span className="rounded bg-gray-100 px-2 py-0.5 text-[11px] font-mono text-gray-600">
                  {currentDraft.template_id}
                </span>
              )}
            </div>

            <div className="flex rounded-lg bg-gray-100 p-0.5">
              <button
                type="button"
                onClick={() => setActiveTab("rendered")}
                className={`rounded-md px-3 py-1 text-xs font-semibold transition-all ${
                  activeTab === "rendered" ? "bg-white text-navy shadow-xs" : "text-gray-500 hover:text-gray-800"
                }`}
              >
                Rendered Document
              </button>
              <button
                type="button"
                onClick={() => setActiveTab("json")}
                className={`rounded-md px-3 py-1 text-xs font-semibold transition-all ${
                  activeTab === "json" ? "bg-white text-navy shadow-xs" : "text-gray-500 hover:text-gray-800"
                }`}
              >
                Structured JSON
              </button>
            </div>
          </div>

          {/* Canvas Sheet */}
          <div className="rounded-2xl border border-border-subtle bg-white p-6 shadow-sm min-h-[640px] max-h-[820px] overflow-y-auto">
            {!currentDraft ? (
              <div className="flex h-[550px] flex-col items-center justify-center text-center p-8">
                <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-blue-50 text-2xl text-blue-600 shadow-inner">
                  📝
                </div>
                <h3 className="text-base font-semibold text-gray-900">No Document Draft Generated Yet</h3>
                <p className="mt-1 max-w-sm text-xs text-gray-500">
                  Select a template on the left, customize the prompt or choose a preset, and click <strong>"Draft with AI"</strong> to preview your document live.
                </p>
                <div className="mt-6 flex flex-wrap justify-center gap-2">
                  {PRESETS.map((p) => (
                    <button
                      key={p.name}
                      onClick={() => handleSelectPreset(p)}
                      className="rounded-full border border-gray-200 bg-gray-50 px-3 py-1 text-xs text-gray-600 hover:border-blue-400 hover:bg-blue-50"
                    >
                      Try: {p.name}
                    </button>
                  ))}
                </div>
              </div>
            ) : activeTab === "json" ? (
              <div className="space-y-3">
                <div className="flex items-center justify-between text-xs text-gray-500 border-b pb-2">
                  <span>Structured Document JSON Payload</span>
                  <button
                    onClick={() => navigator.clipboard.writeText(JSON.stringify(currentDraft.content, null, 2))}
                    className="rounded bg-gray-100 px-2 py-1 text-[11px] font-semibold text-gray-700 hover:bg-gray-200"
                  >
                    Copy JSON
                  </button>
                </div>
                <pre className="rounded-lg bg-gray-900 p-4 font-mono text-xs text-emerald-400 overflow-x-auto leading-relaxed">
                  {JSON.stringify(currentDraft.content, null, 2)}
                </pre>
              </div>
            ) : (
              /* Rendered Paper Preview */
              <div className="space-y-6">
                <div className="border-b border-gray-200 pb-5">
                  <div className="flex items-center justify-between">
                    <span className="text-[11px] font-mono tracking-widest uppercase text-gray-400">
                      PIXOUS DOCUMENT INTELLIGENCE
                    </span>
                    <span className="text-xs font-mono text-gray-400">
                      ID: {currentDraft.document_id.slice(0, 8)}
                    </span>
                  </div>
                  <h2 className="mt-2 text-2xl font-bold tracking-tight text-gray-900">
                    {String(
                      currentDraft.content?.project_name ||
                      currentDraft.content?.system_name ||
                      currentDraft.content?.title ||
                      activeTemplate?.title ||
                      "Generated Document Draft"
                    )}
                  </h2>
                  <div className="mt-2 flex flex-wrap items-center gap-3 text-xs text-gray-500">
                    {currentDraft.content?.version && (
                      <span className="rounded bg-gray-100 px-2 py-0.5 font-medium text-gray-700">
                        Version: {String(currentDraft.content.version)}
                      </span>
                    )}
                    {currentDraft.content?.author && (
                      <span>Author: <strong>{String(currentDraft.content.author)}</strong></span>
                    )}
                    {currentDraft.content?.date && (
                      <span>Date: <strong>{String(currentDraft.content.date)}</strong></span>
                    )}
                  </div>
                </div>

                {/* Render Sections */}
                <div className="space-y-6 text-xs text-gray-800 leading-relaxed">
                  {Object.entries(currentDraft.content || {}).map(([key, val]) => {
                    if (["project_name", "system_name", "title", "version", "author", "date"].includes(key)) {
                      return null;
                    }

                    const prettyKey = key.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());

                    if (Array.isArray(val) && val.length > 0 && typeof val[0] === "object") {
                      const columns = Object.keys(val[0]);
                      return (
                        <div key={key} className="space-y-2 pt-2">
                          <h3 className="text-sm font-bold text-gray-900 border-l-4 border-navy pl-2.5">
                            {prettyKey}
                          </h3>
                          <div className="overflow-x-auto rounded-lg border border-gray-200">
                            <table className="min-w-full divide-y divide-gray-200 text-left text-xs">
                              <thead className="bg-gray-50 font-semibold text-gray-700 uppercase tracking-wider text-[11px]">
                                <tr>
                                  {columns.map((col) => (
                                    <th key={col} className="px-3 py-2">
                                      {col.replace(/_/g, " ")}
                                    </th>
                                  ))}
                                </tr>
                              </thead>
                              <tbody className="divide-y divide-gray-100 bg-white">
                                {val.map((row, idx) => (
                                  <tr key={idx} className={idx % 2 === 0 ? "bg-white" : "bg-gray-50/50"}>
                                    {columns.map((col) => (
                                      <td key={col} className="px-3 py-2 text-gray-800 align-top">
                                        {String(row[col] ?? "")}
                                      </td>
                                    ))}
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        </div>
                      );
                    }

                    if (Array.isArray(val)) {
                      return (
                        <div key={key} className="space-y-1.5 pt-2">
                          <h3 className="text-sm font-bold text-gray-900 border-l-4 border-navy pl-2.5">
                            {prettyKey}
                          </h3>
                          <ul className="list-disc pl-5 space-y-1 text-gray-700">
                            {val.map((item, idx) => (
                              <li key={idx}>{String(item)}</li>
                            ))}
                          </ul>
                        </div>
                      );
                    }

                    if (typeof val === "object" && val !== null) {
                      return (
                        <div key={key} className="space-y-2 pt-2">
                          <h3 className="text-sm font-bold text-gray-900 border-l-4 border-navy pl-2.5">
                            {prettyKey}
                          </h3>
                          <div className="rounded-lg bg-gray-50 p-3 border border-gray-200 grid grid-cols-2 gap-2 text-xs">
                            {Object.entries(val).map(([subK, subV]) => (
                              <div key={subK}>
                                <span className="font-semibold text-gray-600 capitalize">
                                  {subK.replace(/_/g, " ")}:{" "}
                                </span>
                                <span className="text-gray-800">{String(subV)}</span>
                              </div>
                            ))}
                          </div>
                        </div>
                      );
                    }

                    return (
                      <div key={key} className="space-y-1.5 pt-2">
                        <h3 className="text-sm font-bold text-gray-900 border-l-4 border-navy pl-2.5">
                          {prettyKey}
                        </h3>
                        <div className="text-gray-700 whitespace-pre-line leading-relaxed">
                          {String(val)}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Comprehensive Template Hub Modal */}
      {showTemplateModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-gray-900/60 backdrop-blur-xs p-4">
          <div className="w-full max-w-3xl rounded-2xl bg-white shadow-2xl border border-gray-200 overflow-hidden animate-in fade-in zoom-in-95 duration-150 flex flex-col max-h-[85vh]">
            {/* Modal Header */}
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4 bg-gray-50">
              <div>
                <h3 className="text-base font-bold text-gray-900">Word Template Center</h3>
                <p className="text-xs text-gray-500">Guides, sample templates, and custom Word (.docx) upload</p>
              </div>
              <button
                onClick={() => {
                  setShowTemplateModal(false);
                  setUploadFile(null);
                  setInspectionResult(null);
                }}
                className="text-gray-400 hover:text-gray-600 font-bold text-lg"
              >
                ✕
              </button>
            </div>

            {/* Modal Tabs Bar */}
            <div className="flex border-b border-gray-200 bg-white px-6">
              <button
                type="button"
                onClick={() => setModalTab("guide")}
                className={`py-3 px-4 text-xs font-bold border-b-2 transition-all ${
                  modalTab === "guide"
                    ? "border-navy text-navy"
                    : "border-transparent text-gray-500 hover:text-gray-800"
                }`}
              >
                💡 Visual Guide & Cheat Sheet
              </button>
              <button
                type="button"
                onClick={() => setModalTab("starters")}
                className={`py-3 px-4 text-xs font-bold border-b-2 transition-all ${
                  modalTab === "starters"
                    ? "border-navy text-navy"
                    : "border-transparent text-gray-500 hover:text-gray-800"
                }`}
              >
                📥 Download Starter Templates
              </button>
              <button
                type="button"
                onClick={() => setModalTab("upload")}
                className={`py-3 px-4 text-xs font-bold border-b-2 transition-all ${
                  modalTab === "upload"
                    ? "border-navy text-navy"
                    : "border-transparent text-gray-500 hover:text-gray-800"
                }`}
              >
                ⬆️ Upload New .DOCX
              </button>
              <button
                type="button"
                onClick={() => setModalTab("list")}
                className={`py-3 px-4 text-xs font-bold border-b-2 transition-all ${
                  modalTab === "list"
                    ? "border-navy text-navy"
                    : "border-transparent text-gray-500 hover:text-gray-800"
                }`}
              >
                📑 Registered Templates ({templates.length})
              </button>
            </div>

            {/* Modal Body */}
            <div className="p-6 space-y-5 overflow-y-auto flex-1">
              {/* TAB 1: Visual Guide & Cheat Sheet */}
              {modalTab === "guide" && (
                <div className="space-y-6 text-xs text-gray-700">
                  <div className="rounded-xl bg-blue-50/70 border border-blue-200 p-4">
                    <h4 className="font-bold text-sm text-blue-950 mb-1">How Template Formatting Works in Word</h4>
                    <p className="text-blue-800 leading-relaxed">
                      You do not need any coding or macros! Simply write standard Word text and enclose variable names in double curly brackets like <code className="bg-blue-100 px-1 py-0.5 rounded text-blue-900 font-bold">{"{{tag_name}}"}</code>.
                    </p>
                  </div>

                  {/* 3 Step Visual Rules */}
                  <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                    <div className="rounded-xl border border-gray-200 bg-white p-3.5 space-y-2 shadow-xs">
                      <div className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-navy text-white text-[10px] font-bold">1</span>
                        <h5 className="font-bold text-gray-900">Simple Text Tags</h5>
                      </div>
                      <p className="text-gray-500 text-[11px]">For single line items like title, author, date, or version:</p>
                      <div className="rounded bg-gray-50 p-2 font-mono text-[11px] text-gray-800 border border-gray-200">
                        Project: {"{{project_name}}"}<br />
                        Date: {"{{date}}"}
                      </div>
                    </div>

                    <div className="rounded-xl border border-gray-200 bg-white p-3.5 space-y-2 shadow-xs">
                      <div className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-navy text-white text-[10px] font-bold">2</span>
                        <h5 className="font-bold text-gray-900">Multi-Line Paragraphs</h5>
                      </div>
                      <p className="text-gray-500 text-[11px]">Put the tag on its own line below any Word heading:</p>
                      <div className="rounded bg-gray-50 p-2 font-mono text-[11px] text-gray-800 border border-gray-200">
                        ## Executive Summary<br />
                        {"{{executive_summary}}"}
                      </div>
                    </div>

                    <div className="rounded-xl border border-gray-200 bg-white p-3.5 space-y-2 shadow-xs">
                      <div className="flex items-center gap-2">
                        <span className="flex h-5 w-5 items-center justify-center rounded-full bg-navy text-white text-[10px] font-bold">3</span>
                        <h5 className="font-bold text-gray-900">Dynamic Tables</h5>
                      </div>
                      <p className="text-gray-500 text-[11px]">Insert a 2-row table. Row 1 is header, Row 2 holds tags:</p>
                      <div className="rounded bg-gray-50 p-1.5 font-mono text-[10px] text-gray-800 border border-gray-200">
                        | ID | Requirement | Priority |<br />
                        | {"{{id}}"} | {"{{requirement}}"} | {"{{priority}}"} |
                      </div>
                    </div>
                  </div>

                  {/* Interactive Click-to-Copy Tag Library */}
                  <div className="space-y-3 pt-2">
                    <div className="flex items-center justify-between">
                      <h4 className="font-bold text-xs uppercase tracking-wider text-gray-800">
                        Click-to-Copy Tag Library
                      </h4>
                      {copiedTag && (
                        <span className="text-xs font-bold text-emerald-600 animate-pulse">
                          ✓ Copied {copiedTag} to clipboard!
                        </span>
                      )}
                    </div>
                    <p className="text-[11px] text-gray-500">
                      Click any tag below to copy it, then paste it directly into your Microsoft Word document:
                    </p>

                    <div className="space-y-3">
                      {TAG_GROUPS.map((grp) => (
                        <div key={grp.category} className="rounded-xl border border-gray-200 bg-gray-50/50 p-3 space-y-2">
                          <span className="text-[11px] font-bold uppercase tracking-wider text-gray-600 block">
                            {grp.category}
                          </span>
                          <div className="flex flex-wrap gap-2">
                            {grp.tags.map((t) => (
                              <button
                                key={t.name}
                                type="button"
                                onClick={() => handleCopyTag(t.name)}
                                title={t.desc}
                                className="group flex items-center gap-1.5 rounded-lg border border-gray-200 bg-white px-2.5 py-1 text-xs font-mono text-gray-800 shadow-xs hover:border-blue-400 hover:bg-blue-50 hover:text-blue-700 transition-all cursor-pointer"
                              >
                                <span>{t.name}</span>
                                <span className="text-[10px] text-gray-400 group-hover:text-blue-500">📋</span>
                              </button>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              )}

              {/* TAB 2: Download Starter Templates */}
              {modalTab === "starters" && (
                <div className="space-y-4 text-xs text-gray-700">
                  <p className="text-gray-500">
                    These pre-formatted Word documents contain verified placeholders, headings, styles, and dynamic tables. Open them in Word, adapt with your own company branding, and upload them back.
                  </p>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    {/* BRD Card */}
                    <div className="rounded-xl border border-gray-200 bg-white p-4 space-y-3 shadow-xs">
                      <div className="flex items-center justify-between">
                        <span className="rounded bg-blue-100 px-2 py-0.5 text-[10px] font-bold text-blue-800 uppercase">
                          BRD Starter
                        </span>
                        <span className="text-[11px] font-mono text-gray-400">brd_template.docx</span>
                      </div>
                      <h4 className="font-bold text-sm text-gray-900">Business Requirements Document</h4>
                      <p className="text-gray-500 text-xs">
                        Includes Executive Summary, Business Objectives, Project Scope, Stakeholders Table, Functional Requirements Table, and Project Milestones.
                      </p>
                      <button
                        onClick={() => downloadStarterFile("brd_template.docx")}
                        className="w-full flex items-center justify-center gap-2 rounded-lg bg-blue-600 py-2 text-xs font-bold text-white shadow-xs hover:bg-blue-700"
                      >
                        <span>📥</span>
                        <span>Download BRD Starter (.docx)</span>
                      </button>
                    </div>

                    {/* SRS Card */}
                    <div className="rounded-xl border border-gray-200 bg-white p-4 space-y-3 shadow-xs">
                      <div className="flex items-center justify-between">
                        <span className="rounded bg-indigo-100 px-2 py-0.5 text-[10px] font-bold text-indigo-800 uppercase">
                          SRS Starter
                        </span>
                        <span className="text-[11px] font-mono text-gray-400">srs_template.docx</span>
                      </div>
                      <h4 className="font-bold text-sm text-gray-900">Software Requirements Specification</h4>
                      <p className="text-gray-500 text-xs">
                        Includes System Overview, Architectural Constraints, User Classes, Functional Requirements Table, Non-Functional Requirements, and Security Specs.
                      </p>
                      <button
                        onClick={() => downloadStarterFile("srs_template.docx")}
                        className="w-full flex items-center justify-center gap-2 rounded-lg bg-indigo-600 py-2 text-xs font-bold text-white shadow-xs hover:bg-indigo-700"
                      >
                        <span>📥</span>
                        <span>Download SRS Starter (.docx)</span>
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {/* TAB 3: Upload & Inspect New .DOCX */}
              {modalTab === "upload" && (
                <div className="space-y-4">
                  <div
                    onDragOver={(e) => {
                      e.preventDefault();
                      setIsDragOver(true);
                    }}
                    onDragLeave={() => setIsDragOver(false)}
                    onDrop={handleDropFile}
                    onClick={() => fileInputRef.current?.click()}
                    className={`flex flex-col items-center justify-center rounded-xl border-2 border-dashed p-6 text-center cursor-pointer transition-all ${
                      isDragOver
                        ? "border-blue-500 bg-blue-50"
                        : "border-gray-300 bg-gray-50/50 hover:bg-gray-100/50"
                    }`}
                  >
                    <input
                      ref={fileInputRef}
                      type="file"
                      accept=".docx"
                      onChange={(e: ChangeEvent<HTMLInputElement>) => {
                        if (e.target.files && e.target.files[0]) {
                          handleFileSelected(e.target.files[0]);
                        }
                      }}
                      className="hidden"
                    />
                    <span className="text-3xl mb-2">📄</span>
                    <p className="text-sm font-semibold text-gray-800">
                      {uploadFile ? uploadFile.name : "Click to select or drag & drop a Word (.docx) file"}
                    </p>
                    <p className="text-xs text-gray-400 mt-1">Supports Word documents with curly bracket tags like {"{{project_name}}"}</p>
                  </div>

                  {inspecting && (
                    <div className="flex items-center justify-center gap-2 py-4 text-xs font-semibold text-blue-700">
                      <svg className="h-4 w-4 animate-spin text-blue-600" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                      </svg>
                      <span>Inspecting Word template structure, sections, and tables...</span>
                    </div>
                  )}

                  {inspectionResult && (
                    <div className="space-y-4 rounded-xl bg-slate-50 border border-border-subtle p-4">
                      <div className="flex items-center justify-between">
                        <span className="text-xs font-bold uppercase tracking-wider text-emerald-700">
                          ✓ Template Schema Detected
                        </span>
                        <span className="text-xs font-mono text-gray-500">ID: {inspectionResult.template_id}</span>
                      </div>

                      <div className="grid grid-cols-2 gap-3 text-xs">
                        <div>
                          <label className="block text-[11px] font-semibold text-gray-600 mb-1">Template Title</label>
                          <input
                            type="text"
                            value={uploadTitle}
                            onChange={(e) => setUploadTitle(e.target.value)}
                            className="w-full rounded border border-gray-300 bg-white px-2.5 py-1.5 text-xs text-gray-800"
                          />
                        </div>
                        <div>
                          <label className="block text-[11px] font-semibold text-gray-600 mb-1">Document Type</label>
                          <select
                            value={uploadCategory}
                            onChange={(e) => setUploadCategory(e.target.value)}
                            className="w-full rounded border border-gray-300 bg-white px-2.5 py-1.5 text-xs text-gray-800"
                          >
                            <option value="brd">Business Requirements (BRD)</option>
                            <option value="srs">Software Requirements (SRS)</option>
                            <option value="letter">Formal Letter</option>
                            <option value="report">Business Report</option>
                            <option value="contract">Agreement / Contract</option>
                          </select>
                        </div>
                      </div>

                      <div>
                        <label className="block text-[11px] font-semibold text-gray-600 mb-1">Description</label>
                        <input
                          type="text"
                          value={uploadDescription}
                          onChange={(e) => setUploadDescription(e.target.value)}
                          placeholder="e.g. Standard enterprise business requirements template"
                          className="w-full rounded border border-gray-300 bg-white px-2.5 py-1.5 text-xs text-gray-800"
                        />
                      </div>

                      <div className="space-y-2 pt-2">
                        <span className="text-xs font-bold text-gray-700">
                          Discovered Elements ({inspectionResult.template.content_schema?.sections?.length || 0})
                        </span>
                        <div className="max-h-40 overflow-y-auto space-y-1 rounded border border-gray-200 bg-white p-2 text-xs">
                          {inspectionResult.template.content_schema?.sections?.map((sec) => (
                            <div key={sec.id} className="flex items-center justify-between py-1 border-b border-gray-100 last:border-0">
                              <span className="font-mono text-gray-800">{sec.id}</span>
                              <div className="flex items-center gap-2">
                                <span className="rounded bg-blue-50 px-2 py-0.5 text-[10px] font-semibold text-blue-700">
                                  {sec.type}
                                </span>
                                {sec.fields && sec.fields.length > 0 && (
                                  <span className="text-[10px] text-gray-400">
                                    [{sec.fields.join(", ")}]
                                  </span>
                                )}
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              )}

              {/* TAB 4: List Registered Templates */}
              {modalTab === "list" && (
                <div className="space-y-3">
                  <p className="text-xs text-gray-500">
                    All templates currently registered and available for generation:
                  </p>
                  <div className="space-y-2">
                    {templates.map((tpl) => (
                      <div
                        key={tpl.template_id}
                        className="flex items-center justify-between rounded-xl border border-gray-200 bg-white p-3.5 hover:border-gray-300"
                      >
                        <div>
                          <div className="flex items-center gap-2">
                            <h5 className="font-bold text-xs text-gray-900">{tpl.title || tpl.template_id}</h5>
                            <span className="rounded bg-gray-100 px-2 py-0.5 text-[10px] font-semibold text-gray-600 uppercase">
                              {tpl.document_type}
                            </span>
                            <span className="text-[10px] text-gray-400">v{tpl.version}</span>
                          </div>
                          <p className="text-[11px] text-gray-500 mt-0.5">{tpl.description || "No description."}</p>
                          <div className="text-[10px] text-gray-400 mt-1">
                            {tpl.content_schema?.sections?.length || 0} sections • {tpl.content_schema?.sections?.filter(s => s.type === "table")?.length || 0} dynamic tables
                          </div>
                        </div>

                        <div className="flex items-center gap-2">
                          <button
                            type="button"
                            onClick={() => {
                              setSelectedTemplateId(tpl.template_id);
                              setShowTemplateModal(false);
                            }}
                            className="rounded-lg bg-navy px-3 py-1.5 text-xs font-semibold text-white hover:bg-navy-dark"
                          >
                            Use in Studio
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="flex items-center justify-between border-t border-gray-200 px-6 py-4 bg-gray-50">
              <span className="text-[11px] text-gray-400">
                Word 2016+ (.docx) OpenXML compliant
              </span>

              <div className="flex items-center gap-3">
                <button
                  type="button"
                  onClick={() => {
                    setShowTemplateModal(false);
                    setUploadFile(null);
                    setInspectionResult(null);
                  }}
                  className="rounded-lg border border-gray-300 bg-white px-4 py-2 text-xs font-semibold text-gray-700 hover:bg-gray-100"
                >
                  Close
                </button>

                {modalTab === "upload" && (
                  <button
                    type="button"
                    disabled={!inspectionResult || uploadSaving}
                    onClick={handleSaveUploadedTemplate}
                    className="rounded-lg bg-navy px-4 py-2 text-xs font-bold text-white shadow-xs hover:bg-navy-dark disabled:opacity-50"
                  >
                    {uploadSaving ? "Registering Template..." : "Upload & Register Template"}
                  </button>
                )}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
