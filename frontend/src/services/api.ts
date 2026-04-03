import type {
  AcademicSyncPayload,
  AssessmentSyncPayload,
  RosebankParseResponse,
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
    classFile: File | null,
    assessmentFile: File | null,
    year: string,
    assessmentAttempt: 'main' | 'supplementary',
    group?: string,
  ): Promise<RosebankParseResponse> {
    const formData = new FormData();
    formData.append('student_year', year);
    formData.append('assessment_attempt', assessmentAttempt);
    if (group) {
      formData.append('student_group', group);
    }
    if (classFile) {
      formData.append('class_schedule_pdf', classFile);
    }
    if (assessmentFile) {
      formData.append('assessment_schedule_pdf', assessmentFile);
    }

    const response = await fetch(`${API_BASE}/parser/rosebank`, {
      method: 'POST',
      body: formData,
    });

    return readResponse<RosebankParseResponse>(response);
  },

  async exportAcademic(payload: AcademicSyncPayload): Promise<Blob> {
    const response = await fetch(`${API_BASE}/academic/export`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    return response.blob();
  },

  async exportAssessment(payload: AssessmentSyncPayload): Promise<Blob> {
    const response = await fetch(`${API_BASE}/assessment/export`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    return response.blob();
  },
};
