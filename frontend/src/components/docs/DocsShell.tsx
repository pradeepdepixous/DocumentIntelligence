import { useEffect, useRef, useState } from "react";
import MethodBadge from "../MethodBadge";

export interface DocsSection {
  id: string;
  label: string;
}

export interface DocsMeta {
  label: string;
  value: string;
}

export default function DocsShell({
  eyebrow,
  title,
  description,
  note,
  meta,
  sections,
  children,
}: {
  eyebrow: string;
  title: string;
  description: string;
  note?: string;
  meta?: DocsMeta[];
  sections: DocsSection[];
  children: React.ReactNode;
}) {
  const [activeId, setActiveId] = useState(sections[0]?.id);
  const observerRef = useRef<IntersectionObserver | null>(null);

  useEffect(() => {
    const elements = sections
      .map((s) => document.getElementById(s.id))
      .filter((el): el is HTMLElement => el !== null);

    observerRef.current?.disconnect();
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting);
        if (visible.length > 0) {
          const top = visible.reduce((a, b) => (a.boundingClientRect.top < b.boundingClientRect.top ? a : b));
          setActiveId(top.target.id);
        }
      },
      { rootMargin: "-15% 0px -70% 0px", threshold: 0 }
    );
    elements.forEach((el) => observer.observe(el));
    observerRef.current = observer;
    return () => observer.disconnect();
  }, [sections]);

  return (
    <div className="space-y-6">
      <div className="relative overflow-hidden rounded-2xl border border-border-subtle bg-gradient-to-br from-navy via-navy to-navy-dark p-8 text-white shadow-sm">
        <div className="pointer-events-none absolute -right-16 -top-16 h-56 w-56 rounded-full bg-white/5 blur-2xl" />
        <div className="pointer-events-none absolute -bottom-20 left-1/3 h-48 w-48 rounded-full bg-white/5 blur-2xl" />
        <div className="relative">
          <span className="inline-flex items-center rounded-full border border-white/15 bg-white/10 px-2.5 py-1 text-[11px] font-medium uppercase tracking-wider text-white/80">
            {eyebrow}
          </span>
          <h2 className="mt-3 text-2xl font-semibold tracking-tight">{title}</h2>
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-white/70">{description}</p>
          {note && (
            <p className="mt-3 inline-flex items-center gap-1.5 rounded-lg bg-white/10 px-3 py-1.5 text-xs text-white/80">
              {note}
            </p>
          )}
          {meta && meta.length > 0 && (
            <div className="mt-5 flex flex-wrap gap-2">
              {meta.map((m) => (
                <div
                  key={m.label}
                  className="flex items-center gap-2 rounded-lg border border-white/10 bg-white/5 px-3 py-1.5"
                >
                  <span className="text-[10px] font-medium uppercase tracking-wide text-white/50">{m.label}</span>
                  <code className="text-xs font-medium text-white/90">{m.value}</code>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[200px_1fr]">
        <nav className="hidden lg:block">
          <div className="sticky top-6 space-y-0.5 border-l border-border-subtle pl-3">
            {sections.map((s) => {
              const isActive = s.id === activeId;
              return (
                <a
                  key={s.id}
                  href={`#${s.id}`}
                  className={`relative block rounded-md py-1.5 pl-3 pr-2 text-sm transition-colors ${
                    isActive ? "font-medium text-navy" : "text-gray-500 hover:text-gray-900"
                  }`}
                >
                  {isActive && (
                    <span className="absolute -left-[13px] top-0 h-full w-0.5 rounded-full bg-navy" />
                  )}
                  {s.label}
                </a>
              );
            })}
          </div>
        </nav>

        <div className="space-y-5">{children}</div>
      </div>
    </div>
  );
}

export function DocsSectionCard({
  id,
  index,
  title,
  children,
}: {
  id: string;
  index?: number;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section
      id={id}
      className="scroll-mt-6 rounded-2xl border border-border-subtle bg-white p-6 shadow-sm transition-shadow hover:shadow-md"
    >
      <div className="flex items-center gap-2.5">
        {index !== undefined && (
          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-navy/5 text-[11px] font-semibold text-navy">
            {index}
          </span>
        )}
        <h3 className="text-base font-semibold text-gray-900">{title}</h3>
      </div>
      <div className="mt-3.5 space-y-3.5 text-sm leading-relaxed text-gray-700">{children}</div>
    </section>
  );
}

export function DocsCallout({
  tone = "info",
  children,
}: {
  tone?: "info" | "warn";
  children: React.ReactNode;
}) {
  const styles =
    tone === "warn"
      ? "border-amber-200 bg-amber-50 text-amber-900"
      : "border-blue-200 bg-blue-50 text-blue-900";
  return <div className={`rounded-xl border px-3.5 py-3 text-xs leading-relaxed ${styles}`}>{children}</div>;
}

export function InlineCode({ children }: { children: React.ReactNode }) {
  return <code className="rounded-md bg-gray-100 px-1.5 py-0.5 font-mono text-[0.85em] text-gray-800">{children}</code>;
}

export function EndpointPath({ method, path }: { method: string; path: string }) {
  return (
    <div className="inline-flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50/70 px-3 py-1.5">
      <MethodBadge method={method} />
      <code className="text-xs text-gray-700">{path}</code>
    </div>
  );
}

export type KeyMode = "default" | "ownKey";

export function KeyModeNavbar({
  mode,
  onChange,
}: {
  mode: KeyMode;
  onChange: (m: KeyMode) => void;
}) {
  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border-subtle bg-app-bg px-3 py-2">
      <div className="flex items-center gap-2">
        <span className="inline-block h-2 w-2 rounded-full bg-navy/60" />
        <span className="text-xs font-semibold text-navy">Implementation Mode:</span>
      </div>
      <div className="inline-flex rounded-lg border border-border-subtle bg-white p-0.5 shadow-xs">
        <button
          type="button"
          onClick={() => onChange("default")}
          className={`rounded-md px-3 py-1 text-xs font-medium transition-all ${
            mode === "default"
              ? "bg-navy text-white shadow-xs font-semibold"
              : "text-gray-500 hover:text-navy"
          }`}
        >
          Default (Server Key)
        </button>
        <button
          type="button"
          onClick={() => onChange("ownKey")}
          className={`rounded-md px-3 py-1 text-xs font-medium transition-all ${
            mode === "ownKey"
              ? "bg-navy text-white shadow-xs font-semibold"
              : "text-gray-500 hover:text-navy"
          }`}
        >
          Own Key (BYOK Gemini)
        </button>
      </div>
    </div>
  );
}

export type Lang = "python" | "javascript" | "csharp" | "curl";

const LANGUAGE_LABELS: Record<Lang, string> = {
  python: "Python",
  javascript: "JavaScript (Node.js)",
  csharp: "C# (.NET)",
  curl: "cURL (Bash)",
};

export function LanguageNavbar({
  lang,
  onChange,
  langs = ["python", "javascript", "csharp", "curl"],
}: {
  lang: Lang;
  onChange: (l: Lang) => void;
  langs?: Lang[];
}) {
  return (
    <div className="mb-3 flex flex-wrap items-center gap-1.5 border-b border-border-subtle pb-2">
      <span className="text-[11px] font-semibold uppercase tracking-wider text-gray-400 mr-2">Language:</span>
      {langs.map((id) => (
        <button
          key={id}
          type="button"
          onClick={() => onChange(id)}
          className={`rounded-md px-2.5 py-1 text-xs font-medium transition-colors ${
            lang === id
              ? "bg-navy text-white font-semibold shadow-xs"
              : "bg-gray-100 text-gray-600 hover:bg-gray-200"
          }`}
        >
          {LANGUAGE_LABELS[id]}
        </button>
      ))}
    </div>
  );
}
