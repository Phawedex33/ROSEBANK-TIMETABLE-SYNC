import type { TimetableMode } from '../types';

interface ModeSelectorProps {
  mode: TimetableMode;
  onChange: (mode: TimetableMode) => void;
  disabled?: boolean;
}

const MODES: Array<{ id: TimetableMode; title: string; description: string }> = [
  {
    id: 'academic',
    title: 'Class Timetable',
    description: 'Weekly recurring classes with lecturer and venue details.',
  },
  {
    id: 'assessment',
    title: 'Assessments',
    description: 'Term tests, exams, and due-date driven deadlines.',
  },
];

export function ModeSelector({ mode, onChange, disabled = false }: ModeSelectorProps) {
  return (
    <div className="panel p-6">
      <div className="mb-4">
        <p className="chip">Sync Mode</p>
        <h2 className="mt-3 text-2xl font-bold">Choose your workflow</h2>
        <p className="mt-2 text-sm text-white/60">
          The backend still uses the unified Rosebank parser, so the class timetable PDF remains required.
        </p>
      </div>
      <div className="grid gap-4 md:grid-cols-2">
        {MODES.map((item) => {
          const active = item.id === mode;
          return (
            <button
              key={item.id}
              type="button"
              disabled={disabled}
              onClick={() => onChange(item.id)}
              className={`rounded-3xl border p-5 text-left transition ${
                active
                  ? 'border-accent bg-accent/10 shadow-lg shadow-accent/10'
                  : 'border-white/10 bg-white/[0.03] hover:bg-white/[0.06]'
              } disabled:opacity-50`}
            >
              <div className="text-lg font-semibold">{item.title}</div>
              <p className="mt-2 text-sm text-white/60">{item.description}</p>
            </button>
          );
        })}
      </div>
    </div>
  );
}
