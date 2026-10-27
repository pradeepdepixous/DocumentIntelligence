import type { DocumentProcessingResult } from "../lib/documentTypes";
import { downloadFile } from "../lib/apiClient";
import { useState } from "react";

export default function DocumentHeader({
  result,
  onStartOver,
}: {
  result: DocumentProcessingResult;
  onStartOver: () => void;
}) {
  const [downloading, setDownloading] = useState<"csv" | "markdown" | null>(null);
  const docType = result.plan?.target_document_type || "Document";
  const pageCount = result.manifest?.total_pages ?? result.stitchedEnvelope?.total_pages ?? 0;

  async function handleDownload(kind: "csv" | "markdown") {
    setDownloading(kind);
    try {
      await downloadFile(`/documents/jobs/${result.jobId}/${kind}`, `${result.jobId}.${kind === "csv" ? "csv" : "md"}`);
    } finally {
      setDownloading(null);
    }
  }

  return (
    <div className="rounded-xl border border-border-subtle bg-white px-5 py-4">
      <p className="text-xs text-gray-400">
        Workspace <span className="mx-1 text-gray-300">/</span> Documents <span className="mx-1 text-gray-300">/</span>{" "}
        <span className="text-gray-600">{docType}</span>
      </p>
      <div className="mt-2 flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-center gap-3">
          <span className="flex items-center gap-1.5 rounded-full bg-green-50 px-2.5 py-1 text-xs font-medium text-green-700">
            <span className="h-1.5 w-1.5 rounded-full bg-green-500" />
            Completed
          </span>
          <div>
            <h1 className="text-xl font-semibold leading-tight text-navy">{docType}</h1>
            <p className="text-xs text-gray-500">
              PDF &middot; {pageCount} pages &middot; Job {result.jobId}
            </p>
          </div>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => handleDownload("csv")}
            disabled={downloading !== null}
            className="rounded-md border border-border-subtle px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {downloading === "csv" ? "Downloading..." : "Export CSV"}
          </button>
          <button
            onClick={() => handleDownload("markdown")}
            disabled={downloading !== null}
            className="rounded-md border border-border-subtle px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {downloading === "markdown" ? "Downloading..." : "Export Markdown"}
          </button>
          <button
            onClick={onStartOver}
            className="rounded-md bg-navy px-3 py-1.5 text-xs font-medium text-white hover:bg-navy-dark"
          >
            New document
          </button>
        </div>
      </div>
    </div>
  );
}
