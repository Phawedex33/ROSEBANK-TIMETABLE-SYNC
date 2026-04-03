import { AlertTriangle, Calendar, LoaderCircle } from 'lucide-react';
import { useMemo, useState } from 'react';
import { EventPreview } from '../components/EventPreview';
import { FileUpload } from '../components/FileUpload';
import { ModeSelector } from '../components/ModeSelector';
import { ParserDiagnosticsPanel } from '../components/ParserDiagnosticsPanel';
import { SyncConfirmation } from '../components/SyncConfirmation';
import { ModuleSelection } from '../components/ModuleSelection';
import { apiService } from '../services/api';
import type {
  AcademicSyncPayload,
  AssessmentSyncPayload,
  DiagnosticRow,
  PreviewEvent,
  PreviewResponse,
  RosebankAssessmentEvent,
  RosebankClassEvent,
  RosebankParseResponse,
  TimetableMode,
} from '../types';

function confidenceToScore(confidence: 'high' | 'medium' | 'low'): number {
  if (confidence === 'high') return 0.95;
  if (confidence === 'medium') return 0.78;
  return 0.56;
}

function mapClassEvent(event: RosebankClassEvent): PreviewEvent {
  return {
    id: event.id,
    mode: 'academic',
    title: event.subject_name ?? event.subject_code,
    subjectCode: event.subject_code,
    day: event.day_of_week ?? undefined,
    startTime: event.start_time ?? '00:00',
    endTime: event.end_time ?? '00:00',
    location: event.room,
    lecturer: event.lecturer,
    notes: event.notes,
    confidence: event.confidence,
  };
}

function mapAssessmentEvent(event: RosebankAssessmentEvent): PreviewEvent {
  const time = event.due_time ?? '23:59';
  const deliveryMode = event.submission_type === 'online' ? 'Online Submission' : 'Campus Sitting';
  const detailParts = [event.assessment_type, deliveryMode];
  return {
    id: event.id,
    mode: 'assessment',
    title: event.subject_name ?? event.subject_code,
    subjectCode: event.subject_code,
    date: event.specific_date ?? undefined,
    startTime: time,
    endTime: time,
    deliveryMode,
    sitting: null,
    notes: detailParts.join(' · '),
    confidence: event.confidence,
  };
}

function buildDiagnostics(events: PreviewEvent[]): DiagnosticRow[] {
  return events.map((event) => ({
    id: event.id,
    raw: event.title,
    parsed: {
      day: event.day ?? event.date ?? 'Unscheduled',
      time: `${event.startTime}${event.endTime !== event.startTime ? `-${event.endTime}` : ''}`,
      module: event.subjectCode,
    },
    parserBranch: event.mode === 'academic' ? 'RosebankAcademicParser' : 'RosebankAssessmentParser',
    confidence: confidenceToScore(event.confidence),
  }));
}

function normalizePreview(response: RosebankParseResponse, mode: TimetableMode): PreviewResponse {
  const events =
    mode === 'academic'
      ? response.schedules.class_schedule.events.map(mapClassEvent)
      : response.schedules.assessment_schedule.events.map(mapAssessmentEvent);

  return {
    rows: buildDiagnostics(events),
    events,
    errors: response.warnings.map((warning) => `${warning.severity.toUpperCase()}: ${warning.issue}`),
    summary: {
      totalRows: events.length,
      validRows: events.length,
      parserStats: {
        high: events.filter((event) => event.confidence === 'high').length,
        medium: events.filter((event) => event.confidence === 'medium').length,
        low: events.filter((event) => event.confidence === 'low').length,
      },
    },
  };
}

function buildAcademicPayload(
  events: PreviewEvent[],
  year: string,
  group: string,
  timeZone: string,
  semesterEndDate: string,
  weeksDuration: number,
): AcademicSyncPayload {
  return {
    year: Number.parseInt(year.replace(/\D/g, ''), 10) || 0,
    group,
    timeZone,
    semesterEndDate: semesterEndDate || undefined,
    weeksDuration,
    events: events
      .filter((event) => !!event.day)
      .map((event) => ({
        day: event.day!,
        startTime: event.startTime,
        endTime: event.endTime,
        subject: event.title,
        lecturer: event.lecturer ?? '',
        venue: event.location ?? '',
      })),
  };
}

function buildAssessmentPayload(events: PreviewEvent[], timeZone: string): AssessmentSyncPayload {
  return {
    timeZone,
    durationMinutes: 60,
    events: events
      .filter((event) => !!event.date)
      .map((event) => ({
        date: event.date!,
        time: event.startTime,
        moduleCode: event.subjectCode,
        moduleName: event.title,
        assessmentType: event.notes?.split(' · ')[0] ?? 'Assessment',
        deliveryMode: event.deliveryMode ?? 'Unspecified',
        sitting: event.sitting ?? null,
      })),
  };
}

