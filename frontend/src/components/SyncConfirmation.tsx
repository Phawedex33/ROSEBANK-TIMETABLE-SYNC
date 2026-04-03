import { Calendar, Download, Info } from 'lucide-react';
import type { TimetableMode, PreviewEvent } from '../types';

interface SyncConfirmationProps {
  mode: TimetableMode;
  events?: PreviewEvent[];
  timeZone: string;
  semesterEndDate: string;
  weeksDuration: number;
  loading?: boolean;
  onTimeZoneChange: (value: string) => void;
  onSemesterEndDateChange: (value: string) => void;
  onWeeksDurationChange: (value: number) => void;
  onExport: () => void;
  lastDownloadName?: string | null;
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
  lastDownloadName = null,
  disabled = false,
}: SyncConfirmationProps) {

  return (
    <div className="panel p-6">
      <div className="mb-5 text-center">
        <div className="mx-auto bg-emerald-500/10 w-16 h-16 rounded-full flex items-center justify-center mb-4">
           <Calendar className="text-emerald-400" size={32} />
        </div>
        <h3 className="text-2xl font-bold">Sync to Calendar</h3>
        <p className="mt-2 text-sm text-white/60 px-4">
          Export your fully prepared timetable. The generated file natively integrates with Apple Calendar, Google Calendar, Outlook, and most smartphones!
        </p>
      </div>

      <div className="rounded-3xl border border-white/10 bg-white/[0.03] p-5 mb-8">
        <h4 className="text-sm font-bold mb-3 flex items-center gap-2">
           <Info size={16} className="text-blue-400"/> Quick Guide
        </h4>
        <ul className="text-sm text-white/60 space-y-2">
           <li><strong className="text-white/80">iOS & Mac:</strong> Click sync, download the file, and open it. Calendar handles the rest natively.</li>
           <li><strong className="text-white/80">Google Calendar:</strong> Go to Google Calendar on Desktop <span className="opacity-50">→</span> Settings <span className="opacity-50">→</span> Import & Export <span className="opacity-50">→</span> Upload your synced file.</li>
           <li><strong className="text-white/80">Outlook / Android:</strong> Open the `.ics` file directly to instantly import events.</li>
        </ul>
      </div>

      <div>
        {lastDownloadName && (
          <div className="rounded-3xl border border-emerald-400/25 bg-emerald-400/10 p-4 mb-6">
            <div className="text-sm font-semibold text-emerald-100 text-center">Calendar file saved!</div>
            <p className="mt-1 text-sm text-emerald-100/80 text-center">
              Now open <span className="font-mono text-xs">{lastDownloadName}</span> to initialize the calendar import.
            </p>
          </div>
        )}

        <div className="space-y-4 px-2">
          <div>
            <label className="mb-2 block text-sm font-semibold text-white/80">Timezone Configuration</label>
            <select className="field w-full" title="Timezone" value={timeZone} onChange={(event) => onTimeZoneChange(event.target.value)} disabled={loading}>
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
                  className="field w-full"
                  title="Semester end date"
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
                  className="field w-full"
                  title="Weeks duration"
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
          className="button-primary mt-8 w-full py-4 text-base font-bold shadow-lg shadow-emerald-500/20"
          disabled={disabled || loading}
          onClick={onExport}
        >
          <Download size={20} className="mr-2" />
          {loading ? 'Preparing Sync Data...' : 'Sync Timetable'}
        </button>
      </div>
    </div>
  );
}
