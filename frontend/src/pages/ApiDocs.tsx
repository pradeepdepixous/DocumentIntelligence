export default function ApiDocs() {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-semibold text-gray-900">API Documentation</h2>
          <p className="text-sm text-gray-500">Full endpoint reference, generated from the API.</p>
        </div>
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
    </div>
  );
}
