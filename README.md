# ROSEBANK-TIMETABLE-SYNC

## Web App MVP: Upload Timetable -> Calendar File Export

### Stack
- Backend: ASP.NET Core Web API (C#)
- Frontend: React + TypeScript + Vite + TailwindCSS
- PDF text extraction: PdfPig
- OCR: Tesseract CLI for image inputs
- Calendar integration: `.ics` file export for Google Calendar, Apple Calendar, Outlook, and phone calendar apps

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
- Download a calendar file
- Import it into your calendar app of choice

### Active API Endpoints
- `POST /api/parser/rosebank`
  Fields: `student_year`, `student_group`, `class_schedule_pdf`, optional `assessment_schedule_pdf`
- `POST /api/academic/export`
  JSON payload with `year`, `group`, `events`, `timeZone`, optional `semesterEndDate`, optional `weeksDuration`
- `POST /api/assessment/export`
  JSON payload with `events`, `timeZone`, optional `durationMinutes`

### Legacy API Status
- `POST /api/academic/preview` returns `410 Gone`
- `POST /api/assessment/preview` returns `410 Gone`
- `POST /api/upload/*` legacy routes return `410 Gone`

### Backend Setup
1. Install .NET 8 SDK.
2. Install Tesseract OCR and confirm `tesseract --version` works.
3. Run `dotnet restore`.
4. Run `dotnet run --project backend/TimetableSync.Api.csproj`.

App URL from launch settings: `https://localhost:7068`

### Frontend Setup
1. `cd frontend`
2. `npm install`
3. `npm run dev`

The Vite dev server proxies `/api` to `https://localhost:7068`.

### Production Frontend Build
- Bash: `./build.sh`
- PowerShell: `./build-frontend.ps1`

The backend serves `frontend/dist` automatically when that build output exists.

### Verification
- Frontend tests: `cd frontend && npm run test`
- Frontend build: `cd frontend && npm run build`
- Backend tests: `dotnet test tests/TimetableSync.Api.Tests/TimetableSync.Api.Tests.csproj`

### CI
- GitHub Actions workflow: [`.github/workflows/ci.yml`](/c:/Users/CASH/Desktop/ROSEBANK-TIMETABLE-SYNC/rosebank-timetable-sync/.github/workflows/ci.yml)
- Frontend job runs `npm ci`, `npm run test`, and `npm run build`
- Backend job runs `dotnet test tests/TimetableSync.Api.Tests/TimetableSync.Api.Tests.csproj`

### Notes
- Assessment parsing now filters by selected year.
- Suspicious merged OCR assessment rows are skipped instead of leaking into sync previews.
- Lower-quality duplicate assessment rows are pruned during parsing.
- Academic exports default to 16 weeks of weekly recurrence if no explicit end date is supplied.
- Google OAuth and direct Google Calendar mutation were removed in favor of calendar file export for safer public distribution.
