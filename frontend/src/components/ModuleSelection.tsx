import { CheckSquare, Square } from 'lucide-react';

interface ModuleSelectionProps {
  availableModules: Record<string, Record<string, string>>;
  selectedModules: Set<string>;
  onSelectionChange: (modules: Set<string>) => void;
  studentYear: string;
}

export function ModuleSelection({
  availableModules,
  selectedModules,
  onSelectionChange,
  studentYear,
}: ModuleSelectionProps) {
  const toggleModule = (code: string) => {
    const newSelection = new Set(selectedModules);
    if (newSelection.has(code)) {
      newSelection.delete(code);
    } else {
      newSelection.add(code);
    }
    onSelectionChange(newSelection);
  };

  const years = Object.keys(availableModules).sort();

  return (
    <div className="panel p-6">
      <div className="mb-5">
        <p className="chip">Review Options</p>
        <h2 className="mt-3 text-2xl font-bold">Module Selection</h2>
        <p className="mt-2 text-sm text-white/60">
          Default modules for {studentYear} are selected. You can add repeating modules from other years or remove modules you are not taking.
        </p>
      </div>

      <div className="grid gap-6 md:grid-cols-3">
        {years.map((year) => {
          const subjects = availableModules[year];
          const isMainYear = year === studentYear;
          return (
            <div key={year} className="rounded-3xl border border-white/10 bg-white/[0.03] p-5">
              <div className="flex items-center justify-between mb-4">
                <h3 className="font-semibold">{year} Modules</h3>
                {isMainYear && (
                  <span className="rounded-2xl border border-emerald-400/30 bg-emerald-400/10 px-2 py-1 text-xs text-emerald-100">
                    Main
                  </span>
                )}
              </div>
              <div className="space-y-3">
                {Object.entries(subjects).map(([code, name]) => {
                  const isSelected = selectedModules.has(code);
                  return (
                    <div
                      key={code}
                      onClick={() => toggleModule(code)}
                      className={`flex cursor-pointer items-start gap-3 rounded-2xl border p-3 transition-colors ${
                        isSelected
                          ? 'border-emerald-500/50 bg-emerald-500/10 text-emerald-100'
                          : 'border-white/10 hover:bg-white/5'
                      }`}
                    >
                      <div className="mt-0.5">
                        {isSelected ? <CheckSquare size={18} className="text-emerald-400" /> : <Square size={18} className="text-white/40" />}
                      </div>
                      <div>
                        <div className="text-sm font-bold">{code}</div>
                        <div className="text-xs opacity-75">{name}</div>
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
