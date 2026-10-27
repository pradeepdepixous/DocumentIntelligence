import { Link, Navigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import pixousLogo from "../assets/pixous-logo.png";

const FEATURES = [
  {
    title: "Markdown-first extraction",
    description:
      "Every page is transcribed into clean, structured markdown — tables, headings and all — so you get a faithful, readable copy of the source document, not a lossy field map.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5.586a1 1 0 0 1 .707.293l5.414 5.414a1 1 0 0 1 .293.707V19a2 2 0 0 1-2 2Z" />
    ),
  },
  {
    title: "Page-level evidence",
    description:
      "Every answer and summary can be traced back to the exact page it came from, with coverage reporting that flags any page the model couldn't read.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
    ),
  },
  {
    title: "Ask questions, get summaries",
    description:
      "Point an agent at any processed document to ask free-form questions or get a focused summary — no extra setup, no re-uploading the file.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M8.625 12a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H8.25m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H12m4.125 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Zm0 0H16.5M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
    ),
  },
  {
    title: "One credit pool, two ways in",
    description:
      "Try it instantly in the browser playground, then ship it with an API key — both draw from the same account credit, so what you test is exactly what you'll get in production.",
    icon: (
      <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 8.25h19.5M2.25 9h19.5m-16.5 5.25h6m-6 2.25h3m-4.5-9h15a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-15a1.5 1.5 0 0 1-1.5-1.5v-9a1.5 1.5 0 0 1 1.5-1.5Z" />
    ),
  },
];

const STEPS = [
  { step: "1", title: "Upload a document", description: "Drop in a PDF — invoices, contracts, reports, forms, anything." },
  { step: "2", title: "Let it process", description: "Pages are transcribed, stitched together and verified for coverage." },
  { step: "3", title: "Ask, summarize, export", description: "Query the document, get a summary, or pull it into your own app via API." },
];

export default function Landing() {
  const { isAuthenticated, isLoading } = useAuth();

  if (!isLoading && isAuthenticated) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className="min-h-screen bg-white text-gray-900">
      {/* Nav */}
      <header className="border-b border-gray-100">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-5">
          <div className="flex items-center gap-2.5">
            <img src={pixousLogo} alt="Pixous Technologies" className="h-8 w-auto" />
            <span className="hidden text-sm font-semibold tracking-tight text-gray-900 sm:inline">Document Intelligence</span>
          </div>
          <div className="flex items-center gap-3">
            <Link to="/login" className="text-sm font-medium text-gray-600 hover:text-gray-900">
              Sign in
            </Link>
            <Link
              to="/login"
              className="rounded-full bg-gray-900 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-gray-800"
            >
              Get started
            </Link>
          </div>
        </div>
      </header>

      {/* Hero */}
      <section className="mx-auto max-w-4xl px-6 pt-20 pb-24 text-center">
        <div className="mx-auto mb-6 inline-flex items-center gap-2 rounded-full border border-gray-200 bg-gray-50 px-4 py-1.5 text-xs font-medium text-gray-600">
          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
          Markdown-first document AI
        </div>
        <h1 className="text-balance text-4xl font-bold tracking-tight text-gray-900 sm:text-6xl">
          Turn any document into
          <span className="text-indigo-600"> structured, searchable intelligence</span>
        </h1>
        <p className="mx-auto mt-6 max-w-2xl text-pretty text-base text-gray-500 sm:text-lg">
          Upload PDFs and get faithful page-by-page transcription, verified evidence, and an agent you can ask questions —
          all in one API, with a playground to try it before you integrate it.
        </p>
        <div className="mt-10 flex flex-col items-center justify-center gap-3 sm:flex-row">
          <Link
            to="/login"
            className="w-full rounded-full bg-gray-900 px-7 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-gray-800 sm:w-auto"
          >
            Create a free account
          </Link>
          <Link
            to="/login"
            className="w-full rounded-full border border-gray-300 bg-white px-7 py-3 text-sm font-semibold text-gray-900 transition hover:bg-gray-50 sm:w-auto"
          >
            Sign in
          </Link>
        </div>
        <p className="mt-4 text-xs text-gray-400">No credit card required — every account starts with free credits.</p>
      </section>

      {/* Feature grid */}
      <section className="mx-auto max-w-6xl px-6 pb-24">
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2">
          {FEATURES.map((f) => (
            <div
              key={f.title}
              className="rounded-2xl border border-gray-200 bg-white p-6 transition hover:border-gray-300 hover:shadow-sm"
            >
              <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-xl bg-indigo-50 text-indigo-600">
                <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.6} stroke="currentColor" className="h-5 w-5">
                  {f.icon}
                </svg>
              </div>
              <h3 className="text-sm font-semibold text-gray-900">{f.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-gray-500">{f.description}</p>
            </div>
          ))}
        </div>
      </section>

      {/* How it works */}
      <section className="mx-auto max-w-5xl px-6 pb-24">
        <h2 className="text-center text-2xl font-semibold text-gray-900">How it works</h2>
        <div className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-3">
          {STEPS.map((s) => (
            <div key={s.step} className="relative rounded-2xl border border-gray-200 bg-gray-50/60 p-6">
              <div className="mb-4 flex h-9 w-9 items-center justify-center rounded-full bg-gray-900 text-sm font-bold text-white">
                {s.step}
              </div>
              <h3 className="text-sm font-semibold text-gray-900">{s.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-gray-500">{s.description}</p>
            </div>
          ))}
        </div>
      </section>

      {/* CTA band */}
      <section className="mx-auto max-w-4xl px-6 pb-24">
        <div className="rounded-3xl border border-gray-200 bg-gray-50 p-10 text-center">
          <h2 className="text-2xl font-semibold text-gray-900">Ready to try it on your own documents?</h2>
          <p className="mx-auto mt-3 max-w-md text-sm text-gray-500">
            Sign up, drop a PDF into the playground, and see the extraction, verification and Q&amp;A in under a minute.
          </p>
          <Link
            to="/login"
            className="mt-6 inline-block rounded-full bg-gray-900 px-7 py-3 text-sm font-semibold text-white shadow-sm transition hover:bg-gray-800"
          >
            Get started for free
          </Link>
        </div>
      </section>

      <footer className="border-t border-gray-100 px-6 py-6 text-center text-xs text-gray-400">
        Pixous Document Intelligence
      </footer>
    </div>
  );
}
