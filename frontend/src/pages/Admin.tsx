import { useEffect, useState } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import type { AdminAccountSummary, AdminUsageResponse } from "../lib/types";
import StatCard from "../components/StatCard";

export default function Admin() {
  const [accounts, setAccounts] = useState<AdminAccountSummary[] | null>(null);
  const [usage, setUsage] = useState<AdminUsageResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    try {
      const [accountsData, usageData] = await Promise.all([
        apiFetch<AdminAccountSummary[]>("/admin/accounts"),
        apiFetch<AdminUsageResponse>("/admin/usage"),
      ]);
      setAccounts(accountsData);
      setUsage(usageData);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to load admin data");
    }
  }

  useEffect(() => {
    load();
  }, []);

  async function handleRevoke(clientId: string) {
    if (!confirm(`Revoke key ${clientId}? This cannot be undone.`)) return;
    try {
      await apiFetch(`/admin/credentials/${clientId}/revoke`, { method: "POST" });
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Failed to revoke key");
    }
  }

  return (
    <div className="space-y-8">
      <div>
        <h2 className="text-xl font-semibold text-gray-900">Admin</h2>
        <p className="text-sm text-gray-500">Cross-customer accounts, keys, and usage.</p>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {usage && (
        <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
          <StatCard label="Accounts" value={usage.summary.total_accounts} />
          <StatCard label="Keys" value={usage.summary.total_registered_keys} sublabel={`${usage.summary.active_keys} active`} />
          <StatCard label="Pages This Cycle" value={usage.summary.total_pages_processed_this_cycle} />
          <StatCard label="Total Requests" value={usage.summary.total_api_requests} />
        </div>
      )}

      <div className="rounded-xl border border-gray-200 bg-white p-5">
        <h3 className="text-sm font-semibold text-gray-900">Accounts</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-gray-500">
              <tr>
                <th className="py-2 pr-4">Account</th>
                <th className="py-2 pr-4">Keys</th>
                <th className="py-2 pr-4">Pages Used</th>
                <th className="py-2 pr-4">Requests</th>
                <th className="py-2 pr-4">Last Login</th>
                <th className="py-2 pr-4">Role</th>
              </tr>
            </thead>
            <tbody>
              {accounts?.map((a) => (
                <tr key={a.account_id} className="border-t border-gray-100">
                  <td className="py-2 pr-4">
                    <div className="flex items-center gap-2">
                      {a.picture_url && <img src={a.picture_url} alt="" className="h-6 w-6 rounded-full" referrerPolicy="no-referrer" />}
                      <div>
                        <p className="font-medium text-gray-900">{a.name}</p>
                        <p className="text-xs text-gray-500">{a.email}</p>
                      </div>
                    </div>
                  </td>
                  <td className="py-2 pr-4">
                    {a.active_keys} / {a.total_keys}
                  </td>
                  <td className="py-2 pr-4">{a.total_pages_used_this_cycle}</td>
                  <td className="py-2 pr-4">{a.total_requests}</td>
                  <td className="py-2 pr-4 text-xs text-gray-500">
                    {a.last_login_at ? new Date(a.last_login_at).toLocaleString() : "Never"}
                  </td>
                  <td className="py-2 pr-4">
                    {a.is_admin && <span className="rounded-full bg-purple-100 px-2 py-0.5 text-xs text-purple-700">Admin</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {accounts?.length === 0 && <p className="py-4 text-sm text-gray-400">No accounts yet.</p>}
        </div>
      </div>

      <div className="rounded-xl border border-gray-200 bg-white p-5">
        <h3 className="text-sm font-semibold text-gray-900">All Keys</h3>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="text-gray-500">
              <tr>
                <th className="py-2 pr-4">Name</th>
                <th className="py-2 pr-4">Owner</th>
                <th className="py-2 pr-4">Prefix</th>
                <th className="py-2 pr-4">Usage</th>
                <th className="py-2 pr-4">Status</th>
                <th className="py-2 pr-4">Actions</th>
              </tr>
            </thead>
            <tbody>
              {usage?.keys.map((k) => (
                <tr key={k.client_id} className="border-t border-gray-100">
                  <td className="py-2 pr-4">{k.name}</td>
                  <td className="py-2 pr-4 text-xs text-gray-500">{k.owner_account_id ?? "(admin-provisioned)"}</td>
                  <td className="py-2 pr-4 font-mono text-xs">{k.key_prefix}...</td>
                  <td className="py-2 pr-4">
                    {k.pages_used} / {k.monthly_quota}
                  </td>
                  <td className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs ${
                        k.is_active ? "bg-green-100 text-green-700" : "bg-gray-100 text-gray-500"
                      }`}
                    >
                      {k.is_active ? "Active" : "Revoked"}
                    </span>
                  </td>
                  <td className="py-2 pr-4">
                    {k.is_active && (
                      <button onClick={() => handleRevoke(k.client_id)} className="text-xs text-red-600 hover:underline">
                        Revoke
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
