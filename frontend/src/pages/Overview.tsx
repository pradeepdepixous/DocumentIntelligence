import { useEffect, useState } from "react";
import { apiFetch, ApiError } from "../lib/apiClient";
import { useAuth } from "../context/AuthContext";
import type { MyUsageResponse } from "../lib/types";
import StatCard from "../components/StatCard";

export default function Overview() {
  const { account } = useAuth();
  const [usage, setUsage] = useState<MyUsageResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiFetch<MyUsageResponse>("/my/usage")
      .then(setUsage)
      .catch((err) => setError(err instanceof ApiError ? err.message : "Failed to load usage"));
  }, []);

  if (!account) return null;

  return (
    <div className="space-y-8">
      <div className="flex items-center gap-3">
        {account.picture_url && (
          <img src={account.picture_url} alt="" className="h-10 w-10 rounded-full" referrerPolicy="no-referrer" />
        )}
        <div>
          <h2 className="text-xl font-semibold text-gray-900">Welcome, {account.name}</h2>
          <p className="text-sm text-gray-500">{account.email}</p>
        </div>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {usage && (
        <>
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            <StatCard label="API Keys" value={usage.total_keys} sublabel={`${usage.active_keys} active`} />
            <StatCard label="Pages Used" value={usage.total_pages_used_this_cycle} sublabel="this billing cycle" />
            <StatCard label="Total Requests" value={usage.total_requests} />
            <StatCard
              label="Status"
              value={usage.total_keys > 0 ? "Ready" : "No keys yet"}
              sublabel={usage.total_keys === 0 ? "Create a key to get started" : undefined}
            />
          </div>

          {usage.keys.length > 0 && (
            <div className="rounded-xl border border-gray-200 bg-white p-5">
              <h3 className="text-sm font-semibold text-gray-900">Your API Keys</h3>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="text-gray-500">
                    <tr>
                      <th className="py-2 pr-4">Name</th>
                      <th className="py-2 pr-4">Key</th>
                      <th className="py-2 pr-4">Usage</th>
                      <th className="py-2 pr-4">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usage.keys.map((k) => (
                      <tr key={k.client_id} className="border-t border-gray-100">
                        <td className="py-2 pr-4">{k.name}</td>
                        <td className="py-2 pr-4 font-mono text-xs">{k.key_prefix}...</td>
                        <td className="py-2 pr-4">
                          {k.pages_used_this_month} / {k.monthly_page_quota}
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
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {usage.total_keys === 0 && (
            <div className="rounded-xl border border-dashed border-gray-300 bg-white p-6 text-center">
              <p className="text-sm text-gray-600">You haven't created an API key yet.</p>
              <a href="/api-access" className="mt-2 inline-block text-sm font-medium text-blue-600 hover:underline">
                Create your first key &rarr;
              </a>
            </div>
          )}
        </>
      )}
    </div>
  );
}
