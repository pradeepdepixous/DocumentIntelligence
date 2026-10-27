import { useState } from "react";

export default function CodeBlock({ code, language }: { code: string; language?: string }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }

  return (
    <div className="relative">
      {language && (
        <span className="absolute left-3 top-2 text-[10px] uppercase tracking-wide text-gray-500">{language}</span>
      )}
      <button
        onClick={handleCopy}
        className="absolute right-2 top-2 rounded-md border border-gray-700 bg-gray-800 px-2 py-1 text-[11px] text-gray-200 hover:bg-gray-700"
      >
        {copied ? "Copied" : "Copy"}
      </button>
      <pre className={`overflow-x-auto rounded-lg bg-gray-900 p-4 text-xs text-gray-100 ${language ? "pt-7" : ""}`}>
        <code>{code}</code>
      </pre>
    </div>
  );
}
