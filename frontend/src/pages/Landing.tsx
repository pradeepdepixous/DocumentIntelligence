import { Link } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import Reveal from "../components/Reveal";
import PixousMark from "../components/PixousMark";

const TRUST_ROW = ["PDF analysis", "Evidence-backed answers", "DOCX/PDF generation"];

const STATS = [
  {
    title: "Page-level evidence",
    description: "See exactly where the information comes from.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
    ),
    tone: "bg-emerald-50 text-emerald-600",
  },
  {
    title: "Secure & accurate",
    description: "Built for enterprise-grade document processing.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 2.25c-3.5 2-7 2.25-7 2.25v6.75c0 5 3 8.5 7 10.5 4-2 7-5.5 7-10.5V4.5s-3.5-.25-7-2.25Z" />
    ),
    tone: "bg-indigo-50 text-indigo-600",
  },
  {
    title: "One credit pool",
    description: "Use your credits across all features, no extra setup.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 8.25h19.5M2.25 9h19.5m-16.5 5.25h6m-6 2.25h3m-4.5-9h15a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-15a1.5 1.5 0 0 1-1.5-1.5v-9a1.5 1.5 0 0 1 1.5-1.5Z" />
    ),
    tone: "bg-amber-50 text-amber-600",
  },
];

const FEATURES = [
  {
    accent: "border-t-emerald-500",
    tone: "bg-emerald-50 text-emerald-600",
    title: "Understand every page",
    description:
      "Upload PDFs and get structured, verified extraction — with page-level evidence, headings and tables, not just a plain text copy.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5.586a1 1 0 0 1 .707.293l5.414 5.414a1 1 0 0 1 .293.707V19a2 2 0 0 1-2 2Z" />
    ),
  },
  {
    accent: "border-t-indigo-500",
    tone: "bg-indigo-50 text-indigo-600",
    title: "Ask with evidence",
    description:
      "Point to any pre-processed document and ask questions. Get accurate, context-aware answers with citations.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M8.625 12a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H8.25m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H12m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H16.5M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
    ),
  },
  {
    accent: "border-t-amber-500",
    tone: "bg-amber-50 text-amber-600",
    title: "Generate on-brand drafts",
    description:
      "Use a template and a brief to create professional documents — in your style, ready to export as DOCX or PDF.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Zm0 0L19.5 7.125M18 14v4.75A2.25 2.25 0 0 1 15.75 21H5.25A2.25 2.25 0 0 1 3 18.75V8.25A2.25 2.25 0 0 1 5.25 6H10" />
    ),
  },
];

const FLOW_STEPS = [
  { step: "1", title: "Upload / Start", description: "Add your document or brief." },
  { step: "2", title: "Process", description: "AI analyzes and extracts." },
  { step: "3", title: "Review", description: "Check evidence / draft." },
  { step: "4", title: "Download", description: "Get DOCX or PDF." },
];

const NAV_LINKS = [
  { href: "#platform", label: "Platform" },
  { href: "#how-it-works", label: "How it works" },
  { href: "#api", label: "API" },
  { href: "#docs", label: "Docs" },
];

