import { useState } from "react";

export default function CodeBlock({ code, language }: { code: string; language?: string }) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    await navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }

  return (
    <div className="overflow-hidden rounded-xl border border-gray-800/60 bg-[#0d1117] shadow-sm">
      <div className="flex items-center justify-between border-b border-white/5 px-3.5 py-2">
        <span className="text-[10px] font-medium uppercase tracking-wider text-gray-400">
          {language ?? "text"}
        </span>
        <button
          onClick={handleCopy}
          className="rounded-md px-2 py-1 text-[11px] font-medium text-gray-300 transition-colors hover:bg-white/10"
        >
          {copied ? "✓ Copied" : "Copy"}
        </button>
      </div>
      <pre className="overflow-x-auto p-4 text-[12.5px] leading-relaxed text-gray-100">
        <code>{code}</code>
      </pre>
    </div>
  );
}
