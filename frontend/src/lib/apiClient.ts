// The portal token (issued on sign-in) is sent on every request and is the ONLY credential the
// frontend ever handles. Document-processing/agent calls made from this app — including the
// Playground — authenticate with it too and spend the signed-in account's own credit; there is no
// "paste your API key here" step anywhere in the UI. A generated API key (API Access) is only for
// the user's own external application calling this API directly, outside this frontend.
const PORTAL_TOKEN_STORAGE_KEY = "pixous_portal_token";

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

export function clearSession(): void {
  clearPortalToken();
}

/** Fired whenever a request authenticated with the portal token comes back 401, so AuthContext
 * can clear stale session state and let ProtectedRoute redirect to /login — otherwise an expired
 * or revoked session just fails silently on every subsequent call with no visible recovery. */
export const SESSION_EXPIRED_EVENT = "pixous:session-expired";

function notifyIfSessionExpired(status: number): void {
  if (status === 401 && getPortalToken()) {
    window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
  }
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
    notifyIfSessionExpired(res.status);
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

  if (!res.ok) {
    notifyIfSessionExpired(res.status);
  }

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

/** Fetches an authenticated binary endpoint and returns an object URL — for e.g. an <img src>
 * that needs the bearer token, which a plain <img> request can't attach on its own. Caller owns
 * revoking it (URL.revokeObjectURL) once no longer displayed. */
export async function fetchBlobUrl(path: string): Promise<string | null> {
  const res = await fetch(`/api${path}`, withAuthHeaders({}));
  if (!res.ok) {
    notifyIfSessionExpired(res.status);
    return null;
  }
  const blob = await res.blob();
  return URL.createObjectURL(blob);
}

function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** POSTs JSON and returns an object URL for the binary response — for a live preview <iframe>/<embed>. Caller owns revoking it. */
export async function postForBlobUrl(path: string, body: unknown): Promise<string> {
  const res = await fetch(`/api${path}`, withAuthHeaders({
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }));

  if (!res.ok) {
    notifyIfSessionExpired(res.status);
    const { body: errBody } = await parseBody(res);
    const message =
      (errBody && typeof errBody === "object" && "error" in errBody ? (errBody as Record<string, string>).error : null) ||
      `Request failed with status ${res.status}`;
    throw new ApiError(res.status, message, errBody);
  }

  const blob = await res.blob();
  return URL.createObjectURL(blob);
}

/** Downloads an authenticated file endpoint (CSV/Markdown export) and saves it via the browser,
 * since a plain <a href> can't attach the portal token's Authorization header. */
export async function downloadFile(path: string, filename: string): Promise<void> {
  const res = await fetch(`/api${path}`, withAuthHeaders({}));
  if (!res.ok) {
    notifyIfSessionExpired(res.status);
    throw new ApiError(res.status, `Download failed (${res.status})`);
  }
  const blob = await res.blob();
  saveBlob(blob, filename);
}

/** POSTs JSON and saves the binary response (e.g. document generation), reading the filename off Content-Disposition. */
export async function postAndDownload(path: string, body: unknown, fallbackFilename: string): Promise<void> {
  const res = await fetch(`/api${path}`, withAuthHeaders({
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }));

  if (!res.ok) {
    notifyIfSessionExpired(res.status);
    const { body: errBody } = await parseBody(res);
    const message =
      (errBody && typeof errBody === "object" && "error" in errBody ? (errBody as Record<string, string>).error : null) ||
      `Request failed with status ${res.status}`;
    throw new ApiError(res.status, message, errBody);
  }

  const disposition = res.headers.get("content-disposition") || "";
  const match = disposition.match(/filename="?([^";]+)"?/);
  const blob = await res.blob();
  saveBlob(blob, match ? match[1] : fallbackFilename);
}
