import type { ReviewTask } from "../lib/documentTypes";
import { displayValue } from "../lib/documentTypes";

const REASON_LABELS: Record<string, string> = {
  value_unsupported: "Value not found in cited evidence",
  unverified_evidence: "Citation not found on the page",
  partially_verified: "Only some citations verified",
  low_confidence: "Below confidence threshold",
  unresolved_conflict: "Conflicting values across pages",
  extraction_empty: "Extraction returned nothing — likely a failed run, try again",
};

export default function ReviewQueueList({ tasks }: { tasks: ReviewTask[] }) {
  if (tasks.length === 0) return null;

  return (
    <div className="rounded-xl border border-amber-200 bg-white p-5">
      <h3 className="text-sm font-semibold text-gray-900">Needs Review ({tasks.length})</h3>
      <p className="text-xs text-gray-500">These items couldn't be fully auto-verified — worth a quick human check.</p>
      <div className="mt-3 space-y-3">
        {tasks.map((task, i) => (
          <div key={task.task_id ?? i} className="rounded-lg border border-amber-100 bg-amber-50 p-3">
            <div className="flex items-center justify-between">
              <p className="text-sm font-medium text-gray-900">{task.field_key}</p>
              <span className="text-xs text-gray-500">Page {task.page_number}</span>
            </div>
            <p className="mt-1 text-xs font-medium text-amber-700">{REASON_LABELS[task.reason] ?? task.reason}</p>
            {task.reported_value !== undefined && (
              <p className="mt-1 text-xs text-gray-600">Reported value: {displayValue(task.reported_value)}</p>
            )}
            {task.reviewer_notes && <p className="mt-1 text-xs text-gray-500">{task.reviewer_notes}</p>}
          </div>
        ))}
      </div>
    </div>
  );
}
