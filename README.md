# ROSEBANK-TIMETABLE-SYNC

## Web App MVP: Upload Timetable -> Google Calendar Sync

### Stack
- Backend: ASP.NET Core Web API (C#)
- Frontend: React + TypeScript + Vite + TailwindCSS
- PDF text extraction: PdfPig
- OCR: Tesseract CLI for image inputs
- Calendar integration: Google Calendar API

### Project Structure
- `backend/` ASP.NET Core API and static-file host
- `frontend/` primary React frontend source and build output
- `tests/` backend test suite
- `timetable_examples/` sample PDFs for parser verification

### Current Frontend Flow
- Select mode: `academic` or `assessment`
- Upload the class timetable PDF
- Optionally add the assessment PDF
- Review parser diagnostics and event preview
- Connect Google OAuth
- Sync to Google Calendar

### Active API Endpoints
- `POST /api/parser/rosebank`
  Fields: `student_year`, `student_group`, `class_schedule_pdf`, optional `assessment_schedule_pdf`
- `POST /api/academic/sync`
  JSON payload with `year`, `group`, `events`, `timeZone`, optional `semesterEndDate`, optional `weeksDuration`
- `POST /api/assessment/sync`
  JSON payload with `events`, `timeZone`
- `POST /api/calendar/delete-synced`
  JSON payload with optional date window and mode filter
- `GET /oauth/google/status`
- `GET /oauth/google/start`
- `POST /oauth/google/disconnect`

### Legacy API Status
- `POST /api/academic/preview` returns `410 Gone`
- `POST /api/assessment/preview` returns `410 Gone`
- `POST /api/upload/*` legacy routes return `410 Gone`

### Backend Setup
1. Install .NET 8 SDK.
2. Install Tesseract OCR and confirm `tesseract --version` works.
3. Configure Google OAuth settings in `backend/appsettings.json`.
4. Run `dotnet restore`.
5. Run `dotnet run --project backend/TimetableSync.Api.csproj`.

App URL from launch settings: `https://localhost:7068`

### Frontend Setup
1. `cd frontend`
2. `npm install`
3. `npm run dev`

The Vite dev server proxies `/api` and `/oauth` to `https://localhost:7068`.

### Production Frontend Build
- Bash: `./build.sh`
- PowerShell: `./build-frontend.ps1`

The backend serves `frontend/dist` automatically when that build output exists.

### Verification
- Frontend tests: `cd frontend && npm run test`
- Frontend build: `cd frontend && npm run build`
- Backend tests: `dotnet test tests/TimetableSync.Api.Tests/TimetableSync.Api.Tests.csproj`

### Notes
- Assessment parsing now filters by selected year.
- Suspicious merged OCR assessment rows are skipped instead of leaking into sync previews.
- Academic recurring events default to 16 weeks if no explicit end date is supplied.
- Google token state is persisted through the encrypted file token store.
