import { useState } from "react";
import type { DocumentProcessingResult } from "../lib/documentTypes";

function MetricCard({ label, value, sublabel }: { label: string; value: string | number; sublabel?: string }) {
  return (
    <div className="rounded-lg border border-border-subtle bg-white p-4">
      <p className="text-xs text-gray-500">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-navy">{value}</p>
      {sublabel && <p className="mt-0.5 text-[11px] text-gray-400">{sublabel}</p>}
    </div>
  );
}

export default function DocumentOverview({ result }: { result: DocumentProcessingResult }) {
  const [expanded, setExpanded] = useState(true);

  const pages = result.stitchedEnvelope?.pages ?? [];
  const totalPages = result.manifest?.total_pages ?? result.stitchedEnvelope?.total_pages ?? 0;
  const pagesWithContent = pages.filter((p) => p.markdown?.trim()).length;
  const totalWords = pages.reduce((sum, p) => sum + (p.word_count ?? 0), 0);
  const seconds = (result.executionDurationMs / 1000).toFixed(1);
  const summary = result.stitchedEnvelope?.document_summary;
  const keywords = result.stitchedEnvelope?.document_keywords ?? [];
  const conflictCount = result.reconciliationLedger?.conflict_count ?? 0;

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <MetricCard label="Pages Processed" value={`${totalPages} / ${totalPages}`} />
        <MetricCard label="Pages with Content" value={`${pagesWithContent} / ${totalPages}`} />
        <MetricCard label="Words Extracted" value={totalWords.toLocaleString()} />
        <MetricCard label="Processing Time" value={`${seconds}s`} />
      </div>

      {conflictCount > 0 && (
        <p className="text-xs text-amber-700">
          {conflictCount} conflicting observation(s) were detected and preserved for review.
        </p>
      )}

      {summary && (
        <div className="rounded-xl border border-border-subtle bg-white">
          <button
            onClick={() => setExpanded((v) => !v)}
            className="flex w-full items-center justify-between px-5 py-3.5 text-left"
          >
            <span className="flex items-center gap-2">
              <span className="text-sm font-semibold text-navy">Document overview</span>
              <span className="rounded-full bg-indigo-50 px-2 py-0.5 text-[10px] font-medium text-indigo-700">AI-generated</span>
            </span>
            <span className="text-gray-400">{expanded ? "▲" : "▼"}</span>
          </button>
          {expanded && (
            <div className="border-t border-border-subtle px-5 py-4">
              <p className="text-sm leading-relaxed text-gray-700">{summary}</p>
              <p className="mt-2 text-xs text-gray-400">Summary generated from extracted document content.</p>
              {keywords.length > 0 && (
                <div className="mt-3">
                  <p className="text-xs font-medium text-gray-500">Key topics</p>
                  <div className="mt-1.5 flex flex-wrap gap-1.5">
                    {keywords.map((k) => (
                      <span key={k} className="rounded-full bg-gray-100 px-2.5 py-0.5 text-xs text-gray-700">
                        {k}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
