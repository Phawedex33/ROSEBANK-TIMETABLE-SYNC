import type { DiagnosticRow } from '../types';

interface ParserDiagnosticsPanelProps {
  rows: DiagnosticRow[];
}

function getConfidenceClasses(confidence: number) {
  if (confidence >= 0.9) return 'border-green-500/40 bg-green-500/10 text-green-300';
  if (confidence >= 0.7) return 'border-amber-500/40 bg-amber-500/10 text-amber-300';
  return 'border-red-500/40 bg-red-500/10 text-red-300';
}

export function ParserDiagnosticsPanel({ rows }: ParserDiagnosticsPanelProps) {
  const stats = {
    total: rows.length,
    high: rows.filter((row) => row.confidence >= 0.9).length,
    medium: rows.filter((row) => row.confidence >= 0.7 && row.confidence < 0.9).length,
    low: rows.filter((row) => row.confidence < 0.7).length,
  };

  return (
    <div className="panel p-6">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="chip">Diagnostics</p>
          <h3 className="mt-3 text-xl font-bold">Parser confidence snapshot</h3>
        </div>
        <p className="text-sm text-white/60">
          {stats.total} rows • {stats.high} high • {stats.medium} medium • {stats.low} low
        </p>
      </div>
      <div className="mb-5 grid gap-3 md:grid-cols-3">
        <div className="rounded-2xl border border-green-500/25 bg-green-500/10 p-4">
          <div className="text-2xl font-bold text-green-300">{stats.high}</div>
          <div className="text-xs uppercase tracking-[0.2em] text-green-100/70">High confidence</div>
        </div>
        <div className="rounded-2xl border border-amber-500/25 bg-amber-500/10 p-4">
          <div className="text-2xl font-bold text-amber-300">{stats.medium}</div>
          <div className="text-xs uppercase tracking-[0.2em] text-amber-100/70">Medium confidence</div>
        </div>
        <div className="rounded-2xl border border-red-500/25 bg-red-500/10 p-4">
          <div className="text-2xl font-bold text-red-300">{stats.low}</div>
          <div className="text-xs uppercase tracking-[0.2em] text-red-100/70">Low confidence</div>
        </div>
      </div>
      <div className="space-y-3">
        {rows.map((row) => (
          <div key={row.id} className={`rounded-2xl border p-4 ${getConfidenceClasses(row.confidence)}`}>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate text-sm font-semibold">{row.raw}</p>
                <p className="mt-1 text-xs uppercase tracking-[0.2em] opacity-80">{row.parserBranch}</p>
              </div>
              <span className="text-sm font-bold">{Math.round(row.confidence * 100)}%</span>
            </div>
            <div className="mt-3 grid gap-2 text-sm text-white/80 md:grid-cols-3">
              <div>Day: {row.parsed.day}</div>
              <div>Time: {row.parsed.time}</div>
              <div>Module: {row.parsed.module}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
