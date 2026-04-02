import { CalendarSync, Link2, Unlink } from 'lucide-react';
import type { AuthStatus, TimetableMode } from '../types';

interface SyncConfirmationProps {
  auth: AuthStatus;
  mode: TimetableMode;
  timeZone: string;
  semesterEndDate: string;
  weeksDuration: number;
  loading?: boolean;
  onTimeZoneChange: (value: string) => void;
  onSemesterEndDateChange: (value: string) => void;
  onWeeksDurationChange: (value: number) => void;
  onConnect: () => void;
  onDisconnect: () => void;
  onSync: () => void;
  disabled?: boolean;
}

export function SyncConfirmation({
  auth,
  mode,
  timeZone,
  semesterEndDate,
  weeksDuration,
  loading = false,
  onTimeZoneChange,
  onSemesterEndDateChange,
  onWeeksDurationChange,
  onConnect,
  onDisconnect,
  onSync,
  disabled = false,
}: SyncConfirmationProps) {
  return (
    <div className="panel p-6">
      <div className="mb-5">
        <p className="chip">Sync</p>
        <h3 className="mt-3 text-xl font-bold">Google Calendar handoff</h3>
        <p className="mt-2 text-sm text-white/60">
          Connect Google, confirm timezone settings, then create the calendar events from the parsed preview.
        </p>
      </div>

      <div className="rounded-3xl border border-white/10 bg-white/[0.03] p-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="text-sm font-semibold">
              {auth.connected ? 'Google Calendar connected' : 'Google Calendar not connected'}
            </div>
            <div className="mt-1 text-sm text-white/55">
              {auth.connected
                ? auth.email ?? 'A valid session cookie is active for calendar sync.'
                : 'Use the OAuth flow before syncing events.'}
            </div>
          </div>
          {auth.connected ? (
            <button type="button" className="button-secondary" onClick={onDisconnect} disabled={loading}>
              <Unlink size={16} />
              Disconnect
            </button>
          ) : (
            <button type="button" className="button-secondary" onClick={onConnect} disabled={loading}>
              <Link2 size={16} />
              Connect Google
            </button>
          )}
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
        disabled={disabled || loading || !auth.connected}
        onClick={onSync}
      >
        <CalendarSync size={18} />
        {loading ? 'Syncing...' : 'Sync to Google Calendar'}
      </button>
    </div>
  );
}
