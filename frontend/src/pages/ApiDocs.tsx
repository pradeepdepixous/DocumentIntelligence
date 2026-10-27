import { useState } from "react";
import AgentsGuide from "../components/AgentsGuide";

export default function ApiDocs() {
  const [tab, setTab] = useState<"guide" | "reference">("guide");

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold text-gray-900">API Documentation</h2>
          <p className="text-sm text-gray-500">How to integrate with the API, plus the full endpoint reference.</p>
        </div>
        <div className="flex gap-1 rounded-md border border-gray-200 bg-white p-1">
          <button
            onClick={() => setTab("guide")}
            className={`rounded px-3 py-1.5 text-sm font-medium ${
              tab === "guide" ? "bg-gray-900 text-white" : "text-gray-600 hover:bg-gray-100"
            }`}
          >
            Guide
          </button>
          <button
            onClick={() => setTab("reference")}
            className={`rounded px-3 py-1.5 text-sm font-medium ${
              tab === "reference" ? "bg-gray-900 text-white" : "text-gray-600 hover:bg-gray-100"
            }`}
          >
            Full Reference
          </button>
        </div>
      </div>

      {tab === "guide" && <AgentsGuide />}

      {tab === "reference" && (
        <>
          <div className="flex justify-end">
            <a
              href="/swagger"
              target="_blank"
              rel="noreferrer"
              className="rounded-md border border-gray-300 px-3 py-2 text-sm text-gray-700 hover:bg-gray-100"
            >
              Open in new tab
            </a>
          </div>
          <div className="overflow-hidden rounded-xl border border-gray-200 bg-white">
            <iframe src="/swagger" title="API Documentation" className="h-[80vh] w-full" />
          </div>
        </>
      )}
    </div>
  );
}
