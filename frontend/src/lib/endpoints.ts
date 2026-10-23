export interface EndpointDef {
  id: string;
  method: "GET" | "POST" | "DELETE";
  path: string; // may contain {paramName} placeholders
  description: string;
  bodyType: "none" | "json" | "multipart";
  defaultBody?: string;
  roles: string[];
}

export const ENDPOINTS: EndpointDef[] = [
  { id: "usage-me", method: "GET", path: "/usage/me", description: "Get my usage", bodyType: "none", roles: [] },
  { id: "usage-all", method: "GET", path: "/usage/all", description: "Get all usage (admin)", bodyType: "none", roles: ["admin"] },
  { id: "credentials-list", method: "GET", path: "/credentials", description: "List API keys (admin)", bodyType: "none", roles: ["admin"] },
  {
    id: "credentials-create",
    method: "POST",
    path: "/credentials",
    description: "Create API key (admin)",
    bodyType: "json",
    defaultBody: '{\n  "name": "My App",\n  "permissions": ["process"]\n}',
    roles: ["admin"],
  },
  {
    id: "credentials-rotate",
    method: "POST",
    path: "/credentials/{clientId}/rotate",
    description: "Rotate API key (admin)",
    bodyType: "none",
    roles: ["admin"],
  },
  {
    id: "credentials-revoke",
    method: "DELETE",
    path: "/credentials/{clientId}",
    description: "Revoke API key (admin)",
    bodyType: "none",
    roles: ["admin"],
  },
  {
    id: "documents-process",
    method: "POST",
    path: "/documents/process",
    description: "Process a document (upload PDF)",
    bodyType: "multipart",
    roles: ["process", "admin"],
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
    roles: ["summarize", "admin"],
  },
  {
    id: "agent-ask",
    method: "POST",
    path: "/agent/jobs/{jobId}/ask",
    description: "Ask a question about a job",
    bodyType: "json",
    defaultBody: '{\n  "Question": "What is the effective date?"\n}',
    roles: ["ask", "admin"],
  },
  {
    id: "agent-validate-rules",
    method: "POST",
    path: "/agent/jobs/{jobId}/validate-rules",
    description: "Validate rules against a job",
    bodyType: "json",
    defaultBody: '{\n  "rules": []\n}',
    roles: ["evaluate", "admin"],
  },
];

export function extractPathParams(path: string): string[] {
  const matches = path.matchAll(/\{(\w+)\}/g);
  return [...matches].map((m) => m[1]);
}

export function buildPath(path: string, values: Record<string, string>): string {
  return path.replace(/\{(\w+)\}/g, (_, name) => encodeURIComponent(values[name] ?? ""));
}
