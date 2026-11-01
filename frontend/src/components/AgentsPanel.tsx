import { useState } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import type { AskResponse, SummarizeResponse } from "../lib/documentTypes";
import { MarkdownView } from "./MarkdownView";

export function AskPanel({ jobId }: { jobId: string }) {
  const [question, setQuestion] = useState("");
  const [asking, setAsking] = useState(false);
  const [answer, setAnswer] = useState<AskResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleAsk() {
    if (!question.trim()) return;
    setAsking(true);
    setAnswer(null);
    setError(null);
    try {
      const data = await apiFetch<AskResponse>(`/agent/jobs/${jobId}/ask`, {
        method: "POST",
        body: JSON.stringify({ Question: question }),
      });
      setAnswer(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't get an answer right now.");
    } finally {
      setAsking(false);
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <input
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && handleAsk()}
          placeholder="e.g. What is the effective date?"
          className="flex-1 rounded-md border border-border-subtle px-3 py-2 text-sm focus:border-navy focus:outline-none"
        />
        <button
          onClick={handleAsk}
          disabled={asking || !question.trim()}
          className="rounded-md bg-navy px-4 py-2 text-sm font-medium text-white hover:bg-navy-dark disabled:opacity-50"
        >
          {asking ? "Asking..." : "Ask"}
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {answer && (
        <div className="space-y-3 rounded-lg border border-border-subtle bg-app-bg p-4 text-sm">
          {!answer.isFoundInDocument && (
            <p className="text-xs font-medium text-amber-700">Not confidently found in the document — treat with caution.</p>
          )}
          <MarkdownView content={answer.answer} />
          {answer.citations?.length > 0 && (
            <div className="space-y-1 border-t border-border-subtle pt-2">
              {answer.citations.map((c, i) => (
                <p key={i} className="text-xs text-gray-500">
                  <span className="font-medium text-gray-700">{c.fieldName}</span> &middot; page {c.pageNumber} &middot;{" "}
                  {Math.round(c.confidence * 100)}% confidence — "{c.evidence}"
                </p>
              ))}
            </div>
          )}
          {answer.warnings?.length > 0 && (
            <div className="border-t border-border-subtle pt-2">
              {answer.warnings.map((w, i) => (
                <p key={i} className="text-xs text-amber-700">
                  {w}
                </p>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

export function SummarizePanel({ jobId }: { jobId: string }) {
  const [focusArea, setFocusArea] = useState("");
  const [summarizing, setSummarizing] = useState(false);
  const [summary, setSummary] = useState<SummarizeResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleSummarize() {
    setSummarizing(true);
    setSummary(null);
    setError(null);
    try {
      const body: Record<string, string> = {};
      if (focusArea.trim()) body.FocusArea = focusArea.trim();
      const data = await apiFetch<SummarizeResponse>(`/agent/jobs/${jobId}/summarize`, {
        method: "POST",
        body: JSON.stringify(body),
      });
      setSummary(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't generate a summary right now.");
    } finally {
      setSummarizing(false);
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <input
          value={focusArea}
          onChange={(e) => setFocusArea(e.target.value)}
          placeholder="Optional focus area, e.g. payment terms"
          className="flex-1 rounded-md border border-border-subtle px-3 py-2 text-sm focus:border-navy focus:outline-none"
        />
        <button
          onClick={handleSummarize}
          disabled={summarizing}
          className="rounded-md bg-navy px-4 py-2 text-sm font-medium text-white hover:bg-navy-dark disabled:opacity-50"
        >
          {summarizing ? "Summarizing..." : summary ? "Regenerate" : "Generate summary"}
        </button>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {summary && (
        <div className="rounded-lg border border-border-subtle bg-app-bg p-4 text-sm text-gray-700">
          <MarkdownView content={summary.summary} />
        </div>
      )}
    </div>
  );
}
