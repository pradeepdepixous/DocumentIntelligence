import { useEffect, useState } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import type { ClientCredential, CreateCredentialResponse, RotateCredentialResponse } from "../lib/types";
import { SELF_SERVE_PERMISSIONS } from "../lib/types";

export default function ApiAccess() {
  const [credentials, setCredentials] = useState<ClientCredential[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newName, setNewName] = useState("");
  const [newPermissions, setNewPermissions] = useState<string[]>(["process"]);
  const [creating, setCreating] = useState(false);
  const [revealedKey, setRevealedKey] = useState<{ label: string; key: string } | null>(null);
  const [copied, setCopied] = useState(false);

  async function load() {
    try {
      const data = await apiFetch<ClientCredential[]>("/my/credentials");
      setCredentials(data);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to load API keys");
    }
  }

  useEffect(() => {
    load();
  }, []);

  function togglePermission(p: string) {
    setNewPermissions((prev) => (prev.includes(p) ? prev.filter((x) => x !== p) : [...prev, p]));
  }

  async function handleCreate() {
    if (!newName.trim()) return;
    setCreating(true);
    setError(null);
    try {
      const result = await apiFetch<CreateCredentialResponse>("/my/credentials", {
        method: "POST",
        body: JSON.stringify({ name: newName, permissions: newPermissions }),
      });
      setRevealedKey({ label: result.project_name, key: result.access_key });
      setNewName("");
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to create API key");
    } finally {
      setCreating(false);
    }
  }

  async function handleRotate(clientId: string, name: string) {
    if (!confirm(`Rotate the key for "${name}"? The old key will stop working immediately.`)) return;
    try {
      const result = await apiFetch<RotateCredentialResponse>(`/my/credentials/${clientId}/rotate`, { method: "POST" });
      setRevealedKey({ label: result.project_name, key: result.new_access_key });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to rotate API key");
    }
  }

  async function handleRevoke(clientId: string, name: string) {
    if (!confirm(`Revoke the key for "${name}"? This cannot be undone.`)) return;
    try {
      await apiFetch(`/my/credentials/${clientId}`, { method: "DELETE" });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to revoke API key");
    }
  }

  async function handleCopyRevealed() {
    if (!revealedKey) return;
    await navigator.clipboard.writeText(revealedKey.key);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }

  return (
    <div className="space-y-6">
      <div className="relative overflow-hidden rounded-2xl border border-border-subtle bg-gradient-to-br from-navy via-navy to-navy-dark p-8 text-white shadow-sm">
        <div className="pointer-events-none absolute -right-16 -top-16 h-56 w-56 rounded-full bg-white/5 blur-2xl" />
        <div className="pointer-events-none absolute -bottom-20 left-1/3 h-48 w-48 rounded-full bg-white/5 blur-2xl" />
        <div className="relative">
          <span className="inline-flex items-center rounded-full border border-white/15 bg-white/10 px-2.5 py-1 text-[11px] font-medium uppercase tracking-wider text-white/80">
            API Access
          </span>
          <h2 className="mt-3 text-2xl font-semibold tracking-tight">Create and manage your API keys</h2>
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-white/70">
            Keys you create here work across every endpoint — document processing, the Analyst
            Agents, and Document Generation — scoped by the permissions you grant. Draws from your
            account's shared credit pool, visible on <strong>Overview</strong>.
          </p>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>
      )}

      {revealedKey && (
        <div className="rounded-2xl border border-amber-200 bg-amber-50 p-5 shadow-sm">
          <p className="text-sm font-medium text-amber-900">
            New key for "{revealedKey.label}" — copy it now, it won't be shown again. Never expose it in frontend code.
          </p>
          <div className="mt-3 flex items-center gap-2">
            <code className="flex-1 overflow-x-auto rounded-lg border border-amber-200 bg-white px-3.5 py-2.5 text-sm text-gray-800">
              {revealedKey.key}
            </code>
            <button
              onClick={handleCopyRevealed}
              className="shrink-0 rounded-lg bg-navy px-3.5 py-2.5 text-xs font-medium text-white transition-colors hover:bg-navy-dark"
            >
              {copied ? "✓ Copied" : "Copy"}
            </button>
          </div>
          <button
            onClick={() => setRevealedKey(null)}
            className="mt-3 text-xs font-medium text-amber-700 underline underline-offset-2 hover:text-amber-900"
          >
            Dismiss
          </button>
        </div>
      )}

      <div className="rounded-2xl border border-border-subtle bg-white p-6 shadow-sm">
        <h3 className="text-base font-semibold text-gray-900">Create a new API key</h3>
        <p className="mt-1 text-xs text-gray-500">Name it after what it's for — you'll see this name on every usage log.</p>
        <div className="mt-4 flex flex-wrap items-end gap-3">
          <div className="min-w-48 flex-1">
            <label className="block text-xs font-medium text-gray-500">Key name</label>
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="e.g. Invoice Processing"
              className="mt-1.5 w-full rounded-lg border border-gray-300 px-3.5 py-2.5 text-sm transition-colors focus:border-navy focus:outline-none focus:ring-1 focus:ring-navy"
            />
          </div>
          <button
            onClick={handleCreate}
            disabled={creating || !newName.trim()}
            className="rounded-lg bg-navy px-4 py-2.5 text-sm font-medium text-white transition-colors hover:bg-navy-dark disabled:cursor-not-allowed disabled:opacity-50"
          >
            {creating ? "Creating…" : "+ Create API key"}
          </button>
        </div>
        <div className="mt-4">
          <label className="block text-xs font-medium text-gray-500">Permissions</label>
          <div className="mt-2 flex flex-wrap gap-2">
            {SELF_SERVE_PERMISSIONS.map((p) => {
              const active = newPermissions.includes(p);
              return (
                <label
                  key={p}
                  className={`cursor-pointer select-none rounded-full border px-3.5 py-1.5 text-xs font-medium transition-colors ${
                    active
                      ? "border-navy bg-navy text-white"
                      : "border-gray-300 text-gray-600 hover:border-gray-400"
                  }`}
                >
                  <input type="checkbox" className="hidden" checked={active} onChange={() => togglePermission(p)} />
                  {p}
                </label>
              );
            })}
          </div>
          <p className="mt-2 text-[11px] text-gray-400">
            <code className="rounded bg-gray-100 px-1 py-0.5">generate</code> gates the Document
            Generation endpoints (templates, drafting, rendering) — the others gate document
            processing and the Analyst Agents.
          </p>
        </div>
      </div>

      <div className="overflow-hidden rounded-2xl border border-border-subtle bg-white shadow-sm">
        <div className="border-b border-border-subtle px-6 py-4">
          <h3 className="text-base font-semibold text-gray-900">Your keys</h3>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead>
              <tr className="border-b border-border-subtle text-[11px] font-medium uppercase tracking-wide text-gray-400">
                <th className="px-6 py-2.5">Name</th>
                <th className="px-3 py-2.5">Prefix</th>
                <th className="px-3 py-2.5">Permissions</th>
                <th className="px-3 py-2.5">Usage</th>
                <th className="px-3 py-2.5">Status</th>
                <th className="px-6 py-2.5 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {credentials?.map((c) => (
                <tr key={c.client_id} className="border-b border-gray-50 last:border-0 hover:bg-gray-50/60">
                  <td className="px-6 py-3 font-medium text-gray-900">{c.name}</td>
                  <td className="px-3 py-3 font-mono text-xs text-gray-500">{c.key_prefix}...</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-1">
                      {c.permissions.map((p) => (
                        <span key={p} className="rounded-full bg-gray-100 px-2 py-0.5 text-[11px] text-gray-600">
                          {p}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-3 py-3 text-xs text-gray-600">
                    {c.pages_used_this_month} / {c.monthly_page_quota}
                  </td>
                  <td className="px-3 py-3">
                    <span
                      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-[11px] font-medium ring-1 ring-inset ${
                        c.is_active
                          ? "bg-emerald-50 text-emerald-700 ring-emerald-600/20"
                          : "bg-gray-100 text-gray-500 ring-gray-400/20"
                      }`}
                    >
                      {c.is_active ? "Active" : "Revoked"}
                    </span>
                  </td>
                  <td className="px-6 py-3">
                    <div className="flex justify-end gap-3">
                      <button
                        onClick={() => handleRotate(c.client_id, c.name)}
                        className="text-xs font-medium text-navy hover:underline"
                      >
                        Rotate
                      </button>
                      {c.is_active && (
                        <button
                          onClick={() => handleRevoke(c.client_id, c.name)}
                          className="text-xs font-medium text-red-600 hover:underline"
                        >
                          Revoke
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {credentials === null && !error && <p className="px-6 py-6 text-sm text-gray-400">Loading keys…</p>}
          {credentials?.length === 0 && <p className="px-6 py-6 text-sm text-gray-400">No API keys yet — create one above.</p>}
        </div>
      </div>
    </div>
  );
}