export default function Landing() {
  const { isAuthenticated, logout } = useAuth();
  const primaryTo = isAuthenticated ? "/dashboard" : "/login";

  return (
    <div className="relative min-h-screen overflow-x-hidden bg-white text-gray-900">
      {/* Ambient gradient mesh backdrop */}
      <div className="pointer-events-none fixed inset-0 -z-10 overflow-hidden">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_20%_10%,rgba(99,102,241,0.10),transparent_40%),radial-gradient(circle_at_85%_5%,rgba(16,185,129,0.10),transparent_40%),radial-gradient(circle_at_50%_90%,rgba(245,158,11,0.06),transparent_45%)]" />
        <div className="animate-blob absolute -left-32 top-10 h-96 w-96 rounded-full bg-indigo-300/20 blur-3xl" />
        <div className="animate-blob absolute right-0 top-40 h-[28rem] w-[28rem] rounded-full bg-emerald-300/20 blur-3xl [animation-delay:4s]" />
        <div className="animate-blob absolute bottom-0 left-1/3 h-80 w-80 rounded-full bg-amber-200/20 blur-3xl [animation-delay:8s]" />
      </div>

      {/* Nav */}
      <header className="sticky top-0 z-20 border-b border-gray-100 bg-white/80 backdrop-blur-md">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-3.5">
          <div className="flex items-center gap-2.5">
            <span className="text-lg font-extrabold tracking-tight text-gray-900">
              PI<span className="text-emerald-700">X</span>OUS
            </span>
            <span className="hidden text-sm font-medium text-gray-400 sm:inline">Document Intelligence</span>
          </div>

          <nav className="hidden items-center gap-7 md:flex">
            {NAV_LINKS.map((l) => (
              <a key={l.href} href={l.href} className="relative text-sm font-medium text-gray-500 transition hover:text-gray-900 group">
                {l.label}
                <span className="absolute -bottom-1 left-0 h-px w-0 bg-gradient-to-r from-indigo-500 to-emerald-500 transition-all duration-300 group-hover:w-full" />
              </a>
            ))}
          </nav>

          <div className="flex items-center gap-4">
            {isAuthenticated ? (
              <button onClick={logout} className="text-sm font-medium text-gray-600 hover:text-gray-900">
                Log out
              </button>
            ) : (
              <Link to="/login" className="text-sm font-medium text-gray-600 hover:text-gray-900">
                Log in
              </Link>
            )}
            <Link
              to={primaryTo}
              className="group relative inline-flex items-center gap-1.5 overflow-hidden rounded-lg bg-navy px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-navy-dark hover:shadow-md"
            >
              <span className="relative z-10 flex items-center gap-1.5">
                {isAuthenticated ? "Go to dashboard" : "Open playground"}
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="h-3.5 w-3.5 transition-transform group-hover:translate-x-0.5">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M17.25 8.25 21 12m0 0-3.75 3.75M21 12H3" />
                </svg>
              </span>
            </Link>
          </div>
        </div>
      </header>

      {/* Hero */}
      <section id="platform" className="relative overflow-hidden">
        <div className="mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 pt-16 pb-20 lg:grid-cols-2 lg:pt-24">
          <div>
            <div className="animate-border-glow mb-5 inline-flex items-center gap-2 rounded-full border border-gray-200 bg-gray-50 px-3.5 py-1.5 text-xs font-medium text-gray-600">
              <span className="relative flex h-1.5 w-1.5">
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75" />
                <span className="relative inline-flex h-1.5 w-1.5 rounded-full bg-emerald-500" />
              </span>
              Document workspace
              <span className="text-gray-300">•</span>
              AI-powered
            </div>
            <h1 className="text-balance text-4xl font-bold leading-[1.1] tracking-tight text-gray-900 sm:text-5xl">
              From complex documents to{" "}
              <span className="animate-gradient-pan bg-gradient-to-r from-indigo-600 via-emerald-500 to-indigo-600 bg-clip-text text-transparent">
                clear decisions.
              </span>
            </h1>
            <p className="mt-6 max-w-xl text-pretty text-base leading-relaxed text-gray-500">
              Upload PDFs and get page-by-page transcription, verified evidence, and an agent you can ask
              questions. Or go the other way: turn a Word template and a plain-language brief into a fully
              drafted DOCX or PDF.
            </p>
            <div className="mt-8 flex flex-col gap-3 sm:flex-row">
              <Link
                to={primaryTo}
                className="group relative inline-flex items-center justify-center gap-1.5 overflow-hidden rounded-lg bg-navy px-6 py-3 text-sm font-semibold text-white shadow-sm transition hover:-translate-y-0.5 hover:bg-navy-dark hover:shadow-lg hover:shadow-navy/20"
              >
                <span className="pointer-events-none absolute inset-y-0 -left-1/3 w-1/3 skew-x-[-20deg] bg-white/20 opacity-0 transition-opacity duration-300 group-hover:opacity-100 group-hover:animate-shimmer-sweep" />
                <span className="relative z-10 flex items-center gap-1.5">
                  {isAuthenticated ? "Go to dashboard" : "Try the playground"}
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="h-3.5 w-3.5 transition-transform group-hover:translate-x-0.5">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M17.25 8.25 21 12m0 0-3.75 3.75M21 12H3" />
                  </svg>
                </span>
              </Link>
              <a
                href="#api"
                className="inline-flex items-center justify-center rounded-lg border border-gray-300 bg-white px-6 py-3 text-sm font-semibold text-gray-900 transition hover:-translate-y-0.5 hover:border-gray-400 hover:bg-gray-50 hover:shadow-md"
              >
                Explore the API
              </a>
            </div>
            <div className="mt-6 flex flex-wrap items-center gap-x-5 gap-y-2">
              {TRUST_ROW.map((t) => (
                <span key={t} className="inline-flex items-center gap-1.5 text-xs font-medium text-gray-500">
                  <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor" className="h-3.5 w-3.5 text-emerald-500">
                    <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                  </svg>
                  {t}
                </span>
              ))}
            </div>
          </div>

          {/* Hero visual */}
          <div className="relative">
            <div className="animate-blob pointer-events-none absolute -right-6 -top-6 h-32 w-32 rounded-full bg-indigo-200/50 blur-2xl" />
            <div className="animate-blob pointer-events-none absolute -bottom-8 -left-8 h-32 w-32 rounded-full bg-emerald-200/50 blur-2xl [animation-delay:3s]" />
            <div className="animate-soft-float relative overflow-hidden rounded-2xl border border-gray-200 bg-white shadow-xl transition-shadow duration-500 hover:shadow-2xl">
              <div className="flex items-center gap-1.5 border-b border-gray-100 bg-gray-50 px-4 py-2.5">
                <span className="h-2.5 w-2.5 rounded-full bg-[#ff5f57]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[#febc2e]" />
                <span className="h-2.5 w-2.5 rounded-full bg-[#28c840]" />
                <span className="ml-2 text-[11px] font-medium text-gray-400">Document Intelligence</span>
              </div>
              <div className="grid grid-cols-5 gap-3 p-4">
                <div className="col-span-1 space-y-2 rounded-lg bg-gray-50 p-2">
                  {["Documents", "Ask", "Generate", "Templates"].map((item, i) => (
                    <div
                      key={item}
                      className={`rounded-md px-2 py-1.5 text-[10px] font-medium ${
                        i === 0 ? "bg-white text-navy shadow-sm" : "text-gray-400"
                      }`}
                    >
                      {item}
                    </div>
                  ))}
                </div>
                <div className="col-span-4 space-y-2">
                  <div className="rounded-lg border border-gray-100 p-3">
                    <p className="text-[11px] font-semibold text-gray-700">Financial_Report_2024.pdf</p>
                    <p className="text-[10px] text-gray-400">10 pages • 2.4 MB</p>
                    <div className="mt-2 space-y-1.5">
                      <div className="h-1.5 w-full rounded bg-amber-100" />
                      <div className="h-1.5 w-5/6 rounded bg-gray-100" />
                      <div className="h-1.5 w-full rounded bg-emerald-100" />
                    </div>
                  </div>
                  <div className="rounded-lg border border-indigo-100 bg-indigo-50/60 p-3">
                    <p className="text-[11px] font-semibold text-indigo-700">Ask a question</p>
                    <p className="mt-1 text-[10px] leading-relaxed text-indigo-900/70">
                      According to page 4, revenue from operations increased 12.4% to ₹1,246 crore in FY2024.
                    </p>
                  </div>
                </div>
              </div>
            </div>
            <div className="animate-soft-float absolute -bottom-5 -right-5 flex items-center gap-2 rounded-xl border border-gray-200 bg-white px-3.5 py-2.5 shadow-lg [animation-delay:1.5s]">
              <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-emerald-50 text-emerald-600">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="h-4 w-4">
                  <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                </svg>
              </span>
              <div>
                <p className="text-[11px] font-semibold text-gray-900">Draft ready</p>
                <p className="text-[10px] text-gray-400">DOCX • PDF</p>
              </div>
            </div>
          </div>
        </div>

        {/* Stat row */}
        <div className="border-t border-gray-100 bg-gray-50/60">
          <div className="mx-auto grid max-w-7xl grid-cols-1 gap-6 px-6 py-8 sm:grid-cols-3">
            {STATS.map((s, i) => (
              <Reveal key={s.title} delayMs={i * 100}>
                <div className="group flex items-start gap-3">
                  <span className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${s.tone} transition-transform duration-300 group-hover:scale-110`}>
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.6} stroke="currentColor" className="h-4.5 w-4.5">
                      {s.icon}
                    </svg>
                  </span>
                  <div>
                    <p className="text-sm font-semibold text-gray-900">{s.title}</p>
                    <p className="text-xs text-gray-500">{s.description}</p>
                  </div>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* Feature cards */}
      <section className="mx-auto max-w-7xl px-6 py-20">
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
          {FEATURES.map((f, i) => (
            <Reveal key={f.title} delayMs={i * 120}>
              <div
                className={`group rounded-2xl border border-gray-200 border-t-4 ${f.accent} bg-white p-6 transition duration-300 hover:-translate-y-1.5 hover:shadow-xl`}
              >
              <div className={`mb-4 flex h-10 w-10 items-center justify-center rounded-xl ${f.tone} transition-transform duration-300 group-hover:scale-110 group-hover:rotate-3`}>
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.6} stroke="currentColor" className="h-5 w-5">
                  {f.icon}
                </svg>
              </div>
              <h3 className="text-base font-semibold text-gray-900">{f.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-gray-500">{f.description}</p>
              <span className="mt-4 inline-flex items-center gap-1 text-xs font-semibold text-gray-400 transition group-hover:text-gray-900">
                Learn more
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="h-3 w-3 transition group-hover:translate-x-0.5">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M17.25 8.25 21 12m0 0-3.75 3.75M21 12H3" />
                </svg>
              </span>
              </div>
            </Reveal>
          ))}
        </div>
      </section>

      {/* Two workflows */}
      <section id="how-it-works" className="mx-auto max-w-7xl px-6 pb-20">
        <div className="grid grid-cols-1 gap-12 lg:grid-cols-[0.8fr_1.2fr] lg:items-center">
          <Reveal>
            <div>
              <p className="text-xs font-semibold uppercase tracking-widest text-emerald-600">Two ways to work with documents</p>
              <h2 className="mt-3 text-3xl font-bold tracking-tight text-gray-900">
                One workspace.
                <br />
                Two workflows.
              </h2>
              <p className="mt-4 text-sm leading-relaxed text-gray-500">
                Whether you need to understand what's inside a document or create a new one from a brief,
                Pixous gives you a single, simple workspace.
              </p>
            </div>
          </Reveal>

          <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
            <Reveal delayMs={100}>
              <div className="group rounded-2xl border border-gray-200 bg-white p-5 shadow-sm transition duration-300 hover:-translate-y-1 hover:shadow-lg">
                <div className="mb-3 flex items-center gap-2">
                  <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-indigo-50 text-indigo-600 transition-transform duration-300 group-hover:scale-110">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.8} stroke="currentColor" className="h-4 w-4">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5.586a1 1 0 0 1 .707.293l5.414 5.414a1 1 0 0 1 .293.707V19a2 2 0 0 1-2 2Z" />
                    </svg>
                  </span>
                  <p className="text-sm font-semibold text-gray-900">Analyze a document</p>
                </div>
                <p className="text-xs text-gray-500">Upload a PDF, get structured data, and ask questions with cited evidence.</p>
                <div className="mt-4 space-y-1.5 rounded-lg border border-gray-100 bg-gray-50 p-3">
                  <div className="flex items-center justify-between text-[10px] font-medium text-gray-500">
                    <span>Project_Report.pdf</span>
                    <span>2.4 MB</span>
                  </div>
                  <div className="h-1.5 w-full overflow-hidden rounded bg-gray-200">
                    <div className="h-full w-full origin-left scale-x-0 rounded bg-indigo-400 transition-transform duration-700 group-hover:scale-x-100" />
                  </div>
                  <div className="h-1.5 w-4/5 rounded bg-indigo-200" />
                </div>
              </div>
            </Reveal>

            <Reveal delayMs={220}>
              <div className="group rounded-2xl border border-gray-200 bg-white p-5 shadow-sm transition duration-300 hover:-translate-y-1 hover:shadow-lg">
                <div className="mb-3 flex items-center gap-2">
                  <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-emerald-50 text-emerald-600 transition-transform duration-300 group-hover:scale-110">
                    <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.8} stroke="currentColor" className="h-4 w-4">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m16.862 4.487 1.687-1.688a1.875 1.875 0 1 1 2.652 2.652L10.582 16.07a4.5 4.5 0 0 1-1.897 1.13L6 18l.8-2.685a4.5 4.5 0 0 1 1.13-1.897l8.932-8.931Z" />
                    </svg>
                  </span>
                  <p className="text-sm font-semibold text-gray-900">Create a document</p>
                </div>
                <p className="text-xs text-gray-500">Start with a template and a brief, and get a polished draft in DOCX or PDF.</p>
                <div className="mt-4 space-y-1.5 rounded-lg border border-gray-100 bg-gray-50 p-3">
                  <div className="flex items-center justify-between text-[10px] font-medium text-gray-500">
                    <span>Q3 Project Report</span>
                    <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-emerald-700">Draft ready</span>
                  </div>
                  <div className="h-1.5 w-full overflow-hidden rounded bg-gray-200">
                    <div className="h-full w-full origin-left scale-x-0 rounded bg-emerald-400 transition-transform duration-700 group-hover:scale-x-100" />
                  </div>
                  <div className="h-1.5 w-3/5 rounded bg-emerald-200" />
                </div>
              </div>
            </Reveal>
          </div>
        </div>

        {/* 4-step flow */}
        <Reveal delayMs={150}>
          <div className="mt-14 grid grid-cols-2 gap-6 rounded-2xl border border-gray-200 bg-gray-50/60 p-8 sm:grid-cols-4">
            {FLOW_STEPS.map((s, i) => (
              <div key={s.step} className="group flex items-start gap-3">
                <span className="relative flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-navy text-xs font-bold text-white transition-transform duration-300 group-hover:scale-110">
                  {s.step}
                  {i < FLOW_STEPS.length - 1 && (
                    <span className="absolute left-full top-1/2 hidden h-px w-6 -translate-y-1/2 bg-gradient-to-r from-navy/40 to-transparent sm:block" />
                  )}
                </span>
                <div>
                  <p className="text-sm font-semibold text-gray-900">{s.title}</p>
                  <p className="text-xs text-gray-500">{s.description}</p>
                </div>
              </div>
            ))}
          </div>
        </Reveal>
      </section>

      {/* CTA band */}
      <section id="api" className="mx-auto max-w-7xl px-6 pb-20">
        <div className="animate-gradient-pan relative flex flex-col items-center gap-6 overflow-hidden rounded-3xl bg-gradient-to-r from-navy via-indigo-900 to-navy px-8 py-10 text-center shadow-xl shadow-navy/20 sm:flex-row sm:text-left">
          <div className="pointer-events-none absolute -right-10 -top-10 h-40 w-40 rounded-full bg-emerald-400/10 blur-3xl" />
          <div className="pointer-events-none absolute -bottom-12 left-1/4 h-40 w-40 rounded-full bg-indigo-400/10 blur-3xl" />
          <div className="relative hidden h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-white/10 sm:flex">
            <PixousMark size={28} animate />
          </div>
          <div className="flex-1">
            <h2 className="text-xl font-semibold text-white">Ready to work smarter with documents?</h2>
            <p className="mt-1.5 text-sm text-white/70">
              Sign up, then process a PDF or draft one from a template — both are live in the playground in under a minute.
            </p>
          </div>
          <Link
            to={primaryTo}
            className="group relative z-10 inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-white px-6 py-3 text-sm font-semibold text-navy shadow-sm transition hover:-translate-y-0.5 hover:bg-gray-100 hover:shadow-lg"
          >
            {isAuthenticated ? "Go to dashboard" : "Open playground"}
            <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor" className="h-3.5 w-3.5 transition-transform group-hover:translate-x-0.5">
              <path strokeLinecap="round" strokeLinejoin="round" d="M17.25 8.25 21 12m0 0-3.75 3.75M21 12H3" />
            </svg>
          </Link>
        </div>
      </section>

      <footer id="docs" className="border-t border-gray-100 px-6 py-8">
        <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-4 sm:flex-row">
          <div className="flex items-center gap-2">
            <span className="text-sm font-extrabold tracking-tight text-gray-700">
              PI<span className="text-emerald-700">X</span>OUS
            </span>
            <span className="text-xs font-medium text-gray-400">Document Intelligence</span>
          </div>
          <div className="flex items-center gap-5 text-xs text-gray-400">
            {NAV_LINKS.map((l) => (
              <a key={l.href} href={l.href} className="hover:text-gray-600">
                {l.label}
              </a>
            ))}
          </div>
        </div>
      </footer>
    </div>
  );
}
