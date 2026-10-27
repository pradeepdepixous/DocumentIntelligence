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

  return (
    <div className="space-y-8">
      <div>
        <h2 className="text-xl font-semibold text-gray-900">API Access</h2>
        <p className="text-sm text-gray-500">Create and manage your own API keys.</p>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {revealedKey && (
        <div className="rounded-xl border border-amber-300 bg-amber-50 p-4">
          <p className="text-sm font-medium text-amber-900">
            New key for "{revealedKey.label}" — copy it now, it won't be shown again. Never expose it in frontend code.
          </p>
          <div className="mt-2 flex items-center gap-2">
            <code className="flex-1 overflow-x-auto rounded bg-white px-3 py-2 text-sm">{revealedKey.key}</code>
            <button
              onClick={() => navigator.clipboard.writeText(revealedKey.key)}
              className="rounded-md bg-gray-900 px-3 py-2 text-xs font-medium text-white hover:bg-gray-800"
            >
              Copy
            </button>
          </div>
          <button onClick={() => setRevealedKey(null)} className="mt-2 text-xs text-amber-700 underline">
            Dismiss
          </button>
        </div>
      )}

      <div className="rounded-xl border border-gray-200 bg-white p-5">
        <h3 className="text-sm font-semibold text-gray-900">Create new API Key</h3>
        <div className="mt-3 flex flex-wrap items-end gap-3">
          <div className="flex-1 min-w-48">
            <label className="block text-xs text-gray-500">Key Name</label>
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="e.g. Invoice Processing"
              className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-gray-900 focus:outline-none"
            />
          </div>
          <button
            onClick={handleCreate}
            disabled={creating || !newName.trim()}
            className="rounded-md bg-gray-900 px-4 py-2 text-sm font-medium text-white hover:bg-gray-800 disabled:opacity-50"
          >
            {creating ? "Creating..." : "+ Create API Key"}
          </button>
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          {SELF_SERVE_PERMISSIONS.map((p) => (
            <label
              key={p}
              className={`cursor-pointer rounded-full border px-3 py-1 text-xs ${
                newPermissions.includes(p) ? "border-gray-900 bg-gray-900 text-white" : "border-gray-300 text-gray-600"
              }`}
            >
              <input type="checkbox" className="hidden" checked={newPermissions.includes(p)} onChange={() => togglePermission(p)} />
              {p}
            </label>
          ))}
        </div>
      </div>

      <div className="rounded-xl border border-gray-200 bg-white p-5">
        <h3 className="text-sm font-semibold text-gray-900">Your Keys</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-gray-500">
              <tr>
                <th className="py-2 pr-4">Name</th>
                <th className="py-2 pr-4">Prefix</th>
                <th className="py-2 pr-4">Permissions</th>
                <th className="py-2 pr-4">Usage</th>
                <th className="py-2 pr-4">Status</th>
                <th className="py-2 pr-4">Actions</th>
              </tr>
            </thead>
            <tbody>
              {credentials?.map((c) => (
                <tr key={c.client_id} className="border-t border-gray-100">
                  <td className="py-2 pr-4">{c.name}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{c.key_prefix}...</td>
                  <td className="py-2 pr-4 text-xs">{c.permissions.join(", ")}</td>
                  <td className="py-2 pr-4">
                    {c.pages_used_this_month} / {c.monthly_page_quota}
                  </td>
                  <td className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs ${
                        c.is_active ? "bg-green-100 text-green-700" : "bg-gray-100 text-gray-500"
                      }`}
                    >
                      {c.is_active ? "Active" : "Revoked"}
                    </span>
                  </td>
                  <td className="py-2 pr-4">
                    <div className="flex gap-2">
                      <button onClick={() => handleRotate(c.client_id, c.name)} className="text-xs text-blue-600 hover:underline">
                        Rotate
                      </button>
                      {c.is_active && (
                        <button onClick={() => handleRevoke(c.client_id, c.name)} className="text-xs text-red-600 hover:underline">
                          Revoke
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {credentials === null && !error && <p className="py-4 text-sm text-gray-400">Loading keys...</p>}
          {credentials?.length === 0 && <p className="py-4 text-sm text-gray-400">No API keys yet.</p>}
        </div>
      </div>
    </div>
  );
}
