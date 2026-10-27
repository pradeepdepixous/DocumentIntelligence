import type { RuleDefinition } from "../lib/documentTypes";

const CATEGORY_PRESETS = ["General", "Contract", "Invoice", "Compliance", "Financial", "Legal", "Security", "Other"];

function KnockoutToggle({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      aria-pressed={checked}
      className={`relative h-5 w-9 shrink-0 rounded-full transition ${checked ? "bg-navy" : "bg-gray-200"}`}
    >
      <span
        className={`absolute top-0.5 left-0.5 h-4 w-4 rounded-full bg-white shadow transition ${checked ? "translate-x-4" : "translate-x-0"}`}
      />
    </button>
  );
}

export default function ValidationRulesTable({
  rules,
  onChange,
}: {
  rules: RuleDefinition[];
  onChange: (rules: RuleDefinition[]) => void;
}) {
  function updateRule(index: number, patch: Partial<RuleDefinition>) {
    onChange(rules.map((r, i) => (i === index ? { ...r, ...patch } : r)));
  }

  function addRule() {
    const n = rules.length + 1;
    onChange([
      ...rules,
      { rule_id: `RULE-${n}`, category: "General", name: "", rule_description: "", is_knockout: false, max_marks: 10 },
    ]);
  }

  function removeRule(index: number) {
    onChange(rules.filter((_, i) => i !== index));
  }

  return (
    <div>
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-sm font-semibold text-navy">Validation rules</h3>
          <p className="text-xs text-gray-500">Define the conditions the document must satisfy.</p>
        </div>
        <button
          onClick={addRule}
          className="rounded-md bg-navy px-3 py-1.5 text-xs font-medium text-white hover:bg-navy-dark"
        >
          + Add rule
        </button>
      </div>

      <div className="mt-3 overflow-x-auto rounded-lg border border-border-subtle">
        <table className="w-full text-left text-sm">
          <thead className="bg-app-bg text-xs text-gray-500">
            <tr>
              <th className="w-10 px-3 py-2 font-medium">#</th>
              <th className="w-36 px-3 py-2 font-medium">Category</th>
              <th className="px-3 py-2 font-medium">Criterion</th>
              <th className="w-24 px-3 py-2 font-medium">Weight (%)</th>
              <th className="w-24 px-3 py-2 font-medium">Knockout</th>
              <th className="w-12 px-3 py-2 font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule, i) => {
              const invalidWeight = rule.max_marks <= 0;
              return (
                <tr key={i} className="border-t border-border-subtle align-top">
                  <td className="px-3 py-3 text-xs text-gray-400">{i + 1}</td>
                  <td className="px-3 py-3">
                    <select
                      value={CATEGORY_PRESETS.includes(rule.category) ? rule.category : "Other"}
                      onChange={(e) =>
                        updateRule(i, { category: e.target.value === "Other" ? "" : e.target.value })
                      }
                      className="w-full rounded-md border border-border-subtle bg-white px-2 py-1.5 text-xs focus:border-navy focus:outline-none"
                    >
                      {CATEGORY_PRESETS.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                    {!CATEGORY_PRESETS.includes(rule.category) && (
                      <input
                        name={`rule-category-${i}`}
                        autoComplete="off"
                        value={rule.category}
                        onChange={(e) => updateRule(i, { category: e.target.value })}
                        placeholder="Custom category"
                        className="mt-1 w-full rounded-md border border-border-subtle px-2 py-1 text-xs focus:border-navy focus:outline-none"
                      />
                    )}
                  </td>
                  <td className="px-3 py-3">
                    <input
                      name={`rule-description-${i}`}
                      autoComplete="off"
                      value={rule.rule_description}
                      onChange={(e) => updateRule(i, { rule_description: e.target.value })}
                      placeholder="e.g. Contract includes a termination clause"
                      className="w-full rounded-md border border-border-subtle px-2 py-1.5 text-xs focus:border-navy focus:outline-none"
                    />
                  </td>
                  <td className="px-3 py-3">
                    <input
                      name={`rule-weight-${i}`}
                      autoComplete="off"
                      type="number"
                      min={1}
                      value={rule.max_marks}
                      onChange={(e) => updateRule(i, { max_marks: Number(e.target.value) || 0 })}
                      className={`w-full rounded-md border px-2 py-1.5 text-xs focus:outline-none ${
                        invalidWeight ? "border-red-400 focus:border-red-400" : "border-border-subtle focus:border-navy"
                      }`}
                    />
                    {invalidWeight && <p className="mt-1 text-[10px] text-red-600">Must be greater than 0</p>}
                  </td>
                  <td className="px-3 py-3">
                    <KnockoutToggle checked={rule.is_knockout} onChange={(v) => updateRule(i, { is_knockout: v })} />
                  </td>
                  <td className="px-3 py-3">
                    <button
                      onClick={() => removeRule(i)}
                      disabled={rules.length === 1}
                      title="Remove rule"
                      className="rounded-md border border-border-subtle p-1.5 text-gray-400 hover:border-red-200 hover:bg-red-50 hover:text-red-600 disabled:opacity-30"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" strokeWidth={1.8} stroke="currentColor" className="h-3.5 w-3.5">
                        <path strokeLinecap="round" strokeLinejoin="round" d="m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0" />
                      </svg>
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
