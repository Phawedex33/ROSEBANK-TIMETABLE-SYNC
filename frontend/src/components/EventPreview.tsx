import { CalendarDays, Filter, MapPin, UserRound } from 'lucide-react';
import type { PreviewEvent, TimetableMode } from '../types';

interface EventPreviewProps {
  mode: TimetableMode;
  events: PreviewEvent[];
  filterText: string;
  onFilterChange: (value: string) => void;
}

export function EventPreview({ mode, events, filterText, onFilterChange }: EventPreviewProps) {
  const emptyCopy =
    mode === 'academic'
      ? 'Parse a class timetable to preview recurring lectures.'
      : 'Parse with the assessment PDF to preview deadlines and exam-style events.';

  return (
    <div className="panel p-6">
      <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="chip">Preview</p>
          <h3 className="mt-3 text-xl font-bold">
            {mode === 'academic' ? 'Class events' : 'Assessment events'}
          </h3>
        </div>
        <label className="relative block min-w-[220px]">
          <Filter className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-white/40" size={16} />
          <input
            value={filterText}
            onChange={(event) => onFilterChange(event.target.value)}
            placeholder="Filter by module or date"
            className="field pl-11"
          />
        </label>
      </div>

      {events.length === 0 ? (
        <div className="rounded-3xl border border-white/10 bg-white/[0.03] p-8 text-center text-sm text-white/55">
          {emptyCopy}
        </div>
      ) : (
        <div className="space-y-3">
          {events.map((event) => (
            <div key={event.id} className="rounded-3xl border border-white/10 bg-white/[0.03] p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h4 className="text-lg font-semibold">{event.title}</h4>
                    <span className="chip border-white/15 text-white/60">{event.subjectCode}</span>
                  </div>
                  <div className="mt-3 flex flex-wrap gap-4 text-sm text-white/65">
                    <span className="inline-flex items-center gap-2">
                      <CalendarDays size={15} />
                      {event.day ? `${event.day} ${event.startTime} - ${event.endTime}` : `${event.date} ${event.startTime}`}
                    </span>
                    {event.deliveryMode && (
                      <span className="inline-flex items-center gap-2">
                        {event.deliveryMode}
                      </span>
                    )}
                    {event.location && (
                      <span className="inline-flex items-center gap-2">
                        <MapPin size={15} />
                        {event.location}
                      </span>
                    )}
                    {event.lecturer && (
                      <span className="inline-flex items-center gap-2">
                        <UserRound size={15} />
                        {event.lecturer}
                      </span>
                    )}
                  </div>
                </div>
                <span className="chip border-accent/30 text-accent">{event.confidence}</span>
              </div>
              {event.notes && <p className="mt-3 text-sm text-white/55">{event.notes}</p>}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
