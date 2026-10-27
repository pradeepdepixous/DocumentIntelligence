import type { RuleEvaluationResult, RuleValidationResponse } from "../lib/documentTypes";

const STATUS_STYLES: Record<string, string> = {
  Pass: "bg-green-100 text-green-700",
  Fail: "bg-red-100 text-red-700",
  Partial: "bg-amber-100 text-amber-700",
  NotApplicable: "bg-gray-100 text-gray-600",
};

function VerdictBadge({ verdict }: { verdict: string }) {
  const style =
    verdict === "PASS" ? "bg-green-100 text-green-800" : verdict === "FAIL" ? "bg-red-100 text-red-800" : "bg-amber-100 text-amber-800";
  return <span className={`rounded-full px-3 py-1 text-xs font-semibold ${style}`}>{verdict.replace("_", " ")}</span>;
}

function RuleResultRow({ result }: { result: RuleEvaluationResult }) {
  return (
    <div className="rounded-lg border border-border-subtle bg-white p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-xs font-mono text-gray-400">{result.rule_id}</span>
          <span className={`rounded px-2 py-0.5 text-[11px] font-semibold ${STATUS_STYLES[result.status] ?? "bg-gray-100 text-gray-600"}`}>
            {result.status}
          </span>
          {result.is_knockout && <span className="rounded bg-red-50 px-1.5 py-0.5 text-[10px] font-medium text-red-600">Knockout</span>}
        </div>
        <span className="text-xs font-semibold text-navy">
          {result.marks_awarded}/{result.max_marks}
        </span>
      </div>
      <p className="mt-1.5 text-xs text-gray-900">{result.finding}</p>
      {result.decision_reason && <p className="mt-1 text-[11px] text-gray-500">{result.decision_reason}</p>}
      {result.evidence_quote && (
        <p className="mt-1.5 border-t border-border-subtle pt-1.5 text-[11px] text-gray-500">
          {result.page_citations?.length > 0 && <span className="font-medium text-gray-700">p. {result.page_citations.join(", ")} &middot; </span>}
          "{result.evidence_quote}"
        </p>
      )}
    </div>
  );
}

export default function ValidationRunPanel({
  passingThreshold,
  onThresholdChange,
  onRun,
  submitting,
  result,
  error,
}: {
  passingThreshold: number;
  onThresholdChange: (v: number) => void;
  onRun: () => void;
  submitting: boolean;
  result: RuleValidationResponse | null;
  error: string | null;
}) {
  const passedCount = result?.rule_results.filter((r) => r.status === "Pass").length ?? 0;
  const totalCount = result?.rule_results.length ?? 0;

  return (
    <div className="space-y-4">
      <div className="rounded-xl border border-border-subtle bg-white p-5">
        <h3 className="text-sm font-semibold text-navy">Validation settings</h3>
        <div className="mt-3">
          <label className="block text-xs text-gray-500">Passing threshold</label>
          <div className="mt-1 flex items-center gap-2">
            <input
              type="number"
              min={0}
              max={100}
              value={passingThreshold}
              onChange={(e) => onThresholdChange(Number(e.target.value) || 0)}
              className="w-20 rounded-md border border-border-subtle px-2 py-1.5 text-sm focus:border-navy focus:outline-none"
            />
            <span className="text-sm text-gray-500">%</span>
          </div>
          <p className="mt-1.5 text-xs text-gray-400">Minimum weighted score required to pass.</p>
        </div>

        {error && <p className="mt-3 text-sm text-red-600">{error}</p>}

        <button
          onClick={onRun}
          disabled={submitting}
          className="mt-4 flex w-full items-center justify-center gap-2 rounded-lg bg-navy px-4 py-2.5 text-sm font-medium text-white hover:bg-navy-dark disabled:opacity-50"
        >
          {submitting ? (
            <>
              <span className="h-3.5 w-3.5 animate-spin rounded-full border-2 border-white/40 border-t-white" />
              Validating...
            </>
          ) : (
            "Run validation"
          )}
        </button>
      </div>

      <div className="rounded-xl border border-border-subtle bg-white p-5">
        <h3 className="text-sm font-semibold text-navy">Validation summary</h3>

        {!result && (
          <div className="mt-3 rounded-lg bg-app-bg p-4">
            <p className="text-sm font-medium text-gray-700">Ready to validate</p>
            <p className="mt-1 text-xs text-gray-500">
              Your rule results will appear here after execution. Results include score, rule evidence, and failures.
            </p>
          </div>
        )}

        {result && (
          <div className="mt-3 space-y-3">
            <div className="flex items-center justify-between rounded-lg bg-app-bg p-3">
              <VerdictBadge verdict={result.overall_verdict} />
              <p className="text-sm font-semibold text-navy">
                {Math.round(result.percentage)}% &middot; {passedCount}/{totalCount} passed
              </p>
            </div>
            <p className="text-xs text-gray-600">{result.summary}</p>
            <div className="space-y-2">
              {result.rule_results.map((r) => (
                <RuleResultRow key={r.rule_id} result={r} />
              ))}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
