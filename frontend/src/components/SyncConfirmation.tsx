import { Calendar, Download } from 'lucide-react';
import type { TimetableMode } from '../types';

interface SyncConfirmationProps {
  mode: TimetableMode;
  timeZone: string;
  semesterEndDate: string;
  weeksDuration: number;
  loading?: boolean;
  onTimeZoneChange: (value: string) => void;
  onSemesterEndDateChange: (value: string) => void;
  onWeeksDurationChange: (value: number) => void;
  onExport: () => void;
  disabled?: boolean;
}

export function SyncConfirmation({
  mode,
  timeZone,
  semesterEndDate,
  weeksDuration,
  loading = false,
  onTimeZoneChange,
  onSemesterEndDateChange,
  onWeeksDurationChange,
  onExport,
  disabled = false,
}: SyncConfirmationProps) {
  return (
    <div className="panel p-6">
      <div className="mb-5">
        <p className="chip">Export</p>
        <h3 className="mt-3 text-xl font-bold">Calendar file download</h3>
        <p className="mt-2 text-sm text-white/60">
          Generate an `.ics` file from the parsed preview, then import it into Google Calendar, Apple Calendar, or your phone.
        </p>
      </div>

      <div className="rounded-3xl border border-white/10 bg-white/[0.03] p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="text-sm font-semibold">No Google sign-in required</div>
            <div className="mt-1 text-sm text-white/55">
              The exported calendar file keeps this flow safe for a public repo while still working with common calendar apps.
            </div>
          </div>
          <div className="rounded-2xl border border-emerald-400/30 bg-emerald-400/10 px-3 py-2 text-sm text-emerald-100">
            .ics export enabled
          </div>
        </div>
      </div>

      <div className="mt-5 space-y-4">
        <div>
          <label className="mb-2 block text-sm font-semibold text-white/80">Timezone</label>
          <select className="field" value={timeZone} onChange={(event) => onTimeZoneChange(event.target.value)} disabled={loading}>
            <option value="Africa/Johannesburg">Africa/Johannesburg</option>
            <option value="UTC">UTC</option>
            <option value="Europe/London">Europe/London</option>
          </select>
        </div>

        {mode === 'academic' && (
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <label className="mb-2 block text-sm font-semibold text-white/80">Semester end date</label>
              <input
                type="date"
                className="field"
                value={semesterEndDate}
                onChange={(event) => onSemesterEndDateChange(event.target.value)}
                disabled={loading}
              />
            </div>
            <div>
              <label className="mb-2 block text-sm font-semibold text-white/80">Weeks duration</label>
              <input
                type="number"
                min={1}
                max={52}
                className="field"
                value={weeksDuration}
                onChange={(event) => onWeeksDurationChange(Number(event.target.value))}
                disabled={loading}
              />
            </div>
          </div>
        )}
      </div>

      <button
        type="button"
        className="button-primary mt-6 w-full"
        disabled={disabled || loading}
        onClick={onExport}
      >
        {mode === 'academic' ? <Calendar size={18} /> : <Download size={18} />}
        {loading ? 'Preparing file...' : 'Download Calendar (.ics)'}
      </button>
    </div>
  );
}
