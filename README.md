# ROSEBANK-TIMETABLE-SYNC

## Web App MVP: Upload Timetable -> Google Calendar Sync

### Stack
- Backend: ASP.NET Core Web API (C#)
- Frontend: HTML/CSS/JavaScript
- PDF text extraction: PdfPig (with stream-based fallback for difficult files)
- OCR: Tesseract CLI (real OCR for PNG/JPG/JPEG)
- Calendar integration: Google Calendar API

### Project Structure
- `backend/` ASP.NET API
- `frontend/` static web app

### MVP Timetable Format
Each line should follow:
`Monday 08:00-09:00 Mathematics`

### Modes
- `Academic`: Rosebank class timetable flow (year + group + recurring weekly sync).
- `Assessment`: Rosebank PAS flow (one-time assessment events with reminders).
- User can upload PDF/image for either timetable type, choose groups `GR1..GR20`, and select modules before syncing.

### Backend Setup
1. Install .NET 8 SDK.
2. Install Tesseract OCR and ensure `tesseract` works in terminal:
   - Example check: `tesseract --version`
3. In `backend/appsettings.json`, set:
   - `GoogleCalendar:ClientId`
   - `GoogleCalendar:ClientSecret`
4. Run:
   - `dotnet restore`
   - `dotnet run`

API default URL from launch profile: `https://localhost:7068`

### Frontend Setup
Frontend is now served by the backend from `frontend/`.
Open:
- `https://localhost:7068`

### Endpoints
- `POST /api/academic/preview` (`multipart/form-data`, fields: `file`, `year`, `group`)
- `POST /api/academic/sync` (`application/json`, body includes `year`, `group`, `events`, `timeZone`, optional `semesterEndDate` or `weeksDuration`)
- `POST /api/assessment/preview` (`multipart/form-data`, provide `file` or `text`)
- `POST /api/assessment/sync` (`application/json`, one-time assessment events)

### Legacy Endpoints (kept during transition)
- `POST /api/upload/preview` (`multipart/form-data`, fields: `file`, `mode`)
- `POST /api/upload/preview-text` (`application/json`, body: `{ "mode": "Academic|Exam", "text": "..." }`)
- `POST /api/upload/build-academic` (`application/json`, body with `group` and draft `rows`)
- `POST /api/upload/sync` (`application/json`, academic recurring events)
- `POST /api/upload/sync-exam` (`application/json`, exam one-time events)

### Notes
- PDF selectable text is implemented.
- Image OCR now uses Tesseract (`tesseract <image> stdout -l eng --oem 1 --psm 6`).
- Scanned PDFs without selectable text are detected and returned as actionable error (convert to image first, or add PDF-to-image OCR pipeline).
- Text fallback is available via `preview-text` for messy PDFs.
- Assessment sync creates one-time events with popup reminders (24h and 2h) and red color.
- Academic recurring events are created with `[CLASS]` prefix and blue color.
- Rosebank parsing heuristics added:
  - Academic: supports day/period grid-style lines (`Mo 3 ADDB6311 ...`, `ADDB6311 ... We 2`, optional `GR1/GR2/GR3`).
  - Assessment PAS: supports both single-line rows and fragmented multi-line module blocks, with fallback time defaults.
- UI includes a parser diagnostics panel showing which parser branch matched each generated row.
- Academic sync no longer requires semester end date from UI; default recurrence is 16 weeks unless `semesterEndDate` or `weeksDuration` is provided.
- OAuth token cache is stored in `backend/timetable-sync-token` after first auth.
