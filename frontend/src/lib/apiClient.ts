// Two independent credentials, sent together on every request:
// - Portal token: identifies the signed-in Google account, authenticates /api/auth, /api/my/*.
// - Test API key: one of the account's own generated keys, pasted in to exercise the actual
//   document-processing endpoints (/api/documents/*, /api/agent/*) from the Playground.
// The backend's two auth schemes each look at their own header, so sending both is harmless —
// the API-key scheme only reads X-Document-Api-Key (falling back to Authorization only when that
// header is absent), and routes pinned to the JWT scheme never consult X-Document-Api-Key at all.
const PORTAL_TOKEN_STORAGE_KEY = "pixous_portal_token";
const TEST_API_KEY_STORAGE_KEY = "pixous_test_api_key";

export class ApiError extends Error {
  status: number;
  body: unknown;

  constructor(status: number, message: string, body?: unknown) {
    super(message);
    this.status = status;
    this.body = body;
  }
}

export function getPortalToken(): string | null {
  return localStorage.getItem(PORTAL_TOKEN_STORAGE_KEY);
}

export function setPortalToken(token: string): void {
  localStorage.setItem(PORTAL_TOKEN_STORAGE_KEY, token);
}

export function clearPortalToken(): void {
  localStorage.removeItem(PORTAL_TOKEN_STORAGE_KEY);
}

export function getTestApiKey(): string | null {
  return localStorage.getItem(TEST_API_KEY_STORAGE_KEY);
}

export function setTestApiKey(key: string): void {
  localStorage.setItem(TEST_API_KEY_STORAGE_KEY, key);
}

export function clearTestApiKey(): void {
  localStorage.removeItem(TEST_API_KEY_STORAGE_KEY);
}

export function clearSession(): void {
  clearPortalToken();
  clearTestApiKey();
}

/**
 * Raw fetch result exposed to the Playground, which needs status/headers/timing in
 * addition to the parsed body — a plain typed call doesn't give callers that.
 */
export interface RawApiResult {
  status: number;
  statusText: string;
  ok: boolean;
  headers: Record<string, string>;
  durationMs: number;
  body: unknown;
  bodyIsText: boolean;
}

async function parseBody(res: Response): Promise<{ body: unknown; bodyIsText: boolean }> {
  const contentType = res.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    try {
      return { body: await res.json(), bodyIsText: false };
    } catch {
      return { body: null, bodyIsText: false };
    }
  }
  const text = await res.text();
  return { body: text, bodyIsText: true };
}

function withAuthHeaders(init: RequestInit): RequestInit {
  const headers = new Headers(init.headers);
  const portalToken = getPortalToken();
  if (portalToken) {
    headers.set("Authorization", `Bearer ${portalToken}`);
  }
  const testKey = getTestApiKey();
  if (testKey) {
    headers.set("X-Document-Api-Key", testKey);
  }
  return { ...init, headers };
}

/** Typed JSON request/response helper for app pages (Overview, API Access). */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(`/api${path}`, withAuthHeaders({ ...init, headers }));
  const { body } = await parseBody(res);

  if (!res.ok) {
    const message =
      (body && typeof body === "object" && ("message" in body || "error" in body)
        ? (body as Record<string, string>).message ?? (body as Record<string, string>).error
        : null) || res.statusText || `Request failed with status ${res.status}`;
    throw new ApiError(res.status, message, body);
  }

  return body as T;
}

/** Raw request used by the Playground, where the endpoint/method/body are chosen at runtime. */
export async function rawApiRequest(
  path: string,
  method: string,
  options: { body?: BodyInit; isJson?: boolean } = {},
): Promise<RawApiResult> {
  const headers = new Headers();
  if (options.isJson && options.body) {
    headers.set("Content-Type", "application/json");
  }

  const started = performance.now();
  const res = await fetch(`/api${path}`, withAuthHeaders({ method, headers, body: options.body }));
  const durationMs = Math.round(performance.now() - started);
  const { body, bodyIsText } = await parseBody(res);

  const responseHeaders: Record<string, string> = {};
  res.headers.forEach((value, key) => {
    responseHeaders[key] = value;
  });

  return {
    status: res.status,
    statusText: res.statusText,
    ok: res.ok,
    headers: responseHeaders,
    durationMs,
    body,
    bodyIsText,
  };
}
