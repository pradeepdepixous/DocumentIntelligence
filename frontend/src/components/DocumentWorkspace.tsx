import { useMemo, useState } from "react";
import type { DocumentProcessingResult, ExtractedItem, RuleDefinition, RuleValidationResponse } from "../lib/documentTypes";
import { confidenceTone, displayValue } from "../lib/documentTypes";
import { AskPanel, SummarizePanel } from "./AgentsPanel";
import ValidationRulesTable from "./ValidationRulesTable";
import ValidationRunPanel from "./ValidationRunPanel";
import { apiFetch, ApiError } from "../lib/apiClient";

type Tab = "transcript" | "ask" | "summarize" | "validate";

const TABS: Array<{ key: Tab; label: string }> = [
  { key: "transcript", label: "Transcript" },
  { key: "ask", label: "Ask AI" },
  { key: "summarize", label: "Summarize" },
  { key: "validate", label: "Validate" },
];

const TONE_CLASSES: Record<string, string> = {
  good: "bg-green-100 text-green-700",
  ok: "bg-amber-100 text-amber-700",
  low: "bg-red-100 text-red-700",
};

function ConfidenceBadge({ value }: { value: number }) {
  const tone = confidenceTone(value);
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${TONE_CLASSES[tone]}`}>{Math.round(value * 100)}%</span>;
}

function FieldRow({ item }: { item: ExtractedItem }) {
  const [expanded, setExpanded] = useState(false);
  const value = displayValue(item.value);
  const normalized = item.normalized_value !== undefined && item.normalized_value !== null ? displayValue(item.normalized_value) : null;
  const needsReview = item.validation_status !== "evidence_confirmed";

  return (
    <div className="border-t border-border-subtle py-3">
      <button onClick={() => setExpanded((e) => !e)} className="flex w-full items-start justify-between gap-4 text-left">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <p className="text-xs font-medium text-gray-500">{item.field_label || item.field_key}</p>
            <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-gray-400">{item.value_type}</span>
          </div>
          <p className="mt-0.5 break-words text-sm text-gray-900">{value || "—"}</p>
          {normalized && normalized !== value && <p className="mt-0.5 text-xs text-gray-400">Normalized: {normalized}</p>}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {needsReview && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-700">Needs review</span>}
          {item.is_dynamically_discovered && <span className="rounded-full bg-blue-100 px-2 py-0.5 text-xs text-blue-700">Discovered</span>}
          <ConfidenceBadge value={item.confidence?.value ?? 0} />
          <span className="text-gray-300">{expanded ? "▲" : "▼"}</span>
        </div>
      </button>
      {expanded && (
        <div className="mt-2 space-y-1 rounded-md bg-app-bg p-3 text-xs text-gray-600">
          <p className="text-gray-400">
            Key: <span className="font-mono">{item.field_key}</span> &middot; Validation: {item.validation_status}
          </p>
          {item.sources?.length ? (
            item.sources.map((s, i) => (
              <p key={i}>
                <span className="font-medium">Page {s.page_number}:</span> "{s.source_text}"
              </p>
            ))
          ) : (
            <p className="text-gray-400">No source evidence recorded.</p>
          )}
        </div>
      )}
    </div>
  );
}

function TableView({ table }: { table: DocumentProcessingResult["stitchedEnvelope"]["tables"][number] }) {
  return (
    <div className="rounded-xl border border-border-subtle bg-white p-5">
      <h4 className="text-sm font-semibold text-navy">{table.table_title || table.table_key}</h4>
      <p className="text-xs text-gray-400">Page {table.page_number}</p>
      {table.rows.length === 0 ? (
        <p className="mt-3 text-sm text-amber-700">
          This table's headers were found, but no row data came back — likely a failed extraction run. Try analyzing the
          document again.
        </p>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-gray-500">
              <tr>
                {table.headers.map((h) => (
                  <th key={h} className="whitespace-nowrap py-2 pr-4">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((row, i) => (
                <tr key={i} className="border-t border-border-subtle">
                  {table.headers.map((h) => (
                    <td key={h} className="py-2 pr-4">
                      {displayValue(row[h]?.value)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function PageStatusBadge({ hasContent, isScanned }: { hasContent: boolean; isScanned: boolean }) {
  if (hasContent) {
    return <span className="rounded-full bg-green-100 px-2 py-0.5 text-[11px] font-medium text-green-700">Content found</span>;
  }
  if (isScanned) {
    return <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-700">Scanned — no text layer</span>;
  }
  return <span className="rounded-full bg-red-100 px-2 py-0.5 text-[11px] font-medium text-red-700">No content found</span>;
}

function TranscriptTab({ result }: { result: DocumentProcessingResult }) {
  const [search, setSearch] = useState("");
  const [expandedPage, setExpandedPage] = useState<number | null>(null);

  const pages = result.stitchedEnvelope?.pages ?? [];
  const fullMarkdown = result.stitchedEnvelope?.full_markdown ?? "";
  const coverage = result.verificationReport?.page_coverage ?? [];
  const coverageByPage = useMemo(() => new Map(coverage.map((c) => [c.page_number, c])), [coverage]);

  const allItems = result.stitchedEnvelope?.items ?? [];
  const tables = result.stitchedEnvelope?.tables ?? [];

  const filteredPages = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return pages;
    return pages.filter((p) => p.markdown?.toLowerCase().includes(q) || String(p.page_number).includes(q));
  }, [pages, search]);

  if (pages.length === 0 && allItems.length === 0 && tables.length === 0 && !fullMarkdown) {
    return <p className="py-6 text-center text-sm text-gray-400">No content was extracted from this document.</p>;
  }

  return (
    <div className="space-y-5">
      {pages.length > 0 && (
        <div>
          <div className="flex items-center justify-between gap-3">
            <h3 className="text-sm font-semibold text-navy">Extracted content</h3>
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search pages..."
              className="w-48 rounded-md border border-border-subtle px-3 py-1.5 text-xs focus:border-navy focus:outline-none"
            />
          </div>
          <div className="mt-3 divide-y divide-border-subtle rounded-xl border border-border-subtle bg-white">
            {filteredPages.length === 0 && <p className="p-4 text-sm text-gray-400">No pages match your search.</p>}
            {filteredPages.map((p) => {
              const isOpen = expandedPage === p.page_number;
              const hasContent = Boolean(p.markdown?.trim());
              const isScanned = coverageByPage.get(p.page_number)?.is_scanned ?? false;
              return (
                <div key={p.page_number}>
                  <button
                    onClick={() => setExpandedPage(isOpen ? null : p.page_number)}
                    className="flex w-full items-center justify-between gap-3 px-4 py-3 text-left hover:bg-app-bg"
                  >
                    <span className="text-sm font-medium text-gray-900">Page {p.page_number}</span>
                    <span className="flex items-center gap-2">
                      <span className="text-xs text-gray-400">{hasContent ? `${p.word_count} words extracted` : "0 words"}</span>
                      {p.has_tables && <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] text-gray-500">table</span>}
                      <PageStatusBadge hasContent={hasContent} isScanned={isScanned} />
                      <span className="text-gray-300">{isOpen ? "▲" : "▼"}</span>
                    </span>
                  </button>
                  {isOpen && (
                    <div className="border-t border-border-subtle bg-app-bg px-4 py-3">
                      <pre className="max-h-80 overflow-auto whitespace-pre-wrap text-xs text-gray-700">
                        {hasContent ? p.markdown : "No markdown was transcribed for this page."}
                      </pre>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {pages.length === 0 && fullMarkdown && (
        <div>
          <h3 className="text-sm font-semibold text-navy">Document transcript</h3>
          <pre className="mt-3 max-h-96 overflow-auto whitespace-pre-wrap rounded-xl border border-border-subtle bg-white p-4 text-xs text-gray-700">
            {fullMarkdown}
          </pre>
        </div>
      )}

      {allItems.length > 0 && (
        <div className="rounded-xl border border-border-subtle bg-white p-5">
          <h3 className="text-sm font-semibold text-navy">Extracted fields</h3>
          <div>
            {allItems.map((item, i) => (
              <FieldRow key={`${item.field_key}-${i}`} item={item} />
            ))}
          </div>
        </div>
      )}

      {tables.map((t) => (
        <TableView key={t.table_key} table={t} />
      ))}
    </div>
  );
}

function emptyRule(n: number): RuleDefinition {
  return { rule_id: `RULE-${n}`, category: "General", name: "", rule_description: "", is_knockout: false, max_marks: 10 };
}

export default function DocumentWorkspace({ result }: { result: DocumentProcessingResult }) {
  const [tab, setTab] = useState<Tab>("transcript");

  const [rules, setRules] = useState<RuleDefinition[]>([emptyRule(1), { ...emptyRule(2), max_marks: 15 }]);
  const [passingThreshold, setPassingThreshold] = useState(70);
  const [submitting, setSubmitting] = useState(false);
  const [validationResult, setValidationResult] = useState<RuleValidationResponse | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  async function handleRunValidation() {
    const validRules = rules.filter((r) => r.rule_description.trim() && r.max_marks > 0);
    if (validRules.length === 0) {
      setValidationError("Add at least one rule with a description and a weight greater than 0.");
      return;
    }
    setSubmitting(true);
    setValidationResult(null);
    setValidationError(null);
    try {
      const data = await apiFetch<RuleValidationResponse>(`/agent/jobs/${result.jobId}/validate-rules`, {
        method: "POST",
        body: JSON.stringify({ rules: validRules, passing_threshold: passingThreshold }),
      });
      setValidationResult(data);
    } catch (err) {
      setValidationError(err instanceof ApiError ? err.message : "Couldn't validate rules right now.");
    } finally {
      setSubmitting(false);
    }
  }

  const isValidate = tab === "validate";

  return (
    <div className={`grid grid-cols-1 gap-5 ${isValidate ? "lg:grid-cols-3" : ""}`}>
      <div className={isValidate ? "lg:col-span-2" : ""}>
        <div className="rounded-xl border border-border-subtle bg-white">
          <div className="border-b border-border-subtle px-5 pt-4">
            <h2 className="text-sm font-semibold text-navy">Document workspace</h2>
          </div>
          <div className="flex gap-1 px-5 pt-3">
            {TABS.map((t) => (
              <button
                key={t.key}
                onClick={() => setTab(t.key)}
                className={`rounded-t-md px-3 py-2 text-sm font-medium transition ${
                  tab === t.key ? "border-b-2 border-navy text-navy" : "text-gray-500 hover:text-gray-900"
                }`}
              >
                {t.label}
              </button>
            ))}
          </div>
          <div className="p-5">
            {tab === "transcript" && <TranscriptTab result={result} />}
            {tab === "ask" && <AskPanel jobId={result.jobId} />}
            {tab === "summarize" && <SummarizePanel jobId={result.jobId} />}
            {tab === "validate" && <ValidationRulesTable rules={rules} onChange={setRules} />}
          </div>
        </div>
      </div>

      {isValidate && (
        <div className="lg:col-span-1">
          <ValidationRunPanel
            passingThreshold={passingThreshold}
            onThresholdChange={setPassingThreshold}
            onRun={handleRunValidation}
            submitting={submitting}
            result={validationResult}
            error={validationError}
          />
        </div>
      )}
    </div>
  );
}
