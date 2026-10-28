const COLORS: Record<string, string> = {
  GET: "bg-blue-50 text-blue-700 ring-blue-600/20",
  POST: "bg-emerald-50 text-emerald-700 ring-emerald-600/20",
  PUT: "bg-amber-50 text-amber-700 ring-amber-600/20",
  PATCH: "bg-amber-50 text-amber-700 ring-amber-600/20",
  DELETE: "bg-red-50 text-red-700 ring-red-600/20",
};

export default function MethodBadge({ method }: { method: string }) {
  return (
    <span
      className={`inline-flex items-center rounded-md px-2 py-0.5 font-mono text-[11px] font-bold ring-1 ring-inset ${
        COLORS[method] ?? "bg-gray-50 text-gray-700 ring-gray-600/20"
      }`}
    >
      {method}
    </span>
  );
}
