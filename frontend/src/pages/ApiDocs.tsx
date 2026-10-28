import { useState } from "react";
import AgentsGuide from "../components/AgentsGuide";
import DocumentGenerationGuide from "../components/DocumentGenerationGuide";

const TABS = [
  { id: "guide", label: "Agent Guide" },
  { id: "generation", label: "Document Generation" },
] as const;

type TabId = (typeof TABS)[number]["id"];

export default function ApiDocs() {
  const [tab, setTab] = useState<TabId>("guide");

  return (
    <div className="space-y-5">
      <div className="inline-flex gap-1 rounded-xl border border-border-subtle bg-white p-1 shadow-sm">
        {TABS.map((t) => (
          <button
            key={t.id}
            onClick={() => setTab(t.id)}
            className={`relative rounded-lg px-4 py-1.5 text-sm font-medium transition-colors ${
              tab === t.id ? "bg-navy text-white shadow-sm" : "text-gray-500 hover:bg-gray-100 hover:text-gray-800"
            }`}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === "guide" && <AgentsGuide />}

      {tab === "generation" && <DocumentGenerationGuide />}
    </div>
  );
}
