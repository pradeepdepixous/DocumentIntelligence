import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
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
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          {account.picture_url && (
            <img src={account.picture_url} alt="" className="h-10 w-10 rounded-full" referrerPolicy="no-referrer" />
          )}
          <div>
            <h2 className="text-xl font-semibold text-gray-900">Welcome, {account.name}</h2>
            <p className="text-sm text-gray-500">{account.email}</p>
          </div>
        </div>
        <Link
          to="/playground"
          className="rounded-md bg-gray-900 px-4 py-2 text-sm font-medium text-white hover:bg-gray-800"
        >
          Try it in the Playground &rarr;
        </Link>
      </div>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {!usage && !error && <p className="text-sm text-gray-400">Loading usage...</p>}

      {usage && (
        <>
          <div className="rounded-xl border border-gray-200 bg-white p-5">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-gray-900">Credit Balance</h3>
              <span className="text-xs text-gray-400">Resets {usage.credit.billing_cycle} (monthly)</span>
            </div>
            <p className="mt-2 text-xs text-gray-500">
              Shared across Playground testing and every API key you create — one pool, spent either way.
            </p>
            <div className="mt-4">
              <div className="flex items-end justify-between text-sm">
                <span className="text-gray-500">
                  {usage.credit.credits_used.toLocaleString()} / {usage.credit.credit_limit.toLocaleString()} pages used
                </span>
                <span className="font-semibold text-gray-900">{usage.credit.credits_remaining.toLocaleString()} remaining</span>
              </div>
              <div className="mt-2 h-2 w-full rounded-full bg-gray-100">
                <div
                  className="h-2 rounded-full bg-gray-900"
                  style={{
                    width: `${Math.min(100, usage.credit.credit_limit > 0 ? (usage.credit.credits_used / usage.credit.credit_limit) * 100 : 0)}%`,
                  }}
                />
              </div>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
            <StatCard label="API Keys" value={usage.total_keys} sublabel={`${usage.active_keys} active`} />
            <StatCard label="Total Requests" value={usage.total_requests} />
            <StatCard
              label="Status"
              value={usage.total_keys > 0 ? "Ready" : "No keys yet"}
              sublabel={usage.total_keys === 0 ? "Create a key to integrate your own app" : undefined}
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
                      <th className="py-2 pr-4">Pages Used</th>
                      <th className="py-2 pr-4">Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usage.keys.map((k) => (
                      <tr key={k.client_id} className="border-t border-gray-100">
                        <td className="py-2 pr-4">{k.name}</td>
                        <td className="py-2 pr-4 font-mono text-xs">{k.key_prefix}...</td>
                        <td className="py-2 pr-4">{k.pages_used_this_month}</td>
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
              <p className="text-sm text-gray-600">
                You can already test extraction right now in the Playground using your account credit above — no key
                needed. Create a key only when you're ready to call the API from your own application.
              </p>
              <Link to="/api-access" className="mt-2 inline-block text-sm font-medium text-blue-600 hover:underline">
                Create an API key &rarr;
              </Link>
            </div>
          )}
        </>
      )}
    </div>
  );
}
