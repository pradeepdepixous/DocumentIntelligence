import { useMemo, useState } from "react";
import { ENDPOINTS, extractPathParams, buildPath } from "../lib/endpoints";
import { rawApiRequest, type RawApiResult } from "../lib/apiClient";

/**
 * Raw HTTP endpoint console — method/path picker, JSON body editor, raw response. This is a
 * developer tool, not something a first-time user should land on; the default Playground
 * experience is DocumentWorkspace's upload-and-see-results flow instead. Kept for anyone who
 * specifically wants to poke at the API shape directly.
 */
export default function AdvancedApiConsole() {
  const [endpointId, setEndpointId] = useState(ENDPOINTS[0].id);
  const [pathParams, setPathParams] = useState<Record<string, string>>({});
  const [jsonBody, setJsonBody] = useState(ENDPOINTS[0].defaultBody ?? "");
  const [file, setFile] = useState<File | null>(null);
  const [instructions, setInstructions] = useState("");
  const [sending, setSending] = useState(false);
  const [result, setResult] = useState<RawApiResult | null>(null);
  const [sendError, setSendError] = useState<string | null>(null);

  const endpoint = useMemo(() => ENDPOINTS.find((e) => e.id === endpointId)!, [endpointId]);
  const params = useMemo(() => extractPathParams(endpoint.path), [endpoint]);
  const creditsRemaining = result?.headers["x-quota-remaining"];

  function selectEndpoint(id: string) {
    const next = ENDPOINTS.find((e) => e.id === id)!;
    setEndpointId(id);
    setJsonBody(next.defaultBody ?? "");
    setPathParams({});
    setFile(null);
    setInstructions("");
    setResult(null);
    setSendError(null);
  }

  async function handleSend() {
    setSending(true);
    setSendError(null);
    setResult(null);
    try {
      const path = buildPath(endpoint.path, pathParams);

      if (endpoint.bodyType === "multipart") {
        if (!file) {
          setSendError("Choose a PDF file first.");
          setSending(false);
          return;
        }
        const form = new FormData();
        form.append("File", file);
        if (instructions.trim()) form.append("Instructions", instructions);
        const res = await rawApiRequest(path, endpoint.method, { body: form });
        setResult(res);
        return;
      }

      if (endpoint.bodyType === "json") {
        const res = await rawApiRequest(path, endpoint.method, { body: jsonBody, isJson: true });
        setResult(res);
        return;
      }

      const res = await rawApiRequest(path, endpoint.method);
      setResult(res);
    } catch (err) {
      setSendError(err instanceof Error ? err.message : "Request failed");
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
      <div className="space-y-4 rounded-xl border border-gray-200 bg-white p-5">
        <div>
          <label className="block text-xs text-gray-500">Endpoint</label>
          <select
            value={endpointId}
            onChange={(e) => selectEndpoint(e.target.value)}
            className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-gray-900 focus:outline-none"
          >
            {ENDPOINTS.map((e) => (
              <option key={e.id} value={e.id}>
                {e.method} {e.path} — {e.description}
              </option>
            ))}
          </select>
          {endpoint.roles.length > 0 && (
            <p className="mt-1 text-xs text-gray-400">Requires: {endpoint.roles.join(" or ")} (your account)</p>
          )}
        </div>

        {params.map((p) => (
          <div key={p}>
            <label className="block text-xs text-gray-500">{p}</label>
            <input
              value={pathParams[p] ?? ""}
              onChange={(e) => setPathParams((prev) => ({ ...prev, [p]: e.target.value }))}
              placeholder={`Enter ${p}`}
              className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm font-mono focus:border-gray-900 focus:outline-none"
            />
          </div>
        ))}

        {endpoint.bodyType === "json" && (
          <div>
            <label className="block text-xs text-gray-500">Body (JSON)</label>
            <textarea
              value={jsonBody}
              onChange={(e) => setJsonBody(e.target.value)}
              rows={8}
              className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 font-mono text-sm focus:border-gray-900 focus:outline-none"
            />
          </div>
        )}

        {endpoint.bodyType === "multipart" && (
          <>
            <div>
              <label className="block text-xs text-gray-500">PDF File</label>
              <input
                type="file"
                accept="application/pdf"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="mt-1 w-full text-sm"
              />
            </div>
            <div>
              <label className="block text-xs text-gray-500">Instructions (optional)</label>
              <textarea
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
                rows={3}
                placeholder="Extract all critical business entities, dates, parties..."
                className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-gray-900 focus:outline-none"
              />
            </div>
          </>
        )}

        <button
          onClick={handleSend}
          disabled={sending}
          className="w-full rounded-md bg-gray-900 px-4 py-2 text-sm font-medium text-white hover:bg-gray-800 disabled:opacity-50"
        >
          {sending ? "Sending..." : "Send"}
        </button>
        {sendError && <p className="text-sm text-red-600">{sendError}</p>}
      </div>

      <div className="rounded-xl border border-gray-200 bg-white p-5">
        <h3 className="text-sm font-semibold text-gray-900">Response</h3>
        {!result && <p className="mt-3 text-sm text-gray-400">Send a request to see the response here.</p>}
        {result && (
          <div className="mt-3 space-y-3">
            <div className="flex items-center gap-3 text-sm">
              <span
                className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                  result.ok ? "bg-green-100 text-green-700" : "bg-red-100 text-red-700"
                }`}
              >
                {result.status} {result.statusText}
              </span>
              <span className="text-gray-400">{result.durationMs}ms</span>
              {creditsRemaining && <span className="text-gray-400">Credits remaining: {creditsRemaining}</span>}
            </div>
            <pre className="max-h-96 overflow-auto rounded-md bg-gray-900 p-3 text-xs text-gray-100">
              {result.bodyIsText ? String(result.body) : JSON.stringify(result.body, null, 2)}
            </pre>
          </div>
        )}
      </div>
    </div>
  );
}
