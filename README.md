# ROSEBANK-TIMETABLE-SYNC

## Web App MVP: Upload Timetable -> Google Calendar Sync

### Stack
- Backend: ASP.NET Core Web API (C#)
- Frontend: HTML/CSS/JavaScript
- PDF text extraction: PdfPig
- OCR: Tesseract CLI (real OCR for PNG/JPG/JPEG)
- Calendar integration: Google Calendar API

### Project Structure
- `backend/` ASP.NET API
- `frontend/` static web app

### MVP Timetable Format
Each line should follow:
`Monday 08:00-09:00 Mathematics`

### Modes
- `Academic`: parses weekly class lines and syncs recurring events.
- `Exam`: parses one-time assessment lines for preview (sync in next milestone).

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
- `POST /api/upload/preview` (`multipart/form-data`, fields: `file`, `mode`)
- `POST /api/upload/preview-text` (`application/json`, body: `{ "mode": "Academic|Exam", "text": "..." }`)
- `POST /api/upload/sync` (`application/json`)

### Notes
- PDF selectable text is implemented.
- Image OCR now uses Tesseract (`tesseract <image> stdout -l eng --oem 1 --psm 6`).
- Scanned PDFs without selectable text are detected and returned as actionable error (convert to image first, or add PDF-to-image OCR pipeline).
- Text fallback is available via `preview-text` for messy PDFs.
- OAuth token cache is stored in `backend/timetable-sync-token` after first auth.
