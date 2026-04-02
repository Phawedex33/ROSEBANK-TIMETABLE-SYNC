import type {
  AcademicSyncPayload,
  AssessmentSyncPayload,
  AuthStatus,
  RosebankParseResponse,
  SyncResponse,
} from '../types';

const API_BASE = '/api';

async function readResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(await response.text());
  }

  return response.json() as Promise<T>;
}

export const apiService = {
  async previewRosebank(
    classFile: File,
    assessmentFile: File | null,
    year: string,
    group: string,
  ): Promise<RosebankParseResponse> {
    const formData = new FormData();
    formData.append('student_year', year);
    formData.append('student_group', group);
    formData.append('class_schedule_pdf', classFile);
    if (assessmentFile) {
      formData.append('assessment_schedule_pdf', assessmentFile);
    }

    const response = await fetch(`${API_BASE}/parser/rosebank`, {
      method: 'POST',
      body: formData,
    });

    return readResponse<RosebankParseResponse>(response);
  },

  async syncAcademic(payload: AcademicSyncPayload): Promise<SyncResponse> {
    const response = await fetch(`${API_BASE}/academic/sync`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    return readResponse<SyncResponse>(response);
  },

  async syncAssessment(payload: AssessmentSyncPayload): Promise<SyncResponse> {
    const response = await fetch(`${API_BASE}/assessment/sync`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    return readResponse<SyncResponse>(response);
  },

  async getAuthStatus(): Promise<AuthStatus> {
    const response = await fetch('/oauth/google/status');
    return readResponse<AuthStatus>(response);
  },

  async disconnectGoogle(): Promise<void> {
    const response = await fetch('/oauth/google/disconnect', { method: 'POST' });
    if (!response.ok) {
      throw new Error(await response.text());
    }
  },
};
