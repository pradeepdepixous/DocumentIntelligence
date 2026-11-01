export interface EndpointDef {
  id: string;
  method: "GET" | "POST" | "DELETE";
  path: string; // may contain {paramName} placeholders
  description: string;
  bodyType: "none" | "json" | "multipart";
  defaultBody?: string;
  roles: string[];
}

// Everything here authenticates with the signed-in portal session (sent automatically on every
// request) — no API key needed. Document/agent actions spend the account's own credit, the same
// pool a self-serve key created in API Access draws from. There is deliberately no
// "paste an API key to test" entry: that's exactly the friction this Playground removes. A
// generated key is for the user's OWN external application, not for testing here.
export const ENDPOINTS: EndpointDef[] = [
  {
    id: "documents-process",
    method: "POST",
    path: "/documents/process",
    description: "Process a document (upload PDF)",
    bodyType: "multipart",
    roles: [],
  },
  { id: "documents-job", method: "GET", path: "/documents/jobs/{jobId}", description: "Get job status", bodyType: "none", roles: [] },
  {
    id: "documents-envelope",
    method: "GET",
    path: "/documents/jobs/{jobId}/envelope",
    description: "Get stitched envelope",
    bodyType: "none",
    roles: [],
  },
  { id: "documents-csv", method: "GET", path: "/documents/jobs/{jobId}/csv", description: "Download CSV", bodyType: "none", roles: [] },
  {
    id: "documents-markdown",
    method: "GET",
    path: "/documents/jobs/{jobId}/markdown",
    description: "Download Markdown",
    bodyType: "none",
    roles: [],
  },
  {
    id: "documents-ledger",
    method: "GET",
    path: "/documents/jobs/{jobId}/ledger",
    description: "Get reconciliation ledger",
    bodyType: "none",
    roles: [],
  },
  {
    id: "documents-review-queue",
    method: "GET",
    path: "/documents/jobs/{jobId}/review-queue",
    description: "Get review queue",
    bodyType: "none",
    roles: [],
  },
  {
    id: "agent-summarize",
    method: "POST",
    path: "/agent/jobs/{jobId}/summarize",
    description: "Summarize job",
    bodyType: "json",
    defaultBody: '{\n  "FocusArea": ""\n}',
    roles: [],
  },
  {
    id: "agent-ask",
    method: "POST",
    path: "/agent/jobs/{jobId}/ask",
    description: "Ask a question about a job",
    bodyType: "json",
    defaultBody: '{\n  "Question": "What is the effective date?"\n}',
    roles: [],
  },
  {
    id: "agent-validate-rules",
    method: "POST",
    path: "/agent/jobs/{jobId}/validate-rules",
    description: "Validate rules against a job",
    bodyType: "json",
    defaultBody: '{\n  "rules": []\n}',
    roles: [],
  },
  {
    id: "my-usage",
    method: "GET",
    path: "/my/usage",
    description: "Get my usage and credit balance",
    bodyType: "none",
    roles: [],
  },
  {
    id: "my-credentials-list",
    method: "GET",
    path: "/my/credentials",
    description: "List my API keys",
    bodyType: "none",
    roles: [],
  },
  {
    id: "admin-accounts",
    method: "GET",
    path: "/admin/accounts",
    description: "List all customer accounts (admin)",
    bodyType: "none",
    roles: ["admin"],
  },
  {
    id: "admin-usage",
    method: "GET",
    path: "/admin/usage",
    description: "Platform-wide usage across all accounts (admin)",
    bodyType: "none",
    roles: ["admin"],
  },
];

export function extractPathParams(path: string): string[] {
  const matches = path.matchAll(/\{(\w+)\}/g);
  return [...matches].map((m) => m[1]);
}

export function buildPath(path: string, values: Record<string, string>): string {
  return path.replace(/\{(\w+)\}/g, (_, name) => encodeURIComponent(values[name] ?? ""));
}
