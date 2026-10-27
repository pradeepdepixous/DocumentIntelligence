const COLORS: Record<string, string> = {
  GET: "bg-blue-100 text-blue-700",
  POST: "bg-green-100 text-green-700",
  PUT: "bg-amber-100 text-amber-700",
  PATCH: "bg-amber-100 text-amber-700",
  DELETE: "bg-red-100 text-red-700",
};

export default function MethodBadge({ method }: { method: string }) {
  return (
    <span className={`rounded px-2 py-0.5 font-mono text-xs font-bold ${COLORS[method] ?? "bg-gray-100 text-gray-700"}`}>
      {method}
    </span>
  );
}
