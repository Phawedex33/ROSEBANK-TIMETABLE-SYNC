import { FileText, Upload } from 'lucide-react';
import { useId, useState } from 'react';
import type { TimetableMode } from '../types';

interface FileDropProps {
  label: string;
  hint: string;
  file: File | null;
  onFileSelect: (file: File) => void;
  accept?: string;
  required?: boolean;
  disabled?: boolean;
}

function FileDrop({
  label,
  hint,
  file,
  onFileSelect,
  accept = '.pdf,.png,.jpg,.jpeg',
  required = false,
  disabled = false,
}: FileDropProps) {
  const inputId = useId();
  const [dragActive, setDragActive] = useState(false);

  function handleFile(candidate: File | null) {
    if (!candidate) return;
    onFileSelect(candidate);
  }

  return (
    <div
      onDragEnter={(event) => {
        event.preventDefault();
        if (!disabled) setDragActive(true);
      }}
      onDragOver={(event) => event.preventDefault()}
      onDragLeave={(event) => {
        event.preventDefault();
        setDragActive(false);
      }}
      onDrop={(event) => {
        event.preventDefault();
        setDragActive(false);
        handleFile(event.dataTransfer.files.item(0));
      }}
      className={`rounded-3xl border-2 border-dashed p-6 transition ${
        dragActive ? 'border-accent bg-accent/10' : 'border-white/10 bg-white/[0.03]'
      } ${disabled ? 'opacity-50' : ''}`}
    >
      <div className="flex items-start gap-4">
        <div className="rounded-2xl bg-white/5 p-3">
          {file ? <FileText className="text-accent" size={22} /> : <Upload className="text-white/60" size={22} />}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h3 className="font-semibold">{label}</h3>
            {required && <span className="chip border-accent/30 text-accent">Required</span>}
          </div>
          <p className="mt-1 text-sm text-white/60">{hint}</p>
          <p className="mt-4 truncate text-sm font-medium text-white/80">
            {file ? file.name : 'Drag and drop a file here or browse from disk.'}
          </p>
          <input
            id={inputId}
            type="file"
            accept={accept}
            className="hidden"
            disabled={disabled}
            onChange={(event) => handleFile(event.target.files?.[0] ?? null)}
          />
          <label htmlFor={inputId} className="button-secondary mt-4 cursor-pointer">
            Browse
          </label>
        </div>
      </div>
    </div>
  );
}

interface FileUploadProps {
  mode: TimetableMode;
  classFile: File | null;
  assessmentFile: File | null;
  onClassFileSelect: (file: File) => void;
  onAssessmentFileSelect: (file: File) => void;
  disabled?: boolean;
}

export function FileUpload({
  mode,
  classFile,
  assessmentFile,
  onClassFileSelect,
  onAssessmentFileSelect,
  disabled = false,
}: FileUploadProps) {
  const classRequired = mode === 'academic';
  const assessmentRequired = mode === 'assessment';

  return (
    <div className="panel p-6">
      <div className="mb-5">
        <p className="chip">Upload</p>
        <h2 className="mt-3 text-2xl font-bold">Load your Rosebank source files</h2>
        <p className="mt-2 text-sm text-white/60">
          {mode === 'academic'
            ? 'Academic mode needs the class timetable PDF and can optionally enrich the result with the PAS assessment PDF.'
            : 'Assessment mode only needs the PAS assessment PDF for the selected student year.'}
        </p>
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <FileDrop
          label="Class Schedule PDF"
          hint="Use the class timetable export for your year and group."
          file={classFile}
          onFileSelect={onClassFileSelect}
          required={classRequired}
          disabled={disabled}
        />
        <FileDrop
          label="Assessment PDF"
          hint={mode === 'assessment' ? 'Required for assessment parsing and export.' : 'Optional, but useful for assessment export too.'}
          file={assessmentFile}
          onFileSelect={onAssessmentFileSelect}
          required={assessmentRequired}
          disabled={disabled}
        />
      </div>
    </div>
  );
}
