import { useState, type DragEvent } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import type { DocumentProcessingResult } from "../lib/documentTypes";
import DocumentHeader from "../components/DocumentHeader";
import DocumentOverview from "../components/DocumentOverview";
import ReviewQueueList from "../components/ReviewQueueList";
import DocumentWorkspace from "../components/DocumentWorkspace";
import AdvancedApiConsole from "../components/AdvancedApiConsole";

const EXTRACTION_CATEGORIES = [
  { icon: "📅", label: "Dates & Deadlines", description: "Effective dates, milestones, expirations" },
  { icon: "👤", label: "Parties & Contacts", description: "Names, companies, emails, addresses" },
  { icon: "💰", label: "Financial Terms", description: "Amounts, fees, payment schedules" },
  { icon: "📊", label: "Tables", description: "Line items, schedules, pricing grids" },
  { icon: "📋", label: "Clauses & Terms", description: "Governing law, liability, renewal terms" },
  { icon: "🔍", label: "Anything else notable", description: "Discovered automatically, even if unexpected" },
];

type Status = "idle" | "analyzing" | "done" | "error";

export default function Playground() {
  const [file, setFile] = useState<File | null>(null);
  const [instructions, setInstructions] = useState("");
  const [status, setStatus] = useState<Status>("idle");
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<DocumentProcessingResult | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [showAdvanced, setShowAdvanced] = useState(false);

  function pickFile(f: File | null) {
    if (f && f.type !== "application/pdf") {
      setError("Please choose a PDF file.");
      return;
    }
    setFile(f);
    setError(null);
  }

  function handleDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragging(false);
    pickFile(e.dataTransfer.files?.[0] ?? null);
  }

  async function handleAnalyze() {
    if (!file) return;
    setStatus("analyzing");
    setError(null);
    setResult(null);

    try {
      const form = new FormData();
      form.append("File", file);
      if (instructions.trim()) form.append("Instructions", instructions);
      const data = await apiFetch<DocumentProcessingResult>("/documents/process", { method: "POST", body: form });
      setResult(data);
      setStatus("done");
    } catch (err) {
      if (err instanceof ApiError && err.status === 429) {
        setError("You've used up your available credit for this billing cycle. Contact an admin to raise your limit.");
      } else if (err instanceof ApiError && err.status === 400) {
        setError(err.message || "We couldn't read this file. Please make sure it's a valid PDF.");
      } else {
        setError(err instanceof ApiError ? err.message : "Something went wrong while analyzing your document. Please try again.");
      }
      setStatus("error");
    }
  }

  function startOver() {
    setFile(null);
    setInstructions("");
    setStatus("idle");
    setError(null);
    setResult(null);
  }

  return (
    <div className="space-y-6">
      {status !== "done" && (
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-xl font-semibold text-navy">Analyze a Document</h2>
            <p className="text-sm text-gray-500">Upload a PDF and we'll pull out the key information — no setup needed.</p>
          </div>
          <button onClick={() => setShowAdvanced((v) => !v)} className="shrink-0 text-xs text-gray-400 hover:text-gray-600 hover:underline">
            {showAdvanced ? "Hide advanced API console" : "Advanced: raw API console"}
          </button>
        </div>
      )}

      {showAdvanced && <AdvancedApiConsole />}

      {!showAdvanced && status !== "done" && (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
          <div className="space-y-4 rounded-xl border border-border-subtle bg-white p-6 lg:col-span-2">
            <div
              onDragOver={(e) => {
                e.preventDefault();
                setIsDragging(true);
              }}
              onDragLeave={() => setIsDragging(false)}
              onDrop={handleDrop}
              onClick={() => document.getElementById("pdf-upload-input")?.click()}
              className={`flex cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed p-10 text-center transition ${
                isDragging ? "border-navy bg-app-bg" : "border-border-subtle hover:border-gray-300"
              }`}
            >
              <input
                id="pdf-upload-input"
                type="file"
                accept="application/pdf"
                className="hidden"
                onChange={(e) => pickFile(e.target.files?.[0] ?? null)}
              />
              {file ? (
                <>
                  <p className="text-3xl">📄</p>
                  <p className="mt-2 text-sm font-medium text-gray-900">{file.name}</p>
                  <p className="mt-1 text-xs text-gray-500">{(file.size / 1024 / 1024).toFixed(1)} MB — click to choose a different file</p>
                </>
              ) : (
                <>
                  <p className="text-3xl">⬆️</p>
                  <p className="mt-2 text-sm font-medium text-gray-900">Drop your PDF here, or click to browse</p>
                  <p className="mt-1 text-xs text-gray-500">PDF files only, any length</p>
                </>
              )}
            </div>

            <div>
              <label className="block text-xs text-gray-500">What would you like to find? (optional)</label>
              <textarea
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
                rows={2}
                placeholder="e.g. Extract the contract dates, parties, and payment terms"
                className="mt-1 w-full rounded-md border border-border-subtle px-3 py-2 text-sm focus:border-navy focus:outline-none"
              />
              <p className="mt-1 text-xs text-gray-400">Leave this blank and we'll pull out everything important automatically.</p>
            </div>

            {error && <p className="text-sm text-red-600">{error}</p>}

            <button
              onClick={handleAnalyze}
              disabled={!file || status === "analyzing"}
              className="w-full rounded-lg bg-navy px-4 py-3 text-sm font-medium text-white transition hover:bg-navy-dark disabled:opacity-50"
            >
              {status === "analyzing" ? (
                <span className="flex items-center justify-center gap-2">
                  <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white/40 border-t-white" />
                  Analyzing your document — this can take a minute...
                </span>
              ) : (
                "Analyze Document"
              )}
            </button>
          </div>

          <div className="rounded-xl border border-border-subtle bg-white p-5">
            <h3 className="text-sm font-semibold text-navy">What we'll pull out</h3>
            <div className="mt-3 space-y-3">
              {EXTRACTION_CATEGORIES.map((c) => (
                <div key={c.label} className="flex items-start gap-2">
                  <span className="text-lg leading-none">{c.icon}</span>
                  <div>
                    <p className="text-sm font-medium text-gray-900">{c.label}</p>
                    <p className="text-xs text-gray-500">{c.description}</p>
                  </div>
                </div>
              ))}
            </div>
            <p className="mt-4 border-t border-border-subtle pt-3 text-xs text-gray-400">
              Every value comes with its source page and an evidence quote, so you can verify anything before relying on it.
            </p>
          </div>
        </div>
      )}

      {!showAdvanced && status === "done" && result && (
        <div className="space-y-5">
          <DocumentHeader result={result} onStartOver={startOver} />
          <DocumentOverview result={result} />
          <ReviewQueueList tasks={result.verificationReport?.review_tasks ?? []} />
          <DocumentWorkspace result={result} />
        </div>
      )}
    </div>
  );
}
