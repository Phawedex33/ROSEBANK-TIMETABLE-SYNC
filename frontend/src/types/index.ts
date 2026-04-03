export type TimetableMode = 'academic' | 'assessment';
export type ConfidenceLevel = 'high' | 'medium' | 'low';
export type WarningSeverity = 'critical' | 'warning' | 'info';

export interface RosebankClassEvent {
  id: string;
  subject_code: string;
  subject_name: string | null;
  day_of_week: string | null;
  start_time: string | null;
  end_time: string | null;
  room: string | null;
  lecturer: string | null;
  confidence: ConfidenceLevel;
  notes: string | null;
}

export interface RosebankAssessmentEvent {
  id: string;
  subject_code: string;
  subject_name: string | null;
  assessment_type: string;
  submission_type: string;
  requires_turnitin: boolean;
  specific_date: string | null;
  due_time: string | null;
  is_deferred: boolean;
  confidence: ConfidenceLevel;
  notes: string | null;
}

export interface RosebankWarning {
  event_id: string | null;
  issue: string;
  severity: WarningSeverity;
}

export interface RosebankParseResponse {
  parsed_at: string;
  student_year: string;
  student_group: string;
  schedules: {
    class_schedule: {
      events: RosebankClassEvent[];
    };
    assessment_schedule: {
      events: RosebankAssessmentEvent[];
    };
  };
  warnings: RosebankWarning[];
  summary: {
    total_class_events: number;
    total_assessment_events: number;
    unique_subjects: string[];
    days_with_classes: string[];
    earliest_assessment_date: string | null;
    latest_assessment_date: string | null;
  };
  available_modules: Record<string, Record<string, string>>;
}

export interface DiagnosticRow {
  id: string;
  raw: string;
  parsed: {
    day: string;
    time: string;
    module: string;
  };
  parserBranch: string;
  confidence: number;
}

export interface PreviewEvent {
  id: string;
  mode: TimetableMode;
  title: string;
  subjectCode: string;
  day?: string;
  date?: string;
  startTime: string;
  endTime: string;
  location?: string | null;
  lecturer?: string | null;
  deliveryMode?: string | null;
  sitting?: number | null;
  notes?: string | null;
  confidence: ConfidenceLevel;
}

export interface PreviewResponse {
  rows: DiagnosticRow[];
  events: PreviewEvent[];
  errors?: string[];
  summary?: {
    totalRows: number;
    validRows: number;
    parserStats: Record<string, number>;
  };
}

export interface AcademicEvent {
  day: string;
  startTime: string;
  endTime: string;
  subject: string;
  lecturer: string;
  venue: string;
}

export interface AcademicSyncPayload {
  year: number;
  group: string;
  events: AcademicEvent[];
  timeZone: string;
  semesterEndDate?: string;
  weeksDuration?: number;
}

export interface AssessmentEvent {
  date: string;
  time: string;
  moduleCode: string;
  moduleName: string;
  assessmentType: string;
  deliveryMode: string;
  sitting: number | null;
}

export interface AssessmentSyncPayload {
  events: AssessmentEvent[];
  timeZone: string;
  durationMinutes?: number;
}