export function App() {
  const [mode, setMode] = useState<TimetableMode>('academic');
  const [classFile, setClassFile] = useState<File | null>(null);
  const [assessmentFile, setAssessmentFile] = useState<File | null>(null);
  const [assessmentAttempt, setAssessmentAttempt] = useState<'main' | 'supplementary'>('main');
  const [year, setYear] = useState('DIS3');
  const [group, setGroup] = useState('GR1');
  const [timeZone, setTimeZone] = useState('Africa/Johannesburg');
  const [semesterEndDate, setSemesterEndDate] = useState('');
  const [weeksDuration, setWeeksDuration] = useState(16);
  const [filterText, setFilterText] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [statusText, setStatusText] = useState('Ready.');
  const [lastDownloadName, setLastDownloadName] = useState<string | null>(null);
  const [rawPreview, setRawPreview] = useState<RosebankParseResponse | null>(null);
  const [selectedModules, setSelectedModules] = useState<Set<string>>(new Set());

  const preview = useMemo(() => {
    if (!rawPreview) return null;

    const clonedResponse: RosebankParseResponse = {
      ...rawPreview,
      schedules: {
        class_schedule: {
          ...rawPreview.schedules.class_schedule,
          events: rawPreview.schedules.class_schedule.events.filter((e) => selectedModules.has(e.subject_code)),
        },
        assessment_schedule: {
          ...rawPreview.schedules.assessment_schedule,
          events: rawPreview.schedules.assessment_schedule.events.filter((e) => selectedModules.has(e.subject_code)),
        },
      },
    };
    return normalizePreview(clonedResponse, mode);
  }, [mode, rawPreview, selectedModules]);

  const filteredPreview = useMemo(() => {
    if (!preview) return null;
    const search = filterText.trim().toLowerCase();
    if (!search) return preview;

    return {
      ...preview,
      rows: preview.rows.filter((row) =>
        `${row.raw} ${row.parsed.day} ${row.parsed.module}`.toLowerCase().includes(search),
      ),
      events: preview.events.filter((event) =>
        `${event.title} ${event.subjectCode} ${event.day ?? ''} ${event.date ?? ''}`.toLowerCase().includes(search),
      ),
    };
  }, [filterText, preview]);

  async function handleParse() {
    if (mode === 'academic' && !classFile) {
      setError('Select the class schedule PDF before parsing.');
      return;
    }

    if (mode === 'assessment' && !assessmentFile) {
      setError('Select the assessment PDF before parsing.');
      return;
    }

    setLoading(true);
    setError(null);
    setStatusText(mode === 'academic' ? 'Parsing academic timetable...' : 'Parsing assessment timetable...');

    try {
      const response = await apiService.previewRosebank(
        classFile,
        assessmentFile,
        year,
        assessmentAttempt,
        mode === 'academic' ? group : undefined,
      );
      setRawPreview(response);
      setLastDownloadName(null);
      
      const defaultModules = new Set<string>();
      if (response.available_modules && response.available_modules[year]) {
        Object.keys(response.available_modules[year]).forEach(code => defaultModules.add(code));
      }
      setSelectedModules(defaultModules);

      setStatusText('Preview ready.');
    } catch (responseError) {
      setError(responseError instanceof Error ? responseError.message : 'Parsing failed.');
      setStatusText('Parsing failed.');
    } finally {
      setLoading(false);
    }
  }

  function downloadCalendarFile(blob: Blob, prefix: string) {
    // Use a temporary object URL so the browser can download the generated .ics file directly.
    const fileUrl = URL.createObjectURL(blob);
    const link = document.createElement('a');
    const fileName = `${prefix}-${mode}-${new Date().toISOString().slice(0, 10)}.ics`;
    link.href = fileUrl;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(fileUrl);
    setLastDownloadName(fileName);
  }

  async function handleExport() {
    if (!preview) return;

    setLoading(true);
    setError(null);
    setStatusText('Building calendar file...');

    try {
      if (mode === 'academic') {
        const payload = buildAcademicPayload(preview.events, year, group, timeZone, semesterEndDate, weeksDuration);
        const file = await apiService.exportAcademic(payload);
        downloadCalendarFile(file, 'rosebank-academic');
        setStatusText('Academic calendar file downloaded.');
      } else {
        const payload = buildAssessmentPayload(preview.events, timeZone);
        const file = await apiService.exportAssessment(payload);
        downloadCalendarFile(file, 'rosebank-assessment');
        setStatusText('Assessment calendar file downloaded.');
      }
    } catch (responseError) {
      setError(responseError instanceof Error ? responseError.message : 'Export failed.');
      setStatusText('Export failed.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="min-h-screen px-4 py-8 md:px-8">
      <div className="mx-auto max-w-7xl">
        <header className="mb-8 panel overflow-hidden">
          <div className="flex flex-wrap items-center justify-between gap-5 px-6 py-6 md:px-8">
            <div className="flex items-center gap-4">
              <div className="rounded-3xl bg-accent/15 p-4 text-accent">
                <Calendar size={28} />
              </div>
              <div>
                <p className="chip">Frontend Modernized</p>
                <h1 className="mt-3 text-3xl font-black tracking-tight md:text-4xl">Rosebank Timetable Sync</h1>
                <p className="mt-2 max-w-2xl text-sm text-white/60 md:text-base">
                  Parse Rosebank timetables, review the results, and download a calendar file that imports cleanly into common calendar apps.
                </p>
              </div>
            </div>
            <div className="rounded-3xl border border-white/10 bg-white/[0.03] px-5 py-4">
              <div className="text-xs uppercase tracking-[0.25em] text-white/40">Status</div>
              <div className="mt-2 text-sm font-semibold text-white/80">{statusText}</div>
            </div>
          </div>
        </header>

        <div className="grid gap-6 xl:grid-cols-[1.4fr_1fr]">
          <div className="space-y-6">
            <ModeSelector mode={mode} onChange={setMode} disabled={loading} />

            <FileUpload
              mode={mode}
              classFile={classFile}
              assessmentFile={assessmentFile}
              onClassFileSelect={setClassFile}
              onAssessmentFileSelect={setAssessmentFile}
              disabled={loading}
            />

            <div className="panel p-6">
              <div className="mb-5">
                <p className="chip">Configuration</p>
                <h2 className="mt-3 text-2xl font-bold">Parser inputs</h2>
              </div>
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
                <div>
                  <label className="mb-2 block text-sm font-semibold text-white/80">Student year</label>
                  <select title="Student year" className="field" value={year} onChange={(event) => setYear(event.target.value)} disabled={loading}>
                    <option value="DIS1">DIS1</option>
                    <option value="DIS2">DIS2</option>
                    <option value="DIS3">DIS3</option>
                  </select>
                </div>
                {mode === 'academic' ? (
                  <div>
                    <label className="mb-2 block text-sm font-semibold text-white/80">Group</label>
                    <select title="Student group" className="field" value={group} onChange={(event) => setGroup(event.target.value)} disabled={loading}>
                      <option value="GR1">GR1</option>
                      <option value="GR2">GR2</option>
                      <option value="GR3">GR3</option>
                    </select>
                  </div>
                ) : (
                  <>
                    <div>
                      <label className="mb-2 block text-sm font-semibold text-white/80">Assessment attempt</label>
                      <select
                        title="Assessment attempt"
                        className="field"
                        value={assessmentAttempt}
                        // Supplementary mode asks the parser for deferred-only rows from the assessment schedule.
                        onChange={(event) => setAssessmentAttempt(event.target.value as 'main' | 'supplementary')}
                        disabled={loading}
                      >
                        <option value="main">Main timetable</option>
                        <option value="supplementary">Resubmission / supplementary</option>
                      </select>
                    </div>
                    <div className="rounded-3xl border border-white/10 bg-white/[0.03] p-4 text-sm text-white/65">
                      Assessment parsing only needs the year plus the assessment PDF.
                    </div>
                  </>
                )}
                <div className="md:col-span-2 flex items-end">
                  <button
                    type="button"
                    className="button-primary w-full"
                    onClick={handleParse}
                    disabled={loading || (mode === 'academic' ? !classFile : !assessmentFile)}
                  >
                    {loading ? <LoaderCircle className="animate-spin" size={18} /> : null}
                    {loading ? 'Working...' : 'Parse Timetable'}
                  </button>
                </div>
              </div>
            </div>

            {rawPreview && rawPreview.available_modules ? (
              <ModuleSelection 
                availableModules={rawPreview.available_modules}
                selectedModules={selectedModules}
                onSelectionChange={setSelectedModules}
                studentYear={year}
              />
            ) : null}

            {filteredPreview ? (
              <EventPreview
                mode={mode}
                events={filteredPreview.events}
                filterText={filterText}
                onFilterChange={setFilterText}
              />
            ) : null}
          </div>

          <div className="space-y-6">
            {error ? (
              <div className="panel border-red-500/30 bg-red-500/10 p-5">
                <div className="flex items-start gap-3">
                  <AlertTriangle className="mt-0.5 text-red-300" size={18} />
                  <div>
                    <div className="font-semibold text-red-200">Something needs attention</div>
                    <p className="mt-1 text-sm text-red-100/80">{error}</p>
                  </div>
                </div>
              </div>
            ) : null}

            {filteredPreview?.rows.length ? <ParserDiagnosticsPanel rows={filteredPreview.rows} /> : null}

            <SyncConfirmation
              mode={mode}
              events={preview?.events || []}
              timeZone={timeZone}
              semesterEndDate={semesterEndDate}
              weeksDuration={weeksDuration}
              loading={loading}
              disabled={!preview || preview.events.length === 0}
              onTimeZoneChange={setTimeZone}
              onSemesterEndDateChange={setSemesterEndDate}
              onWeeksDurationChange={setWeeksDuration}
              lastDownloadName={lastDownloadName}
              onExport={() => {
                handleExport().catch((responseError: Error) => setError(responseError.message));
              }}
            />

            {filteredPreview?.errors?.length ? (
              <div className="panel p-6">
                <p className="chip">Warnings</p>
                <div className="mt-4 space-y-3">
                  {filteredPreview.errors.map((warning) => (
                    <div key={warning} className="rounded-2xl border border-amber-500/25 bg-amber-500/10 p-4 text-sm text-amber-100/90">
                      {warning}
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </div>
        </div>
      </div>
    </div>
  );
}
